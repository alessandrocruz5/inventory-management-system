using Inventory.Domain;
using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Stock.ListStock;

internal sealed class ListStockHandler(InventoryDbContext db)
{
    public async Task<Results<Ok<StockPageResponse>, ValidationProblem>> HandleAsync(
        ListStockRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (page, pageSize) = Paging.Resolve(request.Page, request.PageSize, errors);
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        // The tenant filter and RLS scope all three tables, so joining on Id alone is safe.
        var filtered = db.StockBalances.AsNoTracking().Where(b => !b.IsVirtual);
        if (request.ItemId is { } itemId)
        {
            filtered = filtered.Where(b => b.ItemId == itemId);
        }

        if (request.LocationId is { } locationId)
        {
            filtered = filtered.Where(b => b.LocationId == locationId);
        }

        var query = from b in filtered
                    join i in db.Items on b.ItemId equals i.Id
                    join l in db.Locations on b.LocationId equals l.Id
                    orderby i.Sku, l.Code
                    select new
                    {
                        b.ItemId,
                        i.Sku,
                        ItemName = i.Name,
                        i.BaseUnit,
                        b.LocationId,
                        LocationCode = l.Code,
                        LocationName = l.Name,
                        b.OnHand,
                        b.StockValue,
                        b.LastUnitCost,
                    };

        var result = await query.ToPageAsync(page, pageSize, cancellationToken);
        var rows = result.Items
            .Select(r => new StockRowResponse(
                r.ItemId,
                r.Sku,
                r.ItemName,
                r.BaseUnit,
                r.LocationId,
                r.LocationCode,
                r.LocationName,
                r.OnHand,
                r.StockValue,
                r.OnHand > 0 ? CostingPolicy.Round4(r.StockValue / r.OnHand) : null,
                r.LastUnitCost))
            .ToList();

        var total = await filtered.SumAsync(b => b.StockValue, cancellationToken);
        return TypedResults.Ok(new StockPageResponse(rows, result.Page, result.PageSize, result.Total, new StockTotals(total)));
    }
}