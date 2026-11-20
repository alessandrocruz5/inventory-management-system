using Assistant.Drafts;

namespace Assistant.Tools;

/// <summary>
/// <c>draft_document</c>: validates a proposed document and keeps the valid ones for the reply. It posts nothing and
/// never reaches a posting handler: a person opens the draft in the post-stock form (plan D8).
/// </summary>
internal sealed class DraftTool(DraftValidator validator, DraftCollector collector)
{
    public async Task<object> DraftAsync(
        string role,
        string type,
        string? location,
        string? fromLocation,
        string? toLocation,
        string? reasonCode,
        string? reference,
        string? notes,
        DraftLineRequest[] lines,
        CancellationToken ct)
    {
        var outcome = await validator.ValidateAsync(
            new DraftRequest(type, location, fromLocation, toLocation, reasonCode, reference, notes, lines), role, ct);

        if (outcome.Draft is not { } draft)
        {
            return new { ok = false, errors = outcome.Errors };
        }

        if (!collector.TryAdd(draft))
        {
            return new { ok = false, errors = new[] { $"At most {DraftLimits.MaxDraftsPerReply} drafts per reply." } };
        }

        var where = draft.Type == "transfer"
            ? $"{draft.FromLocation!.Code} to {draft.ToLocation!.Code}"
            : draft.Location!.Code;
        var title = char.ToUpperInvariant(draft.Type[0]) + draft.Type[1..];
        return new
        {
            ok = true,
            draft = collector.Drafts.Count,
            summary = $"{title} at {where}, {draft.Lines.Count} line(s). Not posted: the user must open it with Review and post.",
            warnings = draft.Warnings,
        };
    }
}