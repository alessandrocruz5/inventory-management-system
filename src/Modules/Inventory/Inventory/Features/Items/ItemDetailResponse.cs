using Inventory.Persistence;

namespace Inventory.Features.Items;

/// <summary>GET /items/{id}: the item plus its balance at each physical location (spec §9).</summary>
internal sealed record ItemDetailResponse(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive, byte[] Version,
    IReadOnlyList<ItemBalanceResponse> Balances, IReadOnlyList<ItemUnitResponse> Units)
{
    public static ItemDetailResponse From(Item item, IReadOnlyList<ItemBalanceResponse> balances, IReadOnlyList<ItemUnitResponse> units) =>
        new(item.Id, item.Sku, item.Name, item.BaseUnit, item.IsActive, item.RowVersion, balances, units);
}

/// <summary>One physical balance; average cost is derived, never stored (spec §5).</summary>
internal sealed record ItemBalanceResponse(Guid LocationId, string LocationCode, string LocationName,
    decimal OnHand, decimal StockValue, decimal? AverageCost, decimal? LastUnitCost);