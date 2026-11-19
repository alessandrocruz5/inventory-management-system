using Inventory.Domain;
using Inventory.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Stocktakes;

/// <summary>Reads a stocktake's detail; shared by the stocktake routes so every response has the same shape and line order.</summary>
internal sealed class StocktakeReader(InventoryDbContext db)
{
    /// <summary>The stocktake with its lines and the current balance of each line's item at its location, or null when it doesn't exist.</summary>
    public async Task<StocktakeDetailResponse?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var stocktake = await db.Stocktakes.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stocktake is null)
        {
            return null;
        }

        var location = await db.Locations.AsNoTracking().SingleAsync(l => l.Id == stocktake.LocationId, cancellationToken);

        // The tenant filter and RLS scope every table, so joining on the ids alone is safe.
        var rows = await (
            from line in db.StocktakeLines.AsNoTracking()
            where line.StocktakeId == id
            join item in db.Items on line.ItemId equals item.Id
            join balance in db.StockBalances.Where(b => b.LocationId == stocktake.LocationId && !b.IsVirtual)
                on line.ItemId equals balance.ItemId into balances
            from balance in balances.DefaultIfEmpty()
            select new Row(
                line,
                item.Sku,
                item.Name,
                item.BaseUnit,
                item.IsActive,
                balance != null ? balance.OnHand : 0m,
                balance != null ? balance.StockValue : 0m,
                balance != null ? balance.LastMovementId : null)).ToListAsync(cancellationToken);

        var lines = InLineOrder(rows, r => r.Sku).Select(ToResponse).ToList();

        return new StocktakeDetailResponse(
            stocktake.Id,
            stocktake.LocationId,
            location.Code,
            location.Name,
            stocktake.Status.ToString(),
            stocktake.Notes,
            stocktake.CreatedAt,
            stocktake.CreatedBy,
            stocktake.UpdatedAt,
            stocktake.PostedDocumentId,
            lines);
    }

    /// <summary>
    /// The one line order (plan D14): <c>Sku</c>, ordinal, in C#, so it doesn't depend on the SQL collation. Posting maps
    /// error indexes back to lines through this same order.
    /// </summary>
    internal static IReadOnlyList<T> InLineOrder<T>(IEnumerable<T> lines, Func<T, string> sku) =>
        [.. lines.OrderBy(sku, StringComparer.Ordinal)];

    private static StocktakeLineResponse ToResponse(Row row)
    {
        var line = row.Line;
        var counted = line.CountedQuantity is not null;
        return new StocktakeLineResponse(
            line.ItemId,
            row.Sku,
            row.Name,
            row.BaseUnit,
            row.ItemIsActive,
            line.ExpectedQuantity,
            line.CountedQuantity,
            line.CountedQuantity - line.ExpectedQuantity,
            line.UnitCost,
            line.CountedAt,
            line.CountedBy,
            row.OnHand,
            row.OnHand > 0 ? CostingPolicy.Round4(row.StockValue / row.OnHand) : null,
            // Null-safe: a balance created after a count of a never-stocked item has moved too.
            counted && row.LastMovementId != line.ExpectedLastMovementId);
    }

    private sealed record Row(
        StocktakeLine Line,
        string Sku,
        string Name,
        string BaseUnit,
        bool ItemIsActive,
        decimal OnHand,
        decimal StockValue,
        long? LastMovementId);
}