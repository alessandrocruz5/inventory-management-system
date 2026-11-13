namespace Inventory.Features.Stock;

/// <summary>One item at one physical location, with its average cost derived from the balance (spec §9).</summary>
internal sealed record StockRowResponse(
    Guid ItemId,
    string Sku,
    string ItemName,
    string BaseUnit,
    Guid LocationId,
    string LocationCode,
    string LocationName,
    decimal OnHand,
    decimal StockValue,
    decimal? AverageCost,
    decimal? LastUnitCost);

/// <summary>Totals over the whole filter, not just the returned page.</summary>
internal sealed record StockTotals(decimal StockValue);

/// <summary>One page of stock rows plus the totals for the whole filter.</summary>
internal sealed record StockPageResponse(IReadOnlyList<StockRowResponse> Items, int Page, int PageSize, int Total, StockTotals Totals);