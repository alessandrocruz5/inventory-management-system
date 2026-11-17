namespace Inventory.Features.Ledger;

/// <summary>The tenant's ledger health: INV-1 to INV-5 with their failing rows (spec §6, §9).</summary>
internal sealed record LedgerHealthResponse(DateTimeOffset CheckedAt, bool Healthy, IReadOnlyList<InvariantResult> Invariants);

/// <summary>One invariant. <c>Failures</c> holds at most 100 rows; <c>FailureCount</c> is the full count.</summary>
internal sealed record InvariantResult(string Id, string Name, bool Passed, int FailureCount, IReadOnlyList<InvariantFailure> Failures);

/// <summary>One failing check on a balance key or a movement, with the value the ledger expects and the one found.</summary>
internal sealed record InvariantFailure(
    Guid? ItemId, string? Sku, Guid? LocationId, string? LocationCode, long? MovementId, string Check, decimal? Expected, decimal? Actual);