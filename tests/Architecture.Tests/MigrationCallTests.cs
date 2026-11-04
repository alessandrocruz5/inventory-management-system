using System.Text.RegularExpressions;

using Shouldly;

namespace Architecture.Tests;

public sealed class MigrationCallTests
{
    private static readonly string Root = FindRoot();

    private static readonly string[] ModuleNames = ["Inventory", "Platform"];

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "InventorySystem.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("InventorySystem.slnx not found above the test output.");
    }

    private static string Rel(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static bool IsMigrationsFolder(string path) =>
        path.Split(Path.DirectorySeparatorChar).Contains("Migrations");

    // Module migrators are <Name>Migrator.cs inside a Persistence folder.
    private static bool IsModuleMigrator(string path) =>
        Path.GetFileName(path).EndsWith("Migrator.cs", StringComparison.Ordinal)
        && Path.GetFileName(Path.GetDirectoryName(path)) == "Persistence";

    [Fact]
    public void Only_module_migrators_call_Database_Migrate()
    {
        // Matches the Database receiver only, so `IModuleMigrator.MigrateAsync(ct)` in Api/Setup is not flagged.
        var pattern = new Regex(@"\bDatabase\s*\.\s*Migrate(Async)?\s*\(", RegexOptions.CultureInvariant);

        var offenders = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(p))
            .Where(p => !IsMigrationsFolder(p))
            .Where(p => !IsModuleMigrator(p))
            .Where(p => pattern.IsMatch(File.ReadAllText(p)))
            .Select(Rel)
            .ToList();

        offenders.ShouldBeEmpty(string.Join(", ", offenders));
    }

    [Fact]
    public void Each_module_has_a_migrator()
    {
        var missing = ModuleNames
            .Select(m => Path.Combine(Root, "src", "Modules", m, m, "Persistence", $"{m}Migrator.cs"))
            .Where(p => !File.Exists(p))
            .Select(Rel)
            .ToList();

        missing.ShouldBeEmpty(string.Join(", ", missing));
    }
}