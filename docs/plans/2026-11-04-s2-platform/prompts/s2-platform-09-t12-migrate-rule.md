---
type: prompt
plan: "[[2026-11-04-s2-platform]]"
id: 09
unit: IMS-3
status: done
model: haiku
effort: low
depends_on: [08]
parallel_group: I
files:
  create:
    - tests/Architecture.Tests/MigrationCallTests.cs
  modify: []
  delete: []
---
# 09 · T12 migrate-call rule

## Task
Add one architecture test: no C# file under `src/` calls EF's `Database.Migrate(` or `Database.MigrateAsync(` except the module migrators, which are files named `*Migrator.cs` in a `Persistence` folder.

Match the `Database.` receiver, not any `.MigrateAsync(`. `src/Api/Setup/MigrateCommand.cs` legitimately calls `IModuleMigrator.MigrateAsync(ct)` (`m.MigrateAsync(ct)`), and a bare `\.Migrate(Async)?\(` pattern would flag it.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. Spec §4 `:193`: migrations run only from the migrate step, and the API never auto-migrates. The migrators are `src/Modules/Platform/Platform/Persistence/PlatformMigrator.cs` and `src/Modules/Inventory/Inventory/Persistence/InventoryMigrator.cs`. If prompt 02 moved the Platform context into `Identity/`, the migrator stayed in `Persistence/`; check with `ls`.

Copy the style of `tests/Architecture.Tests/ProjectReferenceTests.cs` exactly:
- `namespace Architecture.Tests;`, `public sealed class`, `using Shouldly;`.
- A `FindRoot()` that walks up to `InventorySystem.slnx` (lines 13–22).
- `Rel(...)` (line 24), and skip `bin`/`obj` (lines 39–40).

Implementation:
```csharp
[Fact]
public void Only_module_migrators_call_Database_Migrate()
{
    var pattern = new Regex(@"\bDatabase\s*\.\s*Migrate(Async)?\s*\(", RegexOptions.CultureInvariant);
    var offenders = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
        .Where(p => !IsBuildOutput(p))
        .Where(p => !IsMigrationsFolder(p))           // EF-generated migrations never call Migrate, but skip them anyway
        .Where(p => !IsModuleMigrator(p))             // .../Persistence/<Name>Migrator.cs
        .Where(p => pattern.IsMatch(File.ReadAllText(p)))
        .Select(Rel)
        .ToList();
    offenders.ShouldBeEmpty(string.Join(", ", offenders));
}
```
`IsModuleMigrator`: the parent directory name is `Persistence` and the file name ends with `Migrator.cs`. Add a second fact, `Each_module_has_a_migrator`: both migrator files exist. That keeps the first test from passing vacuously after a rename.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add packages.
- Create or switch branches, commit, or push.

## Done when
```bash
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test 2>&1 | grep -Ei 'total|passed|failed'    # Architecture.Tests 10 passed
```
- Probe: append `// x.Database.Migrate();` to `src/Api/Program.cs`, then run `dotnet test --project tests/Architecture.Tests`. `Only_module_migrators_call_Database_Migrate` fails and names `src/Api/Program.cs`. Revert, and it passes again.
- Before the probe, with the tree clean, the test passes even though `src/Api/Setup/MigrateCommand.cs` contains `m.MigrateAsync(ct)`. That proves the pattern doesn't over-match.
