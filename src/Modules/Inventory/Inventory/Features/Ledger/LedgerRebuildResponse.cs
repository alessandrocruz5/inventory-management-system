namespace Inventory.Features.Ledger;

/// <summary>A ledger rebuild: how many balance keys it checked and the ones it overwrote (spec §6, §9).</summary>
internal sealed record LedgerRebuildResponse(DateTimeOffset RebuiltAt, int BalancesChecked, IReadOnlyList<BalanceDifference> Differences);

/// <summary>One balance the rebuild wrote. <c>Before</c> is null when the row was missing and the rebuild inserted it.</summary>
internal sealed record BalanceDifference(
    Guid ItemId, string Sku, Guid LocationId, string LocationCode, bool Inserted, BalanceSnapshot? Before, BalanceSnapshot After);

/// <summary>A balance's four ledger-derived values.</summary>
internal sealed record BalanceSnapshot(decimal OnHand, decimal StockValue, decimal? LastUnitCost, long? LastMovementId);