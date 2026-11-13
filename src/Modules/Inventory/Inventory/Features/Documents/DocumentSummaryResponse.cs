namespace Inventory.Features.Documents;

internal sealed record DocumentSummaryResponse(Guid Id, string Type, string? ReasonCode, string? Reference, DateTimeOffset PostedAt,
    Guid PostedBy, int LineCount, Guid? ReversesDocumentId, Guid? ReversedByDocumentId);