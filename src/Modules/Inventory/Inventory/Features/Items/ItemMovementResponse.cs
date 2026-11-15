namespace Inventory.Features.Items;

/// <summary>One movement at one physical location, with the running balance there after it (spec §9 stock card).</summary>
internal sealed record ItemMovementResponse(long MovementId, Guid DocumentId, string DocumentType, int LineNo,
    DateTimeOffset PostedAt, Guid PostedBy, string Direction,
    Guid LocationId, string LocationCode, string LocationName,
    Guid CounterpartLocationId, string CounterpartLocationCode, string CounterpartLocationName,
    decimal QuantityChange, decimal ValueChange, decimal RunningOnHand, decimal RunningValue);