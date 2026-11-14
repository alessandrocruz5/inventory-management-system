namespace Inventory.Features.Documents;

internal sealed record DocumentDetailResponse(
    Guid Id,
    string Type,
    string? ReasonCode,
    string? Reference,
    string? Notes,
    DateTimeOffset PostedAt,
    Guid PostedBy,
    Guid? ReversesDocumentId,
    Guid? ReversedByDocumentId,
    IReadOnlyList<DocumentLineResponse> Lines);

internal sealed record DocumentLineResponse(
    long MovementId,
    int LineNo,
    Guid ItemId,
    string Sku,
    string ItemName,
    string BaseUnit,
    Guid FromLocationId,
    string FromLocationCode,
    string FromLocationName,
    Guid ToLocationId,
    string ToLocationCode,
    string ToLocationName,
    decimal Quantity,
    decimal? UnitCost,
    decimal TotalCost,
    long? ReversesMovementId);