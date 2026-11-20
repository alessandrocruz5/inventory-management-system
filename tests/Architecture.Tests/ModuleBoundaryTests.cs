using System.Reflection;

using NetArchTest.Rules;

using Shouldly;

namespace Architecture.Tests;

public sealed class ModuleBoundaryTests
{
    private static readonly Assembly InventoryAssembly = typeof(global::Inventory.InventoryModuleExtensions).Assembly;
    private static readonly Assembly PlatformAssembly = typeof(global::Platform.PlatformModuleExtensions).Assembly;
    private static readonly Assembly AssistantAssembly = typeof(global::Assistant.AssistantModuleExtensions).Assembly;
    private static readonly Assembly KernelAssembly = LoadKernel();

    private static Assembly LoadKernel()
    {
        try
        {
            return Assembly.Load(new AssemblyName("Kernel"));
        }
        catch (FileNotFoundException)
        {
            return Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Kernel.dll"));
        }
    }

    private static string Failing(NetArchTest.Rules.TestResult result) => string.Join(", ", result.FailingTypeNames ?? []);

    [Theory]
    [InlineData("Inventory")]
    [InlineData("Platform")]
    [InlineData("Assistant")]
    public void Module_implementations_expose_only_their_registration_class(string module)
    {
        var assembly = module switch
        {
            "Inventory" => InventoryAssembly,
            "Platform" => PlatformAssembly,
            "Assistant" => AssistantAssembly,
            _ => throw new ArgumentOutOfRangeException(nameof(module), module, null),
        };

        // EF Core scaffolds migrations as `public partial class`, so `.Migrations` namespaces are exempt.
        var publicTypes = Types.InAssembly(assembly).That().ArePublic().GetTypes()
            .Where(t => !(t.Namespace ?? "").EndsWith(".Migrations", StringComparison.Ordinal))
            .ToList();

        publicTypes.Select(t => t.FullName).ShouldBe([$"{module}.{module}ModuleExtensions"]);

        var methods = publicTypes[0]
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal);

        methods.ShouldBe([$"Add{module}Module", $"Map{module}Endpoints"]);
    }

    [Fact]
    public void Inventory_domain_depends_only_on_the_BCL_and_Kernel()
    {
        var domain = Types.InAssembly(InventoryAssembly).That().ResideInNamespace("Inventory.Domain");

        var allowed = domain.Should().OnlyHaveDependenciesOn("System", "Inventory.Domain", "Kernel").GetResult();
        allowed.IsSuccessful.ShouldBeTrue(Failing(allowed));

        var noData = domain.ShouldNot().HaveDependencyOnAny("System.Data").GetResult();
        noData.IsSuccessful.ShouldBeTrue(Failing(noData));
    }

    [Fact]
    public void Kernel_has_no_infrastructure_dependencies()
    {
        var result = Types.InAssembly(KernelAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Data.SqlClient",
                "System.Data")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    [Fact]
    public void Modules_do_not_read_claims()
    {
        var inventory = Types.InAssembly(InventoryAssembly)
            .ShouldNot().HaveDependencyOn("System.Security.Claims")
            .GetResult();
        inventory.IsSuccessful.ShouldBeTrue(Failing(inventory));

        var platform = Types.InAssembly(PlatformAssembly)
            .That().DoNotResideInNamespace("Platform.Identity")
            .ShouldNot().HaveDependencyOn("System.Security.Claims")
            .GetResult();
        platform.IsSuccessful.ShouldBeTrue(Failing(platform));

        // Assistant reads the user through Kernel.Tenancy.ICurrentUser.
        var assistant = Types.InAssembly(AssistantAssembly)
            .ShouldNot().HaveDependencyOn("System.Security.Claims")
            .GetResult();
        assistant.IsSuccessful.ShouldBeTrue(Failing(assistant));
    }

    // Plan D8: drafts are proposals; only the post-stock form writes.
    [Fact]
    public void Assistant_has_no_write_or_sql_path()
    {
        // "Inventory.Contracts.IInventoryModule" is a full-name prefix match; it does not match IInventoryReader.
        var result = Types.InAssembly(AssistantAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Data.SqlClient",
                "System.Data",
                "Inventory.Contracts.IInventoryModule")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    [Fact]
    public void Assistant_reads_inventory_only_through_its_contracts()
    {
        var referenced = AssistantAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referenced.ShouldNotContain("Inventory");
        referenced.ShouldNotContain("Platform");
    }
}