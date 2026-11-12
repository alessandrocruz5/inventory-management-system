using Inventory.Domain;

namespace Inventory.Persistence;

/// <summary>
/// A posting that passed step 1 and the step 2 lookup (spec §6, plan D2). <see cref="Lines"/> carry resolved From and To
/// locations, numbered 1..n. <see cref="PostedBy"/> comes from the caller, so a seeder can pass its own principal.
/// <see cref="ReasonCode"/> is set on an adjustment, null on every other type, including a reversal of an adjustment
/// (<c>CK_StockDocuments_ReasonCode</c>).
/// </summary>
internal sealed record PostingCommand(
    PostingKind Kind,
    StockDocumentType DocumentType,
    IReadOnlyList<PostingLine> Lines,
    string IdempotencyKey,
    byte[] RequestHash,
    string? Reference,
    string? Notes,
    Guid PostedBy,
    AdjustmentReason? ReasonCode);

/// <summary>What <see cref="PostingEngine"/> did with a posting. Only <see cref="PostedOutcome"/> wrote anything.</summary>
internal abstract record PostingOutcome;

/// <summary>
/// The document, its movements in <c>LineNo</c> order and the physical balances it touched, ordered by <c>ItemId</c>
/// then <c>LocationId</c> as SQL Server sorts <c>uniqueidentifier</c> (plan D3). <see cref="BalancesAsOf"/> is the
/// commit's <c>PostedAt</c>, or the read time on a replay.
/// </summary>
internal sealed record PostedOutcome(
    StockDocument Document,
    IReadOnlyList<StockMovement> Movements,
    IReadOnlyList<StockBalance> Balances,
    bool Replayed,
    DateTimeOffset BalancesAsOf) : PostingOutcome;

/// <summary>The key belongs to a document posted from a different request (spec §6, step 2).</summary>
internal sealed record KeyReusedOutcome(Guid OriginalDocumentId) : PostingOutcome;

/// <summary>The step 7 re-read found the item inactive.</summary>
internal sealed record InactiveItemOutcome(Guid ItemId) : PostingOutcome;

/// <summary>The step 7 re-read found the location inactive.</summary>
internal sealed record InactiveLocationOutcome(Guid LocationId) : PostingOutcome;

/// <summary>Step 8 rejected the posting (Domain rejections).</summary>
internal sealed record RejectedOutcome(PostingRejectedException Rejection) : PostingOutcome;

/// <summary>A resulting <c>OnHand</c> or <c>StockValue</c> wouldn't fit <c>decimal(19,4)</c> (plan D1).</summary>
internal sealed record BalanceOutOfRangeOutcome(Guid ItemId, Guid LocationId) : PostingOutcome;

/// <summary>Every retry failed with a transient error (deadlock 1205 included): <c>409 concurrency-conflict</c>.</summary>
internal sealed record RetriesExhaustedOutcome : PostingOutcome;