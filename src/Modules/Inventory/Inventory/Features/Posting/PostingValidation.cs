using System.Globalization;

using Inventory.Domain;
using Inventory.Features.Posting.PostIssue;
using Inventory.Features.Posting.PostReceipt;

using Microsoft.Extensions.Primitives;

namespace Inventory.Features.Posting;

/// <summary>A line that passed step 1. <c>LineNo</c> is its 1-based position in the request.</summary>
internal sealed record ValidatedLine(int LineNo, Guid ItemId, decimal Quantity, decimal? UnitCost);

/// <summary>A request that passed step 1, with its text normalized (trimmed, empty → null).</summary>
internal sealed record ValidatedPosting(Guid LocationId, string? Reference, string? Notes, IReadOnlyList<ValidatedLine> Lines);

/// <summary>
/// The pure step-1 checks of a posting (spec §6): shape, ranges and decimal places, the line-total guard (plan D1),
/// and the <c>Idempotency-Key</c> header. Existence, kind and active checks need the database and run later (plan D2).
/// </summary>
internal static class PostingValidation
{
    /// <summary>The largest quantity or unit cost a line may carry (spec §6 step 1).</summary>
    public const decimal MaxAmount = 999_999_999.9999m;

    /// <summary>The largest <c>decimal(19,4)</c>: line totals and balances must fit it (plan D1).</summary>
    public const decimal MaxColumn = 999_999_999_999_999.9999m;

    public const int MaxLines = 100;

    private const int MaxReferenceLength = 100;
    private const int MaxNotesLength = 500;
    private const int MaxKeyLength = 64;

    private static readonly string MaxAmountText = MaxAmount.ToString("N4", CultureInfo.InvariantCulture);

    /// <summary>Returns the normalized receipt, or null after adding an error per invalid field.</summary>
    public static ValidatedPosting? Receipt(PostReceiptRequest request, Dictionary<string, string[]> errors) =>
        Validate(
            request.LocationId,
            request.Reference,
            request.Notes,
            request.Lines,
            line => (line.ItemId, line.Quantity, line.UnitCost),
            withUnitCost: true,
            errors);

    /// <summary>Returns the normalized issue, or null after adding an error per invalid field.</summary>
    public static ValidatedPosting? Issue(PostIssueRequest request, Dictionary<string, string[]> errors) =>
        Validate(
            request.LocationId,
            request.Reference,
            request.Notes,
            request.Lines,
            line => (line.ItemId, line.Quantity, null),
            withUnitCost: false,
            errors);

    /// <summary>
    /// Checks the header as sent: no trimming or case folding. No value, or one empty or whitespace value, returns null
    /// with <paramref name="missing"/> set (<c>idempotency-key-missing</c>). More than one value, or a value that doesn't
    /// fit <c>varchar(64)</c> as printable ASCII, returns null with <c>errors["Idempotency-Key"]</c> (<c>validation</c>):
    /// joining repeated headers would post under a key the client never sent.
    /// </summary>
    public static string? IdempotencyKey(StringValues header, Dictionary<string, string[]> errors, out bool missing)
    {
        if (header.Count > 1)
        {
            missing = false;
            errors["Idempotency-Key"] = ["Send exactly one Idempotency-Key header."];
            return null;
        }

        var value = header.Count == 1 ? header[0] : null;
        if (string.IsNullOrWhiteSpace(value))
        {
            missing = true;
            return null;
        }

        missing = false;
        if (value.Length > MaxKeyLength || !value.All(c => c is >= '\x21' and <= '\x7E'))
        {
            errors["Idempotency-Key"] = ["Use 1–64 printable ASCII characters without spaces."];
            return null;
        }

        return value;
    }

    private static ValidatedPosting? Validate<TLine>(
        Guid? locationId,
        string? reference,
        string? notes,
        IReadOnlyList<TLine?>? lines,
        Func<TLine, (Guid? ItemId, decimal? Quantity, decimal? UnitCost)> read,
        bool withUnitCost,
        Dictionary<string, string[]> errors)
        where TLine : class
    {
        var errorCount = errors.Count;

        if (locationId is null || locationId == Guid.Empty)
        {
            errors["locationId"] = ["Location is required."];
        }

        var cleanReference = Text(reference, "reference", "Reference", MaxReferenceLength, errors);
        var cleanNotes = Text(notes, "notes", "Notes", MaxNotesLength, errors);

        var validLines = new List<ValidatedLine>();
        if (lines is null || lines.Count == 0)
        {
            errors["lines"] = ["Add at least one line."];
        }
        else if (lines.Count > MaxLines)
        {
            // The per-line checks are skipped, so an oversized body can't produce an unbounded error list.
            errors["lines"] = [$"A document can have at most {MaxLines} lines."];
        }
        else
        {
            var seenItems = new HashSet<Guid>();
            for (var i = 0; i < lines.Count; i++)
            {
                var path = $"lines[{i}]";
                if (lines[i] is not { } line)
                {
                    errors[path] = ["Line is required."];
                    continue;
                }

                var (itemId, quantity, unitCost) = read(line);

                var item = itemId.GetValueOrDefault();
                if (item == Guid.Empty)
                {
                    errors[$"{path}.itemId"] = ["Item is required."];
                }
                else if (!seenItems.Add(item))
                {
                    errors[$"{path}.itemId"] = ["This item is already on another line."];
                }

                var cleanQuantity = Amount(quantity, $"{path}.quantity", "Quantity", allowZero: false, errors);
                var cleanCost = withUnitCost
                    ? Amount(unitCost, $"{path}.unitCost", "Unit cost", allowZero: true, errors)
                    : null;

                // D1: the line's cost must fit decimal(19,4). Checked only when both factors are valid.
                if (cleanQuantity is { } q && cleanCost is { } c && CostingPolicy.Round4(q * c) > MaxColumn)
                {
                    errors[$"{path}.unitCost"] = ["The line total is too large."];
                }

                if (cleanQuantity is { } validQuantity)
                {
                    validLines.Add(new ValidatedLine(i + 1, item, validQuantity, cleanCost));
                }
            }
        }

        return errors.Count > errorCount
            ? null
            : new ValidatedPosting(locationId.GetValueOrDefault(), cleanReference, cleanNotes, validLines);
    }

    /// <summary>Trims; empty becomes null. Adds an error when longer than the column.</summary>
    private static string? Text(string? value, string key, string label, int maxLength, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            errors[key] = [$"{label} must be at most {maxLength} characters."];
        }

        return trimmed;
    }

    /// <summary>Returns the value when it is present, in range and has at most 4 dp; otherwise adds an error.</summary>
    private static decimal? Amount(decimal? value, string key, string label, bool allowZero, Dictionary<string, string[]> errors)
    {
        string? message = value switch
        {
            null => $"{label} is required.",
            <= 0m when !allowZero => $"{label} must be greater than zero.",
            < 0m => $"{label} must not be negative.",
            > MaxAmount => $"{label} must be at most {MaxAmountText}.",
            { } v when CostingPolicy.Round4(v) != v => $"{label} can have at most 4 decimal places.",
            _ => null,
        };

        if (message is not null)
        {
            errors[key] = [message];
            return null;
        }

        return value;
    }
}