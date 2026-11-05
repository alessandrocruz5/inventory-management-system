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

    // One argument with no top-level comma: the source alone, no filter names.
    private const string SourceOnly = @"\(\s*[^,()]+(?:\([^()]*\)[^,()]*)*\)";

    // Named filters only (spec §8): IgnoreQueryFilters(["Tenant"]) would drop just that filter, a bare call drops them all.
    private static readonly Regex[] Unnamed =
    [
        // Extension form: source.IgnoreQueryFilters()
        new(@"\.\s*IgnoreQueryFilters\s*(<[^>]*>)?\s*\(\s*\)", RegexOptions.CultureInvariant),
        // Static form: EntityFrameworkQueryableExtensions.IgnoreQueryFilters(source)
        new(@"\bEntityFrameworkQueryableExtensions\s*\.\s*IgnoreQueryFilters\s*(<[^>]*>)?\s*" + SourceOnly, RegexOptions.CultureInvariant),
        // using static form: IgnoreQueryFilters(source)
        new(@"(?<!\.\s*)(?<!\w)IgnoreQueryFilters\s*(<[^>]*>)?\s*" + SourceOnly, RegexOptions.CultureInvariant),
    ];

    private static bool IsUnnamed(string code) => Unnamed.Any(r => r.IsMatch(code));

    [Fact]
    public void No_unnamed_IgnoreQueryFilters_in_src()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(p))
            .Where(p => !IsMigrationsFolder(p))
            .Where(p => IsUnnamed(File.ReadAllText(p)))
            .Select(Rel)
            .ToList();

        offenders.ShouldBeEmpty(string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("db.Locations.IgnoreQueryFilters()", true)]
    [InlineData("db.Locations\n    .IgnoreQueryFilters<Location>()", true)]
    [InlineData("EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Locations)", true)]
    [InlineData("EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Location>())", true)]
    [InlineData("return IgnoreQueryFilters(db.Locations);", true)]
    [InlineData("db.Locations.IgnoreQueryFilters([\"Tenant\"])", false)]
    [InlineData("db.Locations.IgnoreQueryFilters([TenantSchema.FilterName])", false)]
    [InlineData("db.Locations.IgnoreQueryFilters(names)", false)]
    [InlineData("EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Locations, [\"Tenant\"])", false)]
    [InlineData("return IgnoreQueryFilters(db.Locations, names);", false)]
    public void Unnamed_pattern_matches_only_calls_without_filter_names(string code, bool unnamed) =>
        IsUnnamed(code).ShouldBe(unnamed, code);
}