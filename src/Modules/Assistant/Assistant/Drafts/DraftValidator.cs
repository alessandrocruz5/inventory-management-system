using System.ComponentModel;
using System.Globalization;

using Inventory.Contracts;

using Kernel.Tenancy;

namespace Assistant.Drafts;

internal sealed record DraftRequest(
    string? Type,
    string? Location,
    string? FromLocation,
    string? ToLocation,
    string? ReasonCode,
    string? Reference,
    string? Notes,
    IReadOnlyList<DraftLineRequest>? Lines);

internal sealed record DraftLineRequest(
    [property: Description("The item's SKU.")] string? Sku,
    [property: Description("More than 0, in the given unit, at most 4 decimal places.")] decimal? Quantity,
    [property: Description("Optional unit code of the item. Omit for the base unit.")] string? Unit,
    [property: Description("PHP per given unit. Required on a receipt; allowed on an adjustment add; otherwise omit.")] decimal? UnitCost,
    [property: Description("Adjustment only: add or remove.")] string? Direction);

/// <summary><see cref="Draft"/> is set when <see cref="Errors"/> is empty. Errors read like <c>lines[1].quantity: Must be more than 0.</c></summary>
internal sealed record DraftOutcome(DocumentDraft? Draft, IReadOnlyList<string> Errors);

/// <summary>
/// Checks a proposed document against master data and the posting limits (spec §6 step 1, spec §9) without posting it.
/// It reads only through <see cref="IInventoryReader"/>, which is tenant-scoped. The role is checked here, never by the model (plan D11).
/// </summary>
internal sealed class DraftValidator(IInventoryReader reader)
{
    private static readonly string[] Types = ["receipt", "issue", "transfer", "adjustment"];

    private static readonly string[] Reasons = ["Damaged", "Lost", "Found", "CountCorrection", "Other"];

    public async Task<DraftOutcome> ValidateAsync(DraftRequest request, string role, CancellationToken ct)
    {
        var errors = new List<string>();

        var type = Types.FirstOrDefault(t => string.Equals(t, request.Type?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (type is null)
        {
            return Fail($"type: Use one of {string.Join(", ", Types)}.");
        }

        if (type == "adjustment" && !string.Equals(role, RoleNames.Admin, StringComparison.Ordinal))
        {
            return Fail("type: Only an Admin can correct stock.");
        }

        var locations = (await reader.ListLocationsAsync(ct)).Where(l => l.IsPhysical && l.IsActive).ToList();
        DraftLocation? location = null;
        DraftLocation? from = null;
        DraftLocation? to = null;
        if (type == "transfer")
        {
            Absent(request.Location, "location", "Use fromLocation and toLocation for a transfer.");
            from = Resolve(request.FromLocation, "fromLocation");
            to = Resolve(request.ToLocation, "toLocation");
            if (from is not null && to is not null && from.Id == to.Id)
            {
                errors.Add("toLocation: Choose a different location to move stock to.");
            }
        }
        else
        {
            Absent(request.FromLocation, "fromLocation", "Only a transfer has fromLocation.");
            Absent(request.ToLocation, "toLocation", "Only a transfer has toLocation.");
            location = Resolve(request.Location, "location");
        }

        string? reason = null;
        if (type == "adjustment")
        {
            reason = Reasons.FirstOrDefault(r => string.Equals(r, request.ReasonCode?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (reason is null)
            {
                errors.Add($"reasonCode: Use one of {string.Join(", ", Reasons)}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            errors.Add("reasonCode: Only an adjustment has a reason.");
        }

        var reference = Clean(request.Reference);
        if (reference is { Length: > DraftLimits.MaxReferenceLength })
        {
            errors.Add($"reference: Use at most {DraftLimits.MaxReferenceLength} characters.");
        }

        var notes = Clean(request.Notes);
        if (notes is { Length: > DraftLimits.MaxNotesLength })
        {
            errors.Add($"notes: Use at most {DraftLimits.MaxNotesLength} characters.");
        }

        var lines = new List<DraftLine>();
        var sources = new List<(int Index, DraftLine Line, decimal BaseQuantity)>();
        if (request.Lines is not { Count: > 0 })
        {
            errors.Add("lines: Add at least one line.");
        }
        else if (request.Lines.Count > DraftLimits.MaxLines)
        {
            errors.Add($"lines: Use at most {DraftLimits.MaxLines} lines.");
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < request.Lines.Count; i++)
            {
                var line = await ValidateLineAsync(i, request.Lines[i], type, seen, ct);
                if (line is { } valid)
                {
                    lines.Add(valid.Line);
                    sources.Add((i, valid.Line, valid.BaseQuantity));
                }
            }
        }

        if (errors.Count > 0)
        {
            return new DraftOutcome(null, errors);
        }

        var warnings = new List<string>();
        var source = type == "transfer" ? from : location;
        foreach (var (index, line, baseQuantity) in sources)
        {
            if (IsOutflow(type, line.Direction) && source is not null)
            {
                var stock = await reader.GetStockAsync(line.ItemId, source.Id, 1, ct);
                var onHand = stock.Rows.Sum(r => r.OnHand);
                if (baseQuantity > onHand)
                {
                    warnings.Add($"lines[{index}]: Only {Format(onHand)} on hand at {source.Code}.");
                }
            }
        }

        return new DraftOutcome(new DocumentDraft(type, location, from, to, reason, reference, notes, lines, warnings), []);

        DraftOutcome Fail(string error) => new(null, [error]);

        void Absent(string? value, string key, string message)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{key}: {message}");
            }
        }

        DraftLocation? Resolve(string? code, string key)
        {
            var found = string.IsNullOrWhiteSpace(code)
                ? null
                : locations.FirstOrDefault(l => string.Equals(l.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                errors.Add(string.IsNullOrWhiteSpace(code)
                    ? $"{key}: Required."
                    : $"{key}: No active location with code {code.Trim()}.");
                return null;
            }

            return new DraftLocation(found.Id, found.Code, found.Name);
        }

        async Task<(DraftLine Line, decimal BaseQuantity)?> ValidateLineAsync(
            int i, DraftLineRequest? raw, string documentType, HashSet<string> skus, CancellationToken token)
        {
            var path = $"lines[{i}]";
            if (raw is null)
            {
                errors.Add($"{path}: Required.");
                return null;
            }

            var before = errors.Count;
            ItemSnapshot? item = null;
            var sku = raw.Sku?.Trim();
            if (string.IsNullOrEmpty(sku))
            {
                errors.Add($"{path}.sku: Required.");
            }
            else if (!skus.Add(sku))
            {
                errors.Add($"{path}.sku: SKU {sku} appears twice; combine the quantities into one line.");
            }
            else
            {
                item = await reader.GetItemBySkuAsync(sku, token);
                if (item is null || !item.IsActive)
                {
                    errors.Add($"{path}.sku: No active item with SKU {sku}.");
                    item = null;
                }
            }

            decimal quantity = 0;
            if (raw.Quantity is not { } q || q <= 0)
            {
                errors.Add($"{path}.quantity: Must be more than 0.");
            }
            else if (q > DraftLimits.MaxAmount)
            {
                errors.Add($"{path}.quantity: Must be at most {Format(DraftLimits.MaxAmount)}.");
            }
            else if (decimal.Round(q, DraftLimits.MaxDecimals, MidpointRounding.AwayFromZero) != q)
            {
                errors.Add($"{path}.quantity: Use at most {DraftLimits.MaxDecimals} decimal places.");
            }
            else
            {
                quantity = q;
            }

            string? unit = null;
            var factor = 1m;
            var unitText = raw.Unit?.Trim();
            if (!string.IsNullOrEmpty(unitText) && item is not null
                && !string.Equals(unitText, item.BaseUnit, StringComparison.OrdinalIgnoreCase))
            {
                var found = item.Units.FirstOrDefault(u => u.IsActive && string.Equals(u.Code, unitText, StringComparison.OrdinalIgnoreCase));
                if (found is null)
                {
                    errors.Add($"{path}.unit: {item.Sku} has no active unit {unitText}. Use {item.BaseUnit} or omit it.");
                }
                else
                {
                    unit = found.Code;
                    factor = found.Factor;
                }
            }

            string? direction = null;
            if (documentType == "adjustment")
            {
                direction = raw.Direction?.Trim().ToLowerInvariant();
                if (direction is not ("add" or "remove"))
                {
                    errors.Add($"{path}.direction: Use add or remove.");
                    direction = null;
                }
            }
            else if (!string.IsNullOrWhiteSpace(raw.Direction))
            {
                errors.Add($"{path}.direction: Only an adjustment has a direction.");
            }

            var takesCost = documentType == "receipt" || (documentType == "adjustment" && direction == "add");
            decimal? unitCost = null;
            if (raw.UnitCost is { } cost)
            {
                if (!takesCost)
                {
                    if (documentType != "adjustment" || direction is not null)
                    {
                        errors.Add($"{path}.unitCost: Leave it out; only a receipt and an adjustment add take a unit cost.");
                    }
                }
                else if (cost < 0)
                {
                    errors.Add($"{path}.unitCost: Must not be negative.");
                }
                else if (cost > DraftLimits.MaxAmount)
                {
                    errors.Add($"{path}.unitCost: Must be at most {Format(DraftLimits.MaxAmount)}.");
                }
                else if (decimal.Round(cost, DraftLimits.MaxDecimals, MidpointRounding.AwayFromZero) != cost)
                {
                    errors.Add($"{path}.unitCost: Use at most {DraftLimits.MaxDecimals} decimal places.");
                }
                else
                {
                    unitCost = cost;
                }
            }
            else if (documentType == "receipt")
            {
                errors.Add($"{path}.unitCost: Required on a receipt.");
            }

            return errors.Count > before || item is null
                ? null
                : (new DraftLine(item.Id, item.Sku, item.Name, item.BaseUnit, quantity, unit, unitCost, direction),
                    decimal.Round(quantity * factor, DraftLimits.MaxDecimals, MidpointRounding.AwayFromZero));
        }
    }

    private static bool IsOutflow(string type, string? direction) =>
        type is "issue" or "transfer" || (type == "adjustment" && direction == "remove");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}