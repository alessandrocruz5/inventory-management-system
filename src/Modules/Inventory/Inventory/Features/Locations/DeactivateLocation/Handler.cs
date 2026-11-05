using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Locations.DeactivateLocation;

internal sealed class DeactivateLocationHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    public async Task<Results<Ok<LocationResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
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
        if (location.IsActive)
        {
            // Spec §9 allows deactivation only with zero stock; that check (with UPDLOCK, HOLDLOCK on the balance rows) lands with the ledger tables (S5a).
            location.IsActive = false;
            location.UpdatedAt = clock.GetUtcNow();
            location.UpdatedBy = user.UserId;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Problems.ConcurrencyConflict();
            }
        }

        return TypedResults.Ok(LocationResponse.From(location));
    }
}