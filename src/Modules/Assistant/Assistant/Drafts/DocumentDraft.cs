namespace Assistant.Drafts;

/// <summary>
/// The posting limits a draft is checked against. Mirror PostingValidation; AssistantDraftTests pins them (CLAUDE.md Hidden coupling).
/// </summary>
internal static class DraftLimits
{
    public const decimal MaxAmount = 999_999_999.9999m;

    public const int MaxLines = 100;

    public const int MaxReferenceLength = 100;

    public const int MaxNotesLength = 500;

    public const int MaxDecimals = 4;

    /// <summary>At most this many drafts ride on one reply (plan D12).</summary>
    public const int MaxDraftsPerReply = 3;
}

/// <summary>
/// A validated proposal for a document. Nothing is posted: a person opens it in the post-stock form, checks it and posts it.
/// <see cref="Type"/> is lower case (<c>receipt|issue|transfer|adjustment</c>), as the Angular form takes it.
/// </summary>
internal sealed record DocumentDraft(
    string Type,
    DraftLocation? Location,
    DraftLocation? FromLocation,
    DraftLocation? ToLocation,
    string? ReasonCode,
    string? Reference,
    string? Notes,
    IReadOnlyList<DraftLine> Lines,
    IReadOnlyList<string> Warnings);

internal sealed record DraftLocation(Guid Id, string Code, string Name);

/// <summary><see cref="Unit"/> is null for the item's base unit. <see cref="Direction"/> is <c>add</c> or <c>remove</c> for an adjustment, else null.</summary>
internal sealed record DraftLine(
    Guid ItemId,
    string Sku,
    string ItemName,
    string BaseUnit,
    decimal Quantity,
    string? Unit,
    decimal? UnitCost,
    string? Direction);

/// <summary>Collects the valid drafts of one reply. Scoped: one instance per request.</summary>
internal sealed class DraftCollector
{
    private readonly List<DocumentDraft> _drafts = [];

    public IReadOnlyList<DocumentDraft> Drafts => _drafts;

    /// <summary>False, and the draft is dropped, once <see cref="DraftLimits.MaxDraftsPerReply"/> are collected.</summary>
    public bool TryAdd(DocumentDraft draft)
    {
        if (_drafts.Count >= DraftLimits.MaxDraftsPerReply)
        {
            return false;
        }

        _drafts.Add(draft);
        return true;
    }
}