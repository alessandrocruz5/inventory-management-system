using Inventory.Domain;
using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.GetItem;

/// <summary>The item with its balance at each physical location (spec §9).</summary>
internal sealed class GetItemHandler(InventoryDbContext db)
{
    public async Task<Results<Ok<ItemDetailResponse>, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return Problems.NotFound();
        }

        // The tenant filter and RLS scope all three tables, so joining on Id alone is safe.
        var rows = await (
            from b in db.StockBalances.AsNoTracking()
            where b.ItemId == id && !b.IsVirtual
            join l in db.Locations on b.LocationId equals l.Id
            orderby l.Code
            select new { b.LocationId, l.Code, l.Name, b.OnHand, b.StockValue, b.LastUnitCost }).ToListAsync(cancellationToken);

        var balances = rows
            .Select(r => new ItemBalanceResponse(
                r.LocationId,
                r.Code,
                r.Name,
                r.OnHand,
                r.StockValue,
                r.OnHand > 0 ? CostingPolicy.Round4(r.StockValue / r.OnHand) : null,
                r.LastUnitCost))
            .ToList();

        // Inactive units are listed, flagged by isActive. Ordinal, so the order doesn't depend on the SQL collation.
        var units = (await db.ItemUnits.AsNoTracking().Where(u => u.ItemId == id).ToListAsync(cancellationToken))
            .OrderBy(u => u.Code, StringComparer.Ordinal)
            .Select(ItemUnitResponse.From)
            .ToList();

        return TypedResults.Ok(ItemDetailResponse.From(item, balances, units));
    }
}