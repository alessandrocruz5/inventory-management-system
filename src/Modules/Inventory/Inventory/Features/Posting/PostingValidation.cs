using System.Globalization;

using Inventory.Domain;
using Inventory.Features.Posting.PostAdjustment;
using Inventory.Features.Posting.PostIssue;
using Inventory.Features.Posting.PostReceipt;
using Inventory.Features.Posting.PostTransfer;
using Inventory.Persistence;

using Microsoft.Extensions.Primitives;

namespace Inventory.Features.Posting;

/// <summary>
/// A line that passed step 1. <c>LineNo</c> is its 1-based position in the request. <c>Quantity</c> is greater than
/// zero, except on an adjustment, where it is the request's signed <c>quantityDelta</c> (never zero).
/// </summary>
internal sealed record ValidatedLine(int LineNo, Guid ItemId, decimal Quantity, decimal? UnitCost);

/// <summary>
/// A request that passed step 1, with its text normalized (trimmed, empty → null). On a transfer, <c>LocationId</c> is
/// the source.
/// </summary>
internal sealed record ValidatedPosting(Guid LocationId, string? Reference, string? Notes, IReadOnlyList<ValidatedLine> Lines)
{
    /// <summary>Transfers only: the destination. <see cref="LocationId"/> is then the source.</summary>
    public Guid? ToLocationId { get; init; }

    /// <summary>Adjustments only: the reason, matched exactly against the enum's names.</summary>
    public AdjustmentReason? ReasonCode { get; init; }
}

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

    private static readonly string[] ReasonNames = Enum.GetNames<AdjustmentReason>();

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
    /// Returns the normalized transfer, or null after adding an error per invalid field. Lines follow the issue rules;
    /// <c>LocationId</c> is the source and <c>ToLocationId</c> the destination, which must differ.
    /// </summary>
    public static ValidatedPosting? Transfer(PostTransferRequest request, Dictionary<string, string[]> errors)
    {
        var errorCount = errors.Count;

        var from = Location(request.FromLocationId, "fromLocationId", "Choose the location to move stock from.", errors);
        var to = Location(request.ToLocationId, "toLocationId", "Choose the location to move stock to.", errors);
        if (from is { } source && to is { } destination && source == destination)
        {
            errors["toLocationId"] = ["Choose a different location to move stock to."];
        }

        var (reference, notes, lines) = Body(
            request.Reference,
            request.Notes,
            request.Lines,
            line => (line.ItemId, line.Quantity, null),
            (path, quantity, unitCost) => StandardAmounts(path, quantity, unitCost, withUnitCost: false, errors),
            errors);

        return errors.Count > errorCount
            ? null
            : new ValidatedPosting(from.GetValueOrDefault(), reference, notes, lines) { ToLocationId = to };
    }

    /// <summary>
    /// Returns the normalized adjustment, or null after adding an error per invalid field. Each line's
    /// <c>Quantity</c> is the signed <c>quantityDelta</c>; only a line that adds stock may carry a unit cost.
    /// </summary>
    public static ValidatedPosting? Adjustment(PostAdjustmentRequest request, Dictionary<string, string[]> errors)
    {
        var errorCount = errors.Count;

        var location = Location(request.LocationId, "locationId", "Location is required.", errors);
        var reason = Reason(request.ReasonCode, errors);

        var (reference, notes, lines) = Body(
            request.Reference,
            request.Notes,
            request.Lines,
            line => (line.ItemId, line.QuantityDelta, line.UnitCost),
            (path, quantityDelta, unitCost) => AdjustmentAmounts(path, quantityDelta, unitCost, errors),
            errors);

        return errors.Count > errorCount
            ? null
            : new ValidatedPosting(location.GetValueOrDefault(), reference, notes, lines) { ReasonCode = reason };
    }

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

        Location(locationId, "locationId", "Location is required.", errors);

        var (cleanReference, cleanNotes, validLines) = Body(
            reference,
            notes,
            lines,
            read,
            (path, quantity, unitCost) => StandardAmounts(path, quantity, unitCost, withUnitCost, errors),
            errors);

        return errors.Count > errorCount
            ? null
            : new ValidatedPosting(locationId.GetValueOrDefault(), cleanReference, cleanNotes, validLines);
    }

    /// <summary>Returns the location, or null after adding <paramref name="message"/> when it is absent or empty.</summary>
    private static Guid? Location(Guid? value, string key, string message, Dictionary<string, string[]> errors)
    {
        if (value is null || value == Guid.Empty)
        {
            errors[key] = [message];
            return null;
        }

        return value;
    }

    /// <summary>
    /// The reference, notes and lines shared by every posting: text, the line count, null lines and item ids. Each
    /// line's quantity and unit cost go through <paramref name="amounts"/>, which returns a null quantity when it
    /// added an error; only lines with a valid quantity are returned.
    /// </summary>
    private static (string? Reference, string? Notes, List<ValidatedLine> Lines) Body<TLine>(
        string? reference,
        string? notes,
        IReadOnlyList<TLine?>? lines,
        Func<TLine, (Guid? ItemId, decimal? Quantity, decimal? UnitCost)> read,
        Func<string, decimal?, decimal?, (decimal? Quantity, decimal? UnitCost)> amounts,
        Dictionary<string, string[]> errors)
        where TLine : class
    {
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

                var (cleanQuantity, cleanCost) = amounts(path, quantity, unitCost);
                if (cleanQuantity is { } validQuantity)
                {
                    validLines.Add(new ValidatedLine(i + 1, item, validQuantity, cleanCost));
                }
            }
        }

        return (cleanReference, cleanNotes, validLines);
    }

    /// <summary>Receipt, issue and transfer lines: a positive quantity and, when carried, a unit cost.</summary>
    private static (decimal? Quantity, decimal? UnitCost) StandardAmounts(
        string path, decimal? quantity, decimal? unitCost, bool withUnitCost, Dictionary<string, string[]> errors)
    {
        var cleanQuantity = Amount(quantity, $"{path}.quantity", "Quantity", allowZero: false, errors);
        var cleanCost = withUnitCost
            ? Amount(unitCost, $"{path}.unitCost", "Unit cost", allowZero: true, errors)
            : null;

        // D1: the line's cost must fit decimal(19,4). Checked only when both factors are valid.
        if (cleanQuantity is { } q && cleanCost is { } c && CostingPolicy.Round4(q * c) > MaxColumn)
        {
            errors[$"{path}.unitCost"] = ["The line total is too large."];
        }

        return (cleanQuantity, cleanCost);
    }

    /// <summary>
    /// Adjustment lines: a signed, non-zero <c>quantityDelta</c> and an optional unit cost, allowed only when the
    /// delta adds stock. A positive line without a cost is valid: the engine uses the average or rejects it later.
    /// </summary>
    private static (decimal? Quantity, decimal? UnitCost) AdjustmentAmounts(
        string path, decimal? quantityDelta, decimal? unitCost, Dictionary<string, string[]> errors)
    {
        var cleanDelta = Delta(quantityDelta, $"{path}.quantityDelta", errors);

        decimal? cleanCost = null;
        if (unitCost is not null)
        {
            if (cleanDelta < 0m)
            {
                errors[$"{path}.unitCost"] = ["Leave the unit cost empty on a line that removes stock."];
            }
            else
            {
                cleanCost = Amount(unitCost, $"{path}.unitCost", "Unit cost", allowZero: true, errors);
            }
        }

        // D1: the line's cost must fit decimal(19,4). Checked only when both factors are valid.
        if (cleanDelta is { } q && q > 0m && cleanCost is { } c && CostingPolicy.Round4(q * c) > MaxColumn)
        {
            errors[$"{path}.unitCost"] = ["The line total is too large."];
        }

        return (cleanDelta, cleanCost);
    }

    /// <summary>
    /// Returns the reason when it equals one of the enum's names exactly (ordinal, untrimmed). Not
    /// <c>Enum.TryParse</c>: it would accept <c>"1"</c>, and <c>"damaged"</c> when ignoring case.
    /// </summary>
    private static AdjustmentReason? Reason(string? value, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors["reasonCode"] = ["Choose a reason."];
            return null;
        }

        if (!ReasonNames.Contains(value, StringComparer.Ordinal))
        {
            errors["reasonCode"] = [$"Choose one of: {string.Join(", ", ReasonNames)}."];
            return null;
        }

        return Enum.Parse<AdjustmentReason>(value);
    }

    /// <summary>Returns the signed delta when it is present, non-zero, in range and has at most 4 dp; otherwise adds an error.</summary>
    private static decimal? Delta(decimal? value, string key, Dictionary<string, string[]> errors)
    {
        string? message = value switch
        {
            null => "Quantity change is required.",
            0m => "Quantity change must not be zero.",
            > MaxAmount or < -MaxAmount => $"Quantity change must be between -{MaxAmountText} and {MaxAmountText}.",
            { } v when CostingPolicy.Round4(v) != v => "Quantity change can have at most 4 decimal places.",
            _ => null,
        };

        if (message is not null)
        {
            errors[key] = [message];
            return null;
        }

        return value;
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