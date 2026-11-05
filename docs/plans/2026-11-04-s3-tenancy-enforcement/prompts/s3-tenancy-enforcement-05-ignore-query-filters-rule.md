---
type: prompt
plan: "[[2026-11-04-s3-tenancy-enforcement]]"
id: 05
unit: IMS-4
status: done
model: haiku
effort: low
depends_on: []
parallel_group: E
files:
  create:
    - tests/Architecture.Tests/QueryFilterCallTests.cs
  modify: []
  delete: []
---
# 05 · T12 rule: no bare `IgnoreQueryFilters()`

## Task
Add an architecture test that fails if any C# file under `src/` calls `IgnoreQueryFilters()` with no arguments. The named overload `IgnoreQueryFilters([...])` stays allowed. Spec §8 `:490` and CLAUDE.md Review constraints say that `IgnoreQueryFilters()` without names is banned outside tests.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You're on branch `IMS-4`. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`. Files have **no final newline**.

Copy `tests/Architecture.Tests/MigrationCallTests.cs` (`:1–53`) for the shape:
- `using System.Text.RegularExpressions;` and `using Shouldly;`.
- The `namespace Architecture.Tests;` declaration.
- The `Root`/`FindRoot()`, `Rel()`, `IsBuildOutput()` and `IsMigrationsFolder()` helpers, copied as private statics.
- The `Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)` scan with `offenders.ShouldBeEmpty(string.Join(", ", offenders))`.

The class is `public sealed class QueryFilterCallTests`, with one `[Fact] public void No_unnamed_IgnoreQueryFilters_in_src()`:
- Pattern: `new Regex(@"\.\s*IgnoreQueryFilters\s*(<[^>]*>)?\s*\(\s*\)", RegexOptions.CultureInvariant)`. It matches `.IgnoreQueryFilters()` and `.IgnoreQueryFilters<T>()`, and not `.IgnoreQueryFilters(["Tenant"])`.
- Exclude build output and `Migrations` folders. Only `src/` is scanned, so tests are out of scope by construction.
- Add a one-line comment: `// Named filters only (spec §8): IgnoreQueryFilters(["Tenant"]) would drop just that filter, a bare call drops them all.`

## Do not
- Touch files outside the `files` list. Don't refactor `MigrationCallTests` to share helpers.
- Add packages.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Architecture.Tests/Architecture.Tests.csproj    # 11/11
```
- Probe: first back up the file (`SCRATCH=$(mktemp -d); cp src/Modules/Inventory/Inventory/InventoryModule.cs "$SCRATCH/"`). Append the line `// db.Locations.IgnoreQueryFilters();`, run the architecture tests, and confirm the new test fails, naming that file. Restore it from the backup, confirm `cmp` against the backup shows no difference, and rerun: 11/11. Don't use `git diff`: earlier prompts' output may be uncommitted.
