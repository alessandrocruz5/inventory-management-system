---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 05
unit: IMS-29
status: done
model: opus
effort: high
depends_on: []
parallel_group: E
files:
  create: []
  modify:
    - src/Modules/Inventory/Inventory/Domain/PostingRules.cs
    - tests/Inventory.UnitTests/CostingVectorTests.cs
    - tests/Inventory.UnitTests/PostingRulesTests.cs
    - tests/Inventory.UnitTests/LedgerHarness.cs
    - docs/Inventory System — Sprint 1 Spec.md
  delete: []
---
# 05 · Domain: `PostingLine.UnitFactor` and vector set U (spec §7 change, approved)

## Task
Let an inflow line be costed in the unit the user entered: `cost = Round4(entered quantity × unit cost per entered unit)`, while the ledger quantity stays in base units. Add `UnitFactor` to `PostingLine`, use it in `PostingRules.Post`'s inflow branch only, add vector set U, and record the change in spec §6/§7.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. `Inventory.Domain` is pure: only `System.*` and Kernel, with no EF, ASP.NET or `System.Data` (T12). Rounding is `CostingPolicy.Round4` (4 dp, `MidpointRounding.AwayFromZero`).
- Plan D2 (**the user approved this §6/§7 change on 2026-11-18**):
  - `PostingLine` gains `decimal UnitFactor = 1m`, the number of base units in one entered unit, as an optional **last** positional parameter. The seven existing `new PostingLine(...)` call sites then compile unchanged.
  - Caller precondition: `Quantity` is `entered × UnitFactor`, exact (prompt 07 guarantees it). So `Quantity / UnitFactor` is the entered quantity exactly; decimal division returns an exact result when one fits.
- Read `Domain/PostingRules.cs` first:
  - `:13`: the record.
  - `:43-82`: per-line argument checks.
  - `:104-160`: the costing loop. `:117-119` is `else if (line.UnitCost is { } unitCost) cost = CostingPolicy.InflowCost(qty, unitCost);`.
  - `:309-335`: `CheckDirection`.
  - Then `Domain/CostingPolicy.cs` and `tests/Inventory.UnitTests/{CostingVectorTests,PostingRulesTests,LedgerHarness}.cs`. `LedgerHarness` drives the vector sets in memory.

### 1. `PostingRules.cs`
- `internal sealed record PostingLine(int LineNo, Guid ItemId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, decimal? UnitCost, decimal UnitFactor = 1m);`. Extend its XML doc: "`UnitFactor`: base units per entered unit (plan D2). `Quantity` is in base units and equals entered × factor exactly; `UnitCost` is per entered unit. Only an inflow with a unit cost uses it."
- Argument checks: `UnitFactor <= 0` → `ArgumentOutOfRangeException(nameof(lines), line.UnitFactor, $"Line {line.LineNo}: the unit factor must be greater than 0.")`. This is a caller bug, like the other checks.
- `:119` becomes `cost = CostingPolicy.InflowCost(qty / line.UnitFactor, unitCost);`, with a comment: "Cost in the entered unit (plan D2): qty / factor is the entered quantity exactly, so a case of 12 at ₱500 costs 500.0000, not 12 × Round4(500 / 12)."
- Change nothing else. `OutflowCost`, `DefaultAdjustmentCost`, `Revaluation`, `LastUnitCostAfterInflow` (base `qty`), `Reverse` and `CheckDirection` keep using base quantities.

### 2. Vector set U (`CostingVectorTests.cs`)
Add `Vector_set_U_units` in the style of `Vector_set_V_two_locations` (`:25`). Item base `pc`, one physical location U, toggle off, unit `case` = 12:

| Step | Action | Line passed to `Post` | Movement cost (₱) | U after: qty / value | LastUnitCost |
|---|---|---|---|---|---|
| IMS-28 | Receive 1 case @ 500.00 | Quantity 12, UnitCost 500.00, UnitFactor 12 | 500.0000 (per-pc 41.6667 × 12 would give 500.0004) | 12 / 500.0000 | 41.6667 |
| IMS-29 | Receive 2.5 case @ 480.00 | 30, 480.00, 12 | 1,200.0000 | 42 / 1,700.0000 | 40.4762 |
| IMS-30 | Issue 0.5 case | 6, null, 12 | 242.8571 | 36 / 1,457.1429 | 40.4762 |
| IMS-31 | Issue 36 pc | 36, null, 1 | 1,457.1429 (the remainder) | 0 / 0.0000 | 40.4762 |

Received 1,700.0000 = issued 242.8571 + 1,457.1429. Assert every cell to the fourth decimal.

Also add `Unit_factor_1_matches_the_old_inflow_cost`: for (qty, unitCost) in {(1, 10.00), (2, 10.01), (3, 10.0067), (0.5, 0.0001)}, posting with an explicit `UnitFactor: 1m` gives the same cost as `InflowCost(qty, unitCost)`.

### 3. `PostingRulesTests.cs`
- `Unit_factor_zero_or_negative_throws` (`[Theory]` 0, −1).
- `Unit_factor_is_ignored_on_outflows_and_default_adjustment_cost`: a positive adjustment without a unit cost and with `UnitFactor` 12 is costed at `DefaultAdjustmentCost(qty, balance)` on the base qty. An issue with factor 12 costs the same as with factor 1.
- Don't change existing tests. V, R, A, N and X must pass unchanged.

### 4. Spec (`docs/Inventory System — Sprint 1 Spec.md`)
- §6 table, Receipt row: cost becomes "`quantity × unitCost` from the request, in the unit the request names (section 7)". Adjustment positive line: "Request `unitCost` (per the request's unit)…".
- §7 code block: after `InflowCost`, add the comment `// A line in another unit (plan D2): Quantity is in base units, UnitFactor base units per entered unit; cost = InflowCost(Quantity / UnitFactor, unitCost), exactly Round4(entered qty × unitCost).`
- Add a **Vector set U** table after set X: the table above plus one sentence on why (no per-unit rounding).
- §15 Log: `- 2026-11-18: Sprint 3 D2 (approved): inflow cost in the entered unit via PostingLine.UnitFactor; vector set U.`

## Do not
- Touch files outside `files`. If you must, stop and report why. `DocumentPoster` is prompt 07.
- Change `CostingPolicy`'s signatures, `Reverse`, `Balance`, the engine or any existing vector's expected values.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests
dotnet test --project tests/Architecture.Tests
```
- `git diff --stat` shows no change to the existing V/R/A/N/X expected numbers.
