namespace Inventory.Features.Posting.PostIssue;

internal sealed record PostIssueRequest(Guid? LocationId, string? Reference, string? Notes, IReadOnlyList<PostIssueLine?>? Lines);

internal sealed record PostIssueLine(Guid? ItemId, decimal? Quantity);