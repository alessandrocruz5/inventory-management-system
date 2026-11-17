namespace Inventory.Features.Items;

/// <summary>Normalizes and validates an item's editable fields (spec §5).</summary>
internal static class ItemRules
{
    /// <summary>Trims all three and lowercases the unit; adds an error per invalid field.</summary>
    public static (string Sku, string Name, string BaseUnit) Normalize(
        string? sku,
        string? name,
        string? baseUnit,
        Dictionary<string, string[]> errors)
    {
        var cleanSku = Check(sku, "sku", "SKU", 64, errors);
        var cleanName = Check(name, "name", "Name", 200, errors);
        var cleanUnit = Check(baseUnit, "baseUnit", "Base unit", 16, errors).ToLowerInvariant();
        return (cleanSku, cleanName, cleanUnit);
    }

    private static string Check(string? value, string key, string label, int maxLength, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            errors[key] = [$"{label} is required."];
        }
        else if (trimmed.Length > maxLength)
        {
            errors[key] = [$"{label} must be at most {maxLength} characters."];
        }

        return trimmed;
    }
}