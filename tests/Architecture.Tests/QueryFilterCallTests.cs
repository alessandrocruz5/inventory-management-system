using System.Text.RegularExpressions;

using Shouldly;

namespace Architecture.Tests;

public sealed class QueryFilterCallTests
{
    private static readonly string Root = FindRoot();

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

    [Fact]
    public void No_unnamed_IgnoreQueryFilters_in_src()
    {
        // Named filters only (spec §8): IgnoreQueryFilters(["Tenant"]) would drop just that filter, a bare call drops them all.
        var pattern = new Regex(@"\.\s*IgnoreQueryFilters\s*(<[^>]*>)?\s*\(\s*\)", RegexOptions.CultureInvariant);

        var offenders = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(p))
            .Where(p => !IsMigrationsFolder(p))
            .Where(p => pattern.IsMatch(File.ReadAllText(p)))
            .Select(Rel)
            .ToList();

        offenders.ShouldBeEmpty(string.Join(", ", offenders));
    }
}