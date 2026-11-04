---
type: prompt
plan: "[[2026-11-04-s1-skeleton]]"
id: 01
unit: IMS-2
status: done
model: sonnet
effort: medium
depends_on: []
parallel_group: A
files:
  create:
    - Directory.Build.props
    - .editorconfig
    - .gitignore
    - InventorySystem.slnx
    - src/Shared/Kernel/Kernel.csproj
    - src/Modules/Inventory/Inventory.Contracts/Inventory.Contracts.csproj
    - src/Modules/Inventory/Inventory/Inventory.csproj
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - src/Modules/Platform/Platform.Contracts/Platform.Contracts.csproj
    - src/Modules/Platform/Platform/Platform.csproj
    - src/Modules/Platform/Platform/PlatformModuleExtensions.cs
  modify: []
  delete: []
---
# 01 · Build foundation and module projects

## Task
Create the repo-wide .NET build configuration, the solution file, and the five library projects from the spec's §4 layout: Kernel, two Contracts projects and two module implementations. Each module implementation gets one public registration class with no-op bodies. No other code.

## Context
- **Repo root:** `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-2`; do not switch. The repo has only docs so far (`docs/`, `CLAUDE.md`, `README.md`, `LICENSE`, `TASKS.md`). This is the first code in the repo.
- **Toolchain:** .NET SDK 10.0.112 is installed. Target `net10.0` everywhere.
- **Spec:** `docs/Inventory System — Sprint 1 Spec.md`, §4 (lines 156–198) defines the layout. Use these paths exactly. CLAUDE.md's High-stakes and Shared-code globs match them: `src/Shared/Kernel/**`, `src/Modules/*/*.Contracts/**`, `src/Modules/Inventory/Inventory/{Domain,Persistence,Features}/**`.

### Steps
1. Run `dotnet new gitignore` at the repo root, and keep its output unchanged.
2. Run `dotnet new editorconfig` at the repo root, then append exactly:
   ```ini

   # EF Core migrations are generated; keep them out of dotnet format (spec audit, NOTE on formatting)
   [**/Migrations/*.cs]
   generated_code = true
   ```
3. Create `Directory.Build.props` with exactly:
   ```xml
   <Project>
     <PropertyGroup>
       <LangVersion>latest</LangVersion>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
     </PropertyGroup>
   </Project>
   ```
   Each csproj keeps its own `<TargetFramework>net10.0</TargetFramework>`. Remove `Nullable` and `ImplicitUsings` from the generated csproj files, because Directory.Build.props sets them.
4. Run `dotnet new sln -n InventorySystem` at the root. SDK 10 writes `InventorySystem.slnx`. If you get a `.sln` instead, delete it and rerun with `--format slnx`.
5. Create the projects with `dotnet new classlib -f net10.0`, then delete each `Class1.cs`:
   | Path | Notes |
   |---|---|
   | `src/Shared/Kernel/Kernel.csproj` | No references. No source files. |
   | `src/Modules/Inventory/Inventory.Contracts/Inventory.Contracts.csproj` | No references. No source files. |
   | `src/Modules/Platform/Platform.Contracts/Platform.Contracts.csproj` | No references. No source files. |
   | `src/Modules/Inventory/Inventory/Inventory.csproj` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />`; ProjectReference to `Inventory.Contracts` and `Kernel` |
   | `src/Modules/Platform/Platform/Platform.csproj` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />`; ProjectReference to `Platform.Contracts` and `Kernel` |

   Add references with `dotnet add <proj> reference <proj>`. The root namespace equals the project name (the default), which gives `Inventory`, `Inventory.Contracts`, `Platform` and so on. Don't create empty `Domain/`, `Features/` or `Persistence/` folders, or `.gitkeep` files.
6. Create `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs`:
   ```csharp
   using Microsoft.AspNetCore.Routing;
   using Microsoft.Extensions.Hosting;

   namespace Inventory;

   public static class InventoryModuleExtensions
   {
       public static IHostApplicationBuilder AddInventoryModule(this IHostApplicationBuilder builder) => builder;

       public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
   }
   ```
   Create `src/Modules/Platform/Platform/PlatformModuleExtensions.cs` the same way, with namespace `Platform`, class `PlatformModuleExtensions`, and methods `AddPlatformModule` and `MapPlatformEndpoints`.
   - The signatures are a locked decision. S2 calls Aspire's `EnrichSqlServerDbContext`, an `IHostApplicationBuilder` extension, from `AddInventoryModule`.
   - The `…Extensions` name keeps `InventoryModule` free for S2's `IInventoryModule` implementation.
   - This class is the only public type a module implementation may have (T12 in prompt 02).
7. Add all five projects to `InventorySystem.slnx` with `dotnet sln InventorySystem.slnx add <proj>`, keeping the solution folders the CLI derives from the paths.

### Conventions that apply (CLAUDE.md)
- Module internals are `internal`. Other projects reference only a module's `.Contracts`, except that the Api host references module implementations to call their registration methods.
- Banned packages: MediatR, AutoMapper, MassTransit, PrimeNG, FluentAssertions 8+. This prompt adds no NuGet packages at all.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here. That includes `Directory.Packages.props`, `global.json` (prompt 02 creates it) and placeholder types in Kernel or Contracts.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror            # 0 warnings, 0 errors, 5 projects
dotnet format --verify-no-changes
grep -c 'Project ' InventorySystem.slnx   # 5 projects listed
git status --porcelain                # only the files above, plus bookkeeping (TASKS.md, docs/plans/)
```
- `dotnet test` is not run here: no test project exists until prompt 02.
- `grep -rn 'public ' src/Modules/*/*/*.cs` shows only the two `*ModuleExtensions` classes and their methods.
