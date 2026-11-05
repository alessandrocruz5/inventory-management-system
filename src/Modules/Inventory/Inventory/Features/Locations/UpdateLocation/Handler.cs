using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Locations.UpdateLocation;

internal sealed class UpdateLocationHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static readonly string[] DuplicateCode = ["A location with this code already exists."];

    public async Task<Results<Ok<LocationResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        UpdateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (code, name) = LocationRules.Normalize(request.Code, request.Name, errors);
        if (request.Version is not { Length: > 0 })
        {
            errors["version"] = ["Version is required."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var location = await db.Locations.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return Problems.NotFound();
        }

        if (location.Kind != LocationKind.Physical)
        {
            errors["location"] = ["System locations can't be changed."];
            return Problems.Validation(errors);
        }

        if (await db.Locations.AnyAsync(l => l.Code == code && l.Id != id, cancellationToken))
        {
            errors["code"] = DuplicateCode;
            return Problems.Validation(errors);
        }

        db.Entry(location).Property(l => l.RowVersion).OriginalValue = request.Version!;
        location.Code = code;
        location.Name = name;
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
        catch (DbUpdateException ex) when (Problems.IsUniqueViolation(ex))
        {
            errors["code"] = DuplicateCode;
            return Problems.Validation(errors);
        }

        return TypedResults.Ok(LocationResponse.From(location));
    }
}