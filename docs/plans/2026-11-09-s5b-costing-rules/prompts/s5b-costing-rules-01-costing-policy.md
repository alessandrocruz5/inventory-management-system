---
type: prompt
plan: "[[2026-11-09-s5b-costing-rules]]"
id: 01
unit: IMS-7
status: todo
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Domain/Balance.cs
    - src/Modules/Inventory/Inventory/Domain/LedgerMovement.cs
    - src/Modules/Inventory/Inventory/Domain/PostingRejections.cs
    - src/Modules/Inventory/Inventory/Domain/CostingPolicy.cs
    - tests/Inventory.UnitTests/Inventory.UnitTests.csproj
    - tests/Inventory.UnitTests/CostingPolicyTests.cs
  modify:
    - src/Modules/Inventory/Inventory/Inventory.csproj
    - InventorySystem.slnx
  delete: []
---
# 01 · Domain types, CostingPolicy and the unit test project

## Task
1. Create the pure `Inventory.Domain` namespace, with its value types, the rejection exceptions and `CostingPolicy`.
2. Create `tests/Inventory.UnitTests`, the first unit test project, with `CostingPolicyTests`, and add it to the solution.

## Context
- **Repo and branch:** the repo root is `/home/apa/Code/portfolio/inventory-management-system`, and you are on branch `IMS-7`. Don't switch it. Run every command from the repo root.
- **Stack:** .NET 10, C# latest, `Nullable` on, `TreatWarningsAsErrors` on (`Directory.Build.props`), implicit usings on. `.editorconfig` has `insert_final_newline = false`, `System` usings first, and using groups separated by a blank line. Run `dotnet format` before you finish.
- **Domain purity (T12, `tests/Architecture.Tests/ModuleBoundaryTests.cs:52-62`):** types in namespace `Inventory.Domain` may depend only on `System.*` (never `System.Data`), `Inventory.Domain` and `Kernel`.
  - Never `using Inventory.Persistence` or `Inventory.Features`, or anything from `Microsoft.*`. Today this test passes vacuously; your types make it real.
- **Visibility (`ModuleBoundaryTests.cs:31-47`):** the only public type in the Inventory assembly is `InventoryModuleExtensions`, so every type you add is `internal`. Members inside internal types may be `public`.
- **Conventions:** quantities and money are `decimal`, never `double` or `float`. Rounding is always `decimal.Round(x, 4, MidpointRounding.AwayFromZero)`. C#'s default banker's rounding is wrong here (spec §7, R4).
- **Folder:** `src/Modules/Inventory/Inventory/Domain/` doesn't exist yet. The namespace is `Inventory.Domain` (file-scoped, matching the folder).
- These records mirror the ledger columns in `Persistence/StockBalance.cs` and `Persistence/StockMovement.cs`. Don't reference those classes.

### Files and exact shapes

**`Domain/Balance.cs`**
```csharp
namespace Inventory.Domain;

/// <summary>Identifies a <c>StockBalances</c> row of the current tenant.</summary>
internal readonly record struct BalanceKey(Guid ItemId, Guid LocationId);

/// <summary>
/// A <c>StockBalances</c> row as the posting rules see it (spec §5). The engine takes <see cref="IsVirtual"/> and
/// <see cref="AllowNegative"/> from the step 7 location read, not from the row (spec §6). Virtual balances track
/// quantity only: their <see cref="StockValue"/> stays 0 and <see cref="LastUnitCost"/> stays null.
/// </summary>
internal sealed record Balance(
    Guid ItemId,
    Guid LocationId,
    bool IsVirtual,
    bool AllowNegative,
    decimal OnHand,
    decimal StockValue,
    decimal? LastUnitCost,
    long? LastMovementId)
{
    public BalanceKey Key => new(ItemId, LocationId);
}
```

**`Domain/LedgerMovement.cs`**
- `internal sealed record LedgerMovement(long Id, Guid DocumentId, int LineNo, Guid ItemId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, decimal TotalCost, long? ReversesMovementId);` is a posted `StockMovements` row.
- `internal sealed record PlannedMovement(int LineNo, Guid ItemId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, decimal TotalCost, long? ReversesMovementId);` is a movement the rules decided on. The engine adds `Id`, `DocumentId`, `PostedAt` and `PostedBy`.
- Give each a one-line `<summary>`.

**`Domain/PostingRejections.cs`**: business rejections. Each one aborts the whole posting, and S5c, S6 and S9 map them to ProblemDetails (spec §9).
```csharp
internal abstract class PostingRejectedException(string message) : Exception(message);

/// <summary>Maps to 422 insufficient-stock (itemId, locationId, available, requested).</summary>
internal sealed class InsufficientStockException(Guid itemId, Guid locationId, decimal available, decimal requested)
    : PostingRejectedException($"Insufficient stock: {requested} requested, {available} available.")
{ public Guid ItemId => itemId; public Guid LocationId => locationId; public decimal Available => available; public decimal Requested => requested; }

/// <summary>A positive adjustment line without a unit cost while OnHand <= 0. Maps to 400 validation on lines[i].unitCost.</summary>
internal sealed class UnitCostRequiredException(int lineNo) : PostingRejectedException(...) { public int LineNo => lineNo; }

/// <summary>Maps to 409 reversal-not-latest; the engine resolves BlockingMovementId to its document.</summary>
internal sealed class ReversalNotLatestException(Guid itemId, Guid locationId, long blockingMovementId) : PostingRejectedException(...) { ItemId, LocationId, BlockingMovementId }
```
Format the classes properly. If an analyzer under `TreatWarningsAsErrors` demands something, such as standard exception constructors, satisfy it with the least code that keeps these constructors and properties.

**`Domain/CostingPolicy.cs`**: `internal static class CostingPolicy`, pure, with no I/O and no state. Write a `<summary>` per member that cites spec §7.
```csharp
public static decimal Round4(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

// Receipt or positive adjustment line with a unit cost.
public static decimal InflowCost(decimal quantity, decimal unitCost) => Round4(quantity * unitCost);

// Positive adjustment line without a unit cost. Requires balance.OnHand > 0, else InvalidOperationException
// (PostingRules checks first and throws UnitCostRequiredException with the line number).
// Multiply first: Round4(quantity * balance.StockValue / balance.OnHand). Rounding the average first is wrong (30.0201 vs 30.0200).
public static decimal DefaultAdjustmentCost(decimal quantity, Balance balance);

// Issue, transfer-out, negative adjustment line, from a physical balance.
//   quantity >  OnHand -> throw new InsufficientStockException(balance.ItemId, balance.LocationId, balance.OnHand, quantity)
//                         EVEN WHEN balance.AllowNegative is true: the estimate branch lands in S10.
//                         Put a comment on that line: "S10: when AllowNegative, estimate instead (spec §7)".
//   quantity == OnHand -> balance.StockValue   (the remainder: rounding never leaves a residue)
//   quantity <  OnHand -> Round4(quantity * balance.StockValue / balance.OnHand)
public static decimal OutflowCost(decimal quantity, Balance balance);

// After an inflow that isn't a reversal (and after its revaluation, S10), with the balance's new totals.
// onHandAfter > 0 ? Round4(stockValueAfter / onHandAfter) : Round4(cost / quantity)
public static decimal LastUnitCostAfterInflow(decimal onHandAfter, decimal stockValueAfter, decimal quantity, decimal cost);

// The ledger definition of LastUnitCost (spec §7 :386-390, §6 INV-5). Used by reversals (S9) and must match S11's SQL.
public static decimal? LastUnitCost(BalanceKey key, IEnumerable<LedgerMovement> history);
```
How the `LastUnitCost(key, history)` fold works:
1. `history` is the movements of `key.ItemId` that touch `key.LocationId` (From or To), in any order. Throw `ArgumentException` if any movement has another item or doesn't touch the location. Sort by `Id`.
2. `reversed` = the set of every `ReversesMovementId` in the history.
3. `inflow` is the last movement (by `Id`) with `ToLocationId == key.LocationId`, `Quantity > 0`, `ReversesMovementId is null` and `!reversed.Contains(Id)`. If there is none, return `null`.
4. `end` = the max `Id` among the history movements with `DocumentId == inflow.DocumentId`. This covers "the inflow and its revaluation" (a same-document, zero-quantity movement, S10).
5. Running sums over the movements with `Id <= end`:
   - An inflow (`To == location`) adds `Quantity` to `onHand` and `TotalCost` to `value`.
   - An outflow (`From == location`) subtracts both.
6. Return `LastUnitCostAfterInflow(onHand, value, inflow.Quantity, inflow.TotalCost)`.

**`Inventory.csproj`:** add `<InternalsVisibleTo Include="Inventory.UnitTests" />` to the existing `InternalsVisibleTo` `ItemGroup` (`:23-26`).

**`tests/Inventory.UnitTests/Inventory.UnitTests.csproj`:** copy `tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj`, with these differences:
- Keep only the `xunit.v3` `4.0.2` and `Shouldly` `4.3.0` package references, `<Using Include="Xunit" />`, `<OutputType>Exe</OutputType>` and `<IsPackable>false</IsPackable>`.
- Its only project reference is `..\..\src\Modules\Inventory\Inventory\Inventory.csproj`.
- No `Microsoft.NET.Test.Sdk` and no `xunit.runner.visualstudio` (TASKS.md:28; MTP runs through `global.json`).
- 2-space indentation.

**`InventorySystem.slnx`:** add `<Project Path="tests/Inventory.UnitTests/Inventory.UnitTests.csproj" />` inside the `/tests/` folder, between `Inventory.IntegrationTests` and `Platform.IntegrationTests`.

**`tests/Inventory.UnitTests/CostingPolicyTests.cs`:** `namespace Inventory.UnitTests;`, `public sealed class CostingPolicyTests`, Shouldly assertions, and method names like `Round4_rounds_midpoints_away_from_zero` (the underscore style of `tests/Architecture.Tests`). Build balances with a small private helper: `new Balance(item, loc, IsVirtual: false, AllowNegative: false, onHand, value, luc, null)`. Cover:
- `Round4`:
  - `10.00665m` → `10.0067m` (banker's rounding gives 10.0066: spec R4);
  - `-10.00665m` → `-10.0067m`;
  - `10.006666m` → `10.0067m`.
- `InflowCost`: `(6m, 125m)` → `750m`; `(2m, 10.01m)` → `20.02m`; `(3m, 0.33335m)` → `1.0001m`.
- `DefaultAdjustmentCost`:
  - `(3m, OnHand 3 / value 30.02)` → `30.0200m` (spec A3), which is not `30.0201m`;
  - `OnHand 0` → `InvalidOperationException`;
  - `OnHand -1` → `InvalidOperationException`.
- `OutflowCost`:
  - proportional `(15m, 20 / 2200)` → `1650m`;
  - `(1m, 3 / 30.02)` → `10.0067m`;
  - `(1m, 2 / 20.0133)` → `10.0067m`;
  - remainder `(1m, 1 / 10.0066)` → `10.0066m`;
  - remainder `(6m, 6 / 720)` → `720m`;
  - `(7m, 6 / 720)` → `InsufficientStockException` with `ItemId`, `LocationId`, `Available == 6m` and `Requested == 7m`;
  - the same with `AllowNegative: true` still throws (the S10 placeholder).
- `LastUnitCostAfterInflow`: `(10m, 1230m, 6m, 750m)` → `123m`; `(0m, 0m, 2m, 120m)` → `60m`.
- `LastUnitCost` fold. Build `LedgerMovement`s by hand for one item and location B, with SUP, A and CUS as other location ids:
  - an empty history → `null`;
  - only an outflow → `null`;
  - transfer-in A→B 4 / 480 (doc d1, id 5), then receipt SUP→B 6 / 750 (doc d2, id 6) → `123m`;
  - the same plus a reversal B→SUP 6 / 750 (`ReversesMovementId` 6, id 7) → `120m`;
  - transfer-in 4 / 480, then issue B→CUS 1 / 120 → `120m` (an outflow after the inflow doesn't change it);
  - a movement for another item, or one that doesn't touch B → `ArgumentException`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add any package. Only `xunit.v3` and `Shouldly`, at the versions above.
- Make any type `public`, or reference `Inventory.Persistence`, `Inventory.Features`, `Microsoft.*` or `System.Data` from `Domain/`.
- Add `PostingRules` (prompt 02), an estimate or revaluation path (S10), request validation (S5c), ProblemDetails slugs, or Kernel helpers.
- Use `double`, `float`, `Math.Round` without `MidpointRounding.AwayFromZero`, or `decimal.Round` without it.
- Create or switch branches, commit, or push.

## Done when
```bash
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests/Inventory.UnitTests.csproj
dotnet test --project tests/Architecture.Tests/Architecture.Tests.csproj
grep -rnE '^using (Inventory\.(Persistence|Features)|Microsoft\.|System\.Data)' src/Modules/Inventory/Inventory/Domain   # expect no output
```
- Every `CostingPolicyTests` case passes, and `Inventory_domain_depends_only_on_the_BCL_and_Kernel` passes with real types in `Inventory.Domain`.
