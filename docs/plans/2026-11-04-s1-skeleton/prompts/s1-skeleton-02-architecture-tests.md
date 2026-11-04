---
type: prompt
plan: "[[2026-11-04-s1-skeleton]]"
id: 02
unit: IMS-2
status: todo
model: sonnet
effort: high
depends_on: [01]
parallel_group: B
files:
  create:
    - global.json
    - tests/Architecture.Tests/Architecture.Tests.csproj
    - tests/Architecture.Tests/ModuleBoundaryTests.cs
    - tests/Architecture.Tests/ProjectReferenceTests.cs
  modify:
    - InventorySystem.slnx
  delete: []
---
# 02 · Architecture tests (T12)

## Task
Create `tests/Architecture.Tests`, the spec's test T12 ("Module boundaries"; rules in §4). Make `dotnet test` run it on Microsoft Testing Platform. Prove that every rule fails when it's broken.

## Context
- **Repo root:** `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-2`; do not switch. .NET SDK 10.0.112.
- **Already built (prompt 01):**
  - `InventorySystem.slnx` and `Directory.Build.props` (Nullable, TreatWarningsAsErrors, LangVersion latest, ImplicitUsings).
  - `src/Shared/Kernel/Kernel.csproj` (empty).
  - `src/Modules/{Inventory,Platform}/{Inventory,Platform}.Contracts/*.csproj` (empty).
  - `src/Modules/Inventory/Inventory/Inventory.csproj`, whose only type is `public static class Inventory.InventoryModuleExtensions` with `AddInventoryModule` and `MapInventoryEndpoints`.
  - `src/Modules/Platform/Platform/Platform.csproj`, whose only type is `Platform.PlatformModuleExtensions` with `AddPlatformModule` and `MapPlatformEndpoints`.
- **Spec §4 rules (lines 188–194):**
  - `Inventory/Domain` is pure C#: no EF Core, no `HttpContext`/ASP.NET, no SQL.
  - Each module registers itself with one extension method pair, and Api never touches a module's internals.
  - No module reads claims directly; tenant and user come through Kernel's `ITenantContext`/`ICurrentUser`.
  - CLAUDE.md adds that module internals are `internal` and that other projects reference only a module's `.Contracts`.

### Locked decisions
- **Test runner.** xunit v3 on Microsoft Testing Platform.
  - The allowed package is `xunit.v3`. Version 4.0.2 depends on exactly `xunit.v3.mtp-v2`.
  - Don't add `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`; they aren't on the allowed list.
  - .NET 10's `dotnet test` needs the MTP runner declared in `global.json`.
- **Domain may use Kernel.** `Inventory.Domain` may depend on `System.*` (except `System.Data`), on itself and on `Kernel`.
- **Kernel stays pure.** `Kernel` must not depend on EF Core, ASP.NET Core, SqlClient or `System.Data`.
- **Claims in Platform.** Only the `Platform.Identity` namespace may touch `System.Security.Claims`. S2 puts the claims factory there.

### Files
1. Create `global.json` with exactly:
   ```json
   {
     "sdk": {
       "version": "10.0.112",
       "rollForward": "latestFeature"
     },
     "test": {
       "runner": "Microsoft.Testing.Platform"
     }
   }
   ```
2. Create `tests/Architecture.Tests/Architecture.Tests.csproj`. This mirrors the official `xunit3` template for `net10.0` + mtp-v2, with the meta-package instead of `xunit.v3.mtp-v2`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <OutputType>Exe</OutputType>
       <IsPackable>false</IsPackable>
     </PropertyGroup>
     <ItemGroup>
       <Using Include="Xunit" />
     </ItemGroup>
     <ItemGroup>
       <PackageReference Include="xunit.v3" Version="4.0.2" />
       <PackageReference Include="Shouldly" Version="4.3.0" />
       <PackageReference Include="NetArchTest.Rules" Version="1.3.2" />
     </ItemGroup>
   </Project>
   ```
   Then `dotnet add tests/Architecture.Tests reference` the five projects: Kernel, Inventory.Contracts, Inventory, Platform.Contracts and Platform. Then `dotnet sln InventorySystem.slnx add tests/Architecture.Tests/Architecture.Tests.csproj`.
3. `tests/Architecture.Tests/ModuleBoundaryTests.cs`, namespace `Architecture.Tests`, `public sealed class ModuleBoundaryTests`.
   - Name the assembly fields `InventoryAssembly`, `PlatformAssembly` and `KernelAssembly` to avoid clashing with the namespaces. Get them from `typeof(global::Inventory.InventoryModuleExtensions).Assembly`, `typeof(global::Platform.PlatformModuleExtensions).Assembly` and `Assembly.Load(new AssemblyName("Kernel"))`. Kernel has no types yet; if `Assembly.Load` fails, use `Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Kernel.dll"))`.
   - On failure, every NetArchTest assertion shows `string.Join(", ", result.FailingTypeNames ?? [])` in the Shouldly message.

   Tests:
   - `Module_implementations_expose_only_their_registration_class`: for each of Inventory and Platform:
     - The public types, excluding those whose namespace ends in `.Migrations`, are exactly `[<M>ModuleExtensions]`. Get them with `Types.InAssembly(a).That().ArePublic().GetTypes()`, then filter `t => !(t.Namespace ?? "").EndsWith(".Migrations", StringComparison.Ordinal)`.
     - Why the exclusion is a locked decision: EF Core scaffolds every migration as `public partial class`, and §4 puts migrations in `<M>/Persistence/Migrations`. Without it, S2's first migration fails T12.
     - That type's public static methods, by name, are exactly `{ Add<M>Module, Map<M>Endpoints }`.
     - Use a `[Theory]` with `[InlineData("Inventory")]` and `[InlineData("Platform")]`, or two facts; your choice.
   - `Inventory_domain_depends_only_on_the_BCL_and_Kernel`: types in `InventoryAssembly` that `ResideInNamespace("Inventory.Domain")` must satisfy both:
     - `Should().OnlyHaveDependenciesOn("System", "Inventory.Domain", "Kernel")`
     - `ShouldNot().HaveDependencyOnAny("System.Data")`
   - `Kernel_has_no_infrastructure_dependencies`: all types in `KernelAssembly` `ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Data.SqlClient", "System.Data")`.
   - `Modules_do_not_read_claims`:
     - All Inventory types `ShouldNot().HaveDependencyOn("System.Security.Claims")`.
     - Platform types that `DoNotResideInNamespace("Platform.Identity")` `ShouldNot().HaveDependencyOn("System.Security.Claims")`.
4. `tests/Architecture.Tests/ProjectReferenceTests.cs`, `public sealed class ProjectReferenceTests`. These test project references, which exist even when no code uses them.
   - Find the repo root by walking up from `AppContext.BaseDirectory` to the directory that contains `InventorySystem.slnx`.
   - Enumerate `*.csproj` under `src/` and `tests/`, skipping `bin/` and `obj/`.
   - Read each `ProjectReference/@Include` with `System.Xml.Linq`.
   - **Normalize `\` to `/` before** calling `Path.GetFullPath(Path.Combine(projectDir, include))`. The `dotnet add reference` command writes backslashes even on Linux.
   - A *module implementation* is `src/Modules/<M>/<M>/<M>.csproj`. A *contracts* project is `src/Modules/<M>/<M>.Contracts/<M>.Contracts.csproj`. *Kernel* is `src/Shared/Kernel/Kernel.csproj`.

   Tests:
   - `Module_implementations_are_referenced_only_by_Api_and_tests`: the only projects that may reference a module implementation are `src/Api/Api.csproj` (it doesn't exist yet; prompt 03 adds it) and projects under `tests/`.
   - `Contracts_reference_only_Kernel_and_other_contracts`.
   - `Kernel_references_no_project`.
   - Each failure message lists the offending `referencer -> referenced` pairs, relative to the repo root.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. The temporary probes in "Done when" are the one exception, and you must revert them.
- Add dependencies, abstractions or config that aren't listed here. That includes `xunit.runner.json`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, coverlet and a test base class.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test 2>&1 | grep -Ei 'total|passed|failed'   # >= 7 tests, 0 failed, run under MTP
```
Then prove that each rule bites. Apply each probe, run `dotnet test`, record the result, and **revert it before the next one**:
1. **Passes:** add `src/Modules/Inventory/Inventory/Domain/ArchProbe.cs` with `namespace Inventory.Domain; internal static class ArchProbe { internal static decimal Round4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero); } internal sealed record ArchProbeRecord(string? Note, decimal Qty);`. The record brings in nullable annotations and the members the compiler generates for records, so this shows the allow-list doesn't trip on them.
2. **Fails** `Inventory_domain_…`: change the probe to also expose `internal static Type Db => typeof(System.Data.Common.DbConnection);`.
3. **Fails** `Module_implementations_expose_…`: add `public sealed class Leak;` anywhere in the Inventory project.
4. **Passes** `Module_implementations_expose_…`: move that class to `src/Modules/Inventory/Inventory/Persistence/Migrations/Leak.cs` with `namespace Inventory.Persistence.Migrations; public partial class Leak;`. This proves the `.Migrations` exclusion.
5. **Fails** `Module_implementations_are_referenced_…`: add a ProjectReference from `Platform.csproj` to `../../Inventory/Inventory/Inventory.csproj`.
6. Revert everything, including any empty `Domain/` or `Persistence/` folders the probes left behind. Then run `dotnet test` (green) and `git status --porcelain`, which should show only the files above plus bookkeeping.

Report the five probe outcomes in your summary. The Kernel and claims rules have no types to check until S2. Say so; don't fake a probe for them.
