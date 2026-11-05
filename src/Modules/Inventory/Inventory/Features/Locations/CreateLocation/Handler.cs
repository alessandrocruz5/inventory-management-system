using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Locations.CreateLocation;

internal sealed class CreateLocationHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static readonly string[] DuplicateCode = ["A location with this code already exists."];

    public async Task<Results<Created<LocationResponse>, ValidationProblem>> HandleAsync(
        CreateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (code, name) = LocationRules.Normalize(request.Code, request.Name, errors);
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        // The unique code index also holds the reserved SUP, CUS and ADJ, so they are refused here too.
        if (await db.Locations.AnyAsync(l => l.Code == code, cancellationToken))
        {
            errors["code"] = DuplicateCode;
            return Problems.Validation(errors);
        }

        // IsActive is set explicitly: HasDefaultValue(true) makes false EF's sentinel.
        var location = new Location
        {
            Code = code,
            Name = name,
            Kind = LocationKind.Physical,
            AllowNegativeStock = false,
            IsActive = true,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user.UserId,
        };
        db.Locations.Add(location);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (Problems.IsUniqueViolation(ex))
        {
            errors["code"] = DuplicateCode;
            return Problems.Validation(errors);
        }

        return TypedResults.Created($"/api/locations/{location.Id}", LocationResponse.From(location));
    }
}