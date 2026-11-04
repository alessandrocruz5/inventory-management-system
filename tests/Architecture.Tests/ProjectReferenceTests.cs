using System.Xml.Linq;

using Shouldly;

namespace Architecture.Tests;

public sealed class ProjectReferenceTests
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

    private static string ModuleImplementation(string module) =>
        Path.GetFullPath(Path.Combine(Root, "src/Modules", module, module, $"{module}.csproj"));

    private static string ModuleContracts(string module) =>
        Path.GetFullPath(Path.Combine(Root, "src/Modules", module, $"{module}.Contracts", $"{module}.Contracts.csproj"));

    private static readonly string Kernel = Path.GetFullPath(Path.Combine(Root, "src/Shared/Kernel/Kernel.csproj"));

    // (referencer, referenced) as full paths.
    private static List<(string From, string To)> References()
    {
        var projects = new[] { "src", "tests" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(Root, d), "*.csproj", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        var result = new List<(string, string)>();
        foreach (var project in projects)
        {
            var projectDir = Path.GetDirectoryName(project)!;
            var includes = XDocument.Load(project)
                .Descendants("ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(i => i is not null);

            foreach (var include in includes)
            {
                // `dotnet add reference` writes backslashes even on Linux.
                var target = Path.GetFullPath(Path.Combine(projectDir, include!.Replace('\\', '/')));
                result.Add((Path.GetFullPath(project), target));
            }
        }

        return result;
    }

    private static string Describe(IEnumerable<(string From, string To)> pairs) =>
        string.Join(", ", pairs.Select(p => $"{Rel(p.From)} -> {Rel(p.To)}"));

    [Fact]
    public void Module_implementations_are_referenced_only_by_Api_and_tests()
    {
        var implementations = ModuleNames.Select(ModuleImplementation).ToHashSet();
        var api = Path.GetFullPath(Path.Combine(Root, "src/Api/Api.csproj"));
        var testsDir = Path.GetFullPath(Path.Combine(Root, "tests")) + Path.DirectorySeparatorChar;

        var offenders = References()
            .Where(r => implementations.Contains(r.To))
            .Where(r => r.From != api && !r.From.StartsWith(testsDir, StringComparison.Ordinal))
            .ToList();

        offenders.ShouldBeEmpty(Describe(offenders));
    }

    [Fact]
    public void Contracts_reference_only_Kernel_and_other_contracts()
    {
        var contracts = ModuleNames.Select(ModuleContracts).ToHashSet();

        var offenders = References()
            .Where(r => contracts.Contains(r.From))
            .Where(r => r.To != Kernel && !contracts.Contains(r.To))
            .ToList();

        offenders.ShouldBeEmpty(Describe(offenders));
    }

    [Fact]
    public void Kernel_references_no_project()
    {
        var offenders = References().Where(r => r.From == Kernel).ToList();

        offenders.ShouldBeEmpty(Describe(offenders));
    }
}