using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.ListItems;

internal sealed class ListItemsHandler(InventoryDbContext db)
{
    private const int MaxSearchLength = 200;

    public async Task<Results<Ok<PagedResponse<ItemResponse>>, ValidationProblem>> HandleAsync(
        ListItemsRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (page, pageSize) = Paging.Resolve(request.Page, request.PageSize, errors);
        var search = request.Search?.Trim();
        if (search is { Length: > MaxSearchLength })
        {
            errors["search"] = ["Search must be at most 200 characters."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var query = db.Items.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(i => i.Sku.Contains(search) || i.Name.Contains(search));
        }

        if (request.Active is { } active)
        {
            query = query.Where(i => i.IsActive == active);
        }

        var result = await query
            .OrderBy(i => i.Sku)
            .Select(i => new ItemResponse(i.Id, i.Sku, i.Name, i.BaseUnit, i.IsActive, i.RowVersion))
            .ToPageAsync(page, pageSize, cancellationToken);
        return TypedResults.Ok(result);
    }
}