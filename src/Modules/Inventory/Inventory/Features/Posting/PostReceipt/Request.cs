namespace Inventory.Features.Posting.PostReceipt;

internal sealed record PostReceiptRequest(Guid? LocationId, string? Reference, string? Notes, IReadOnlyList<PostReceiptLine?>? Lines);

internal sealed record PostReceiptLine(Guid? ItemId, decimal? Quantity, decimal? UnitCost);