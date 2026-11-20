---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 06
unit: IMS-34
status: todo
model: sonnet
effort: medium
depends_on: [05]
parallel_group: F
files:
  create: []
  modify:
    - tests/Architecture.Tests/ModuleBoundaryTests.cs
    - tests/Architecture.Tests/ProjectReferenceTests.cs
    - tests/Architecture.Tests/Architecture.Tests.csproj
  delete: []
---
# 06 · Architecture rules for Assistant (T12)

## Task
Bring the new module under T12's existing boundary rules, and add three rules that make "the assistant has no write path and no SQL path" a tested property (plan D8).

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Read all four files in `tests/Architecture.Tests/` first:
  - `ModuleBoundaryTests.cs:11-12` loads the module assemblies.
  - `:29-49`: `Module_implementations_expose_only_their_registration_class`, a `[Theory]` over `"Inventory"`/`"Platform"` with a ternary assembly pick.
  - `:77-89`: `Modules_do_not_read_claims`.
  - `ProjectReferenceTests.cs:11`: `ModuleNames = ["Inventory", "Platform"]`, used by `Module_implementations_are_referenced_only_by_Api_and_tests`.
  - `MigrationCallTests.cs:11`: its own `ModuleNames`, with `Each_module_has_a_migrator`.
- Assistant: `src/Modules/Assistant/Assistant/Assistant.csproj`, public type `Assistant.AssistantModuleExtensions` with `AddAssistantModule` and `MapAssistantEndpoints`. It has no persistence and no Contracts project (plan D5, D6).
- NetArchTest.Rules 1.3.2 (`Types.InAssembly(...).Should().NotHaveDependencyOn(...)` / `.ShouldNot().HaveDependencyOn(...)`, `.GetResult()`, `Failing(result)` helper at `ModuleBoundaryTests.cs:27`).

### Steps
1. `Architecture.Tests.csproj`: add `<ProjectReference Include="..\..\src\Modules\Assistant\Assistant\Assistant.csproj" />`.
2. `ModuleBoundaryTests.cs`:
   - Add `AssistantAssembly = typeof(global::Assistant.AssistantModuleExtensions).Assembly`.
   - Replace the ternary with a `switch` over the three names, and add `[InlineData("Assistant")]` to `Module_implementations_expose_only_their_registration_class`.
   - Extend `Modules_do_not_read_claims` to Assistant (no `System.Security.Claims` dependency). It reads the user through `Kernel.Tenancy.ICurrentUser`.
   - New `[Fact] Assistant_has_no_write_or_sql_path`: Assistant's types have no dependency on `Microsoft.EntityFrameworkCore`, `Microsoft.Data.SqlClient`, `System.Data` or `Inventory.Contracts.IInventoryModule`. Check the last with `HaveDependencyOn("Inventory.Contracts.IInventoryModule")` (NetArchTest matches the full type name as a prefix, so check that this doesn't also match `IInventoryReader`, and use `HaveDependencyOnAny` with exact names if it does). Comment: plan D8, "drafts are proposals; only the post-stock form writes".
   - New `[Fact] Assistant_reads_inventory_only_through_its_contracts`: Assistant has no dependency on the `Inventory` namespace except `Inventory.Contracts`. Do this by reference: no `Inventory.dll` among `AssistantAssembly.GetReferencedAssemblies()`, nor `Platform.dll`.
3. `ProjectReferenceTests.cs`: add `"Assistant"` to `ModuleNames`, so `Module_implementations_are_referenced_only_by_Api_and_tests` covers it. `ModuleContracts("Assistant")` points at a path that doesn't exist; `Contracts_reference_only_Kernel_and_other_contracts` only reads references *from* contracts, so that's harmless. Add a comment.
4. Leave `MigrationCallTests.cs` as it is. Assistant has no tables (plan D5), so it isn't in that list. Mention this in your report; don't edit the file.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Weaken or skip an existing rule.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Architecture.Tests
dotnet test
```
- Sanity check, then revert: temporarily add `Microsoft.EntityFrameworkCore.SqlServer` to `Assistant.csproj` and use one type from it. `Assistant_has_no_write_or_sql_path` fails. Then a temporary `ProjectReference` to `Inventory.csproj` fails `Assistant_reads_inventory_only_through_its_contracts`. Revert both and report that you saw them fail.
