namespace Inventory.Domain;

/// <summary>Identifies a <c>StockBalances</c> row of the current tenant.</summary>
internal readonly record struct BalanceKey(Guid ItemId, Guid LocationId);

/// <summary>
/// A <c>StockBalances</c> row as the posting rules see it (spec §5). The engine takes <see cref="IsVirtual"/> and
/// <see cref="AllowNegative"/> from the step 7 location read, not from the row (spec §6). Virtual balances track
/// quantity only: their <see cref="StockValue"/> stays 0 and <see cref="LastUnitCost"/> stays null.
/// </summary>
internal sealed record Balance(
    Guid ItemId,
    Guid LocationId,
    bool IsVirtual,
    bool AllowNegative,
    decimal OnHand,
    decimal StockValue,
    decimal? LastUnitCost,
    long? LastMovementId)
{
    public BalanceKey Key => new(ItemId, LocationId);
}