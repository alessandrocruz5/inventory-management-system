namespace Inventory.Features.Locations;

/// <summary>Normalizes and validates a location's editable fields.</summary>
internal static class LocationRules
{
    /// <summary>Trims both; adds an error per invalid field.</summary>
    public static (string Code, string Name) Normalize(string? code, string? name, Dictionary<string, string[]> errors)
    {
        var cleanCode = Check(code, "code", "Code", 32, errors);
        var cleanName = Check(name, "name", "Name", 200, errors);
        return (cleanCode, cleanName);
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