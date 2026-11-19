using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.Units.DeactivateItemUnit;

internal sealed class DeactivateItemUnitHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    public Task<Results<Ok<ItemUnitResponse>, ProblemHttpResult>> HandleAsync(Guid id, string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToLowerInvariant();
        return db.Database.CreateExecutionStrategy().ExecuteAsync(ct => DeactivateAsync(id, normalized, ct), cancellationToken);
    }

    private async Task<Results<Ok<ItemUnitResponse>, ProblemHttpResult>> DeactivateAsync(Guid id, string code, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var unit = await db.ItemUnits.SingleOrDefaultAsync(u => u.ItemId == id && u.Code == code, cancellationToken);
        if (unit is null)
        {
            return Problems.NotFound();
        }

        // Already inactive: nothing to write, so the call is idempotent.
        if (!unit.IsActive)
        {
            return TypedResults.Ok(ItemUnitResponse.From(unit));
        }

        unit.IsActive = false;
        unit.UpdatedAt = clock.GetUtcNow();
        unit.UpdatedBy = user.UserId;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(ItemUnitResponse.From(unit));
    }
}