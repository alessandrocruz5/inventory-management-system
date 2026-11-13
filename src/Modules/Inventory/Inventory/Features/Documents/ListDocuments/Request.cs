namespace Inventory.Features.Documents.ListDocuments;

internal sealed record ListDocumentsRequest(string? Type, DateTimeOffset? PostedFrom, DateTimeOffset? PostedTo, int? Page, int? PageSize);