using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.DeactivateItem;

internal sealed class DeactivateItemHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    public async Task<Results<Ok<ItemResponse>, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return Problems.NotFound();
        }

        // Already inactive: nothing to change, so the call is idempotent.
        if (item.IsActive)
        {
            // Spec §9 allows deactivation only with zero physical stock; that check lands with the ledger tables (S5a).
            item.IsActive = false;
            item.UpdatedAt = clock.GetUtcNow();
            item.UpdatedBy = user.UserId;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Problems.ConcurrencyConflict();
            }
        }

        return TypedResults.Ok(ItemResponse.From(item));
    }
}