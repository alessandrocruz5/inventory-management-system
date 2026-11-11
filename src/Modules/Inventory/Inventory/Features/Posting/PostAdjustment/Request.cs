namespace Inventory.Features.Posting.PostAdjustment;

/// <summary><c>ReasonCode</c> is a string, so an unknown value is a <c>400</c> on <c>reasonCode</c>, not a binding failure.</summary>
internal sealed record PostAdjustmentRequest(Guid? LocationId, string? ReasonCode, string? Reference, string? Notes, IReadOnlyList<PostAdjustmentLine?>? Lines);

internal sealed record PostAdjustmentLine(Guid? ItemId, decimal? QuantityDelta, decimal? UnitCost);