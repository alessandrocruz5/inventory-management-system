using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Locations.DeactivateLocation;

internal sealed class DeactivateLocationHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    public Task<Results<Ok<LocationResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        // The lock below lasts until commit, so this runs in a transaction inside the retrying strategy (TASKS.md, IMS-3).
        db.Database.CreateExecutionStrategy().ExecuteAsync(ct => DeactivateAsync(id, ct), cancellationToken);

    private async Task<Results<Ok<LocationResponse>, ValidationProblem, ProblemHttpResult>> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        // A retry starts clean: a row tracked by the failed attempt would hide the fresh read.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var location = await db.Locations.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return Problems.NotFound();
        }

        if (location.Kind != LocationKind.Physical)
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["location"] = ["System locations can't be changed."] });
        }

        // Already inactive: nothing to change, so the call is idempotent.
        if (!location.IsActive)
        {
            return TypedResults.Ok(LocationResponse.From(location));
        }

        // Spec §6: lock the location's balances through IX_Balances_Location, then check them, then update the location.
        // HOLDLOCK also locks the empty key range, so a posting can't add a balance row here before we commit. The INDEX
        // and FORCESEEK hints keep that range to this location: a scan would range-lock the tenant's whole slice.
        var blocking = await db.StockBalances
            .FromSql($"SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, HOLDLOCK, FORCESEEK, INDEX([IX_Balances_Location])) WHERE [LocationId] = {id}")
            .AsNoTracking()
            .Where(b => b.OnHand != 0 || b.StockValue != 0)
            .OrderBy(b => b.ItemId)
            .ToListAsync(cancellationToken);
        if (blocking.Count > 0)
        {
            return Problems.StockNotZero(blocking);
        }

        location.IsActive = false;
        location.UpdatedAt = clock.GetUtcNow();
        location.UpdatedBy = user.UserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // No version is sent, so a racing deactivate that won first still counts as success.
            await db.Entry(location).ReloadAsync(cancellationToken);
            if (location.IsActive)
            {
                return Problems.ConcurrencyConflict();
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(LocationResponse.From(location));
    }
}