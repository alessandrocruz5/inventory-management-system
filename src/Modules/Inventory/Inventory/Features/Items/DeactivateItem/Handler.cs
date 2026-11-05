using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.DeactivateItem;

internal sealed class DeactivateItemHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    public Task<Results<Ok<ItemResponse>, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        // The lock below lasts until commit, so this runs in a transaction inside the retrying strategy (TASKS.md, IMS-3).
        db.Database.CreateExecutionStrategy().ExecuteAsync(ct => DeactivateAsync(id, ct), cancellationToken);

    private async Task<Results<Ok<ItemResponse>, ProblemHttpResult>> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        // A retry starts clean: a row tracked by the failed attempt would hide the fresh read.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return Problems.NotFound();
        }

        // Already inactive: nothing to change, so the call is idempotent.
        if (!item.IsActive)
        {
            return TypedResults.Ok(ItemResponse.From(item));
        }

        // Spec §6: lock the item's balances, then check them, then update the item. HOLDLOCK also locks the empty
        // key range, so a posting can't add a balance row for this item before we commit. FORCESEEK keeps that
        // range to this item: a scan would range-lock the tenant's whole slice.
        var blocking = await db.StockBalances
            .FromSql($"SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, HOLDLOCK, FORCESEEK) WHERE [ItemId] = {id}")
            .AsNoTracking()
            .Where(b => !b.IsVirtual && (b.OnHand != 0 || b.StockValue != 0))
            .OrderBy(b => b.LocationId)
            .ToListAsync(cancellationToken);
        if (blocking.Count > 0)
        {
            return Problems.StockNotZero(blocking);
        }

        item.IsActive = false;
        item.UpdatedAt = clock.GetUtcNow();
        item.UpdatedBy = user.UserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // No version is sent, so a racing deactivate that won first still counts as success.
            await db.Entry(item).ReloadAsync(cancellationToken);
            if (item.IsActive)
            {
                return Problems.ConcurrencyConflict();
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(ItemResponse.From(item));
    }
}