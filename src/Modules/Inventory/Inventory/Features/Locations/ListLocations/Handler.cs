using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Locations.ListLocations;

internal sealed class ListLocationsHandler(InventoryDbContext db)
{
    public async Task<Results<Ok<PagedResponse<LocationResponse>>, ValidationProblem>> HandleAsync(
        ListLocationsRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (page, pageSize) = Paging.Resolve(request.Page, request.PageSize, errors);
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        // Physical locations first, then the virtual ones; inactive rows are included.
        var result = await db.Locations.AsNoTracking()
            .OrderBy(l => l.Kind == LocationKind.Physical ? 0 : 1)
            .ThenBy(l => l.Code)
            .ToPageAsync(page, pageSize, cancellationToken);
        return TypedResults.Ok(new PagedResponse<LocationResponse>(
            result.Items.Select(LocationResponse.From).ToList(), result.Page, result.PageSize, result.Total));
    }
}