namespace Inventory.Features.Posting.PostTransfer;

internal sealed record PostTransferRequest(Guid? FromLocationId, Guid? ToLocationId, string? Reference, string? Notes, IReadOnlyList<PostTransferLine?>? Lines);

internal sealed record PostTransferLine(Guid? ItemId, decimal? Quantity);