---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 07
unit: U2
status: todo
model: opus
effort: high
depends_on: [06]
parallel_group: G
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Posting/UnitConversion.cs
    - tests/Inventory.IntegrationTests/UnitPostingTests.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentPoster.cs
    - docs/Inventory System — Sprint 1 Spec.md
  delete: []
---
# 07 · DocumentPoster: resolve and convert units (T16 API run)

## Task
In `DocumentPoster`, resolve each line's `unit` against the item's base unit and active `ItemUnits`, convert the quantity to base units exactly, and pass `UnitFactor` to the engine. Add the T16 integration tests: vector set U through the API, plus the unit errors and replays. Record the request field in spec §9.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. `Features/Posting/**` is a High-stakes path.
- What's already in place:
  - Prompt 05: `PostingLine(..., decimal? UnitCost, decimal UnitFactor = 1m)`. An inflow is costed `InflowCost(Quantity / UnitFactor, UnitCost)`.
  - Prompt 06: `ValidatedLine.Unit` (normalized or null). The hash covers it.
  - Prompts 01–02: `db.ItemUnits` (`ItemId`, `Code`, `Factor`, `IsActive`).
- Plan D4: base = `quantity × factor`. It must satisfy `CostingPolicy.Round4(base) == base` and `Math.Abs(base) <= PostingValidation.MaxAmount`, or the line gets 400 on its quantity field (`quantity`, or `quantityDelta` on an adjustment). Message: "This doesn't convert to whole 0.0001 {baseUnit}; enter it in {baseUnit}." or "This is more than {MaxAmount} {baseUnit}."
- Plan D5: unit null, or equal to `item.BaseUnit` → factor 1. Otherwise it must be an **active** `ItemUnits` row of that item, or the line gets 400 on `lines[i].unit` "Choose one of this item's units." Unit errors are 400s, so they're added **before** the `if (errors.Count > 0) return Problems.Validation(errors);` at `DocumentPoster.cs:79-82`. That keeps "every 400 before any 422". Replays (step 2) run before this, so a replay never re-checks units (plan D2 of S5c).
- Read `Features/Posting/DocumentPoster.cs` in full, especially:
  - `:62-82`: the database checks and the 400 gate.
  - `:96-119`: building `PostingLine`s. An adjustment's signed delta becomes a direction there.
  - `:156-176`: `BalanceOutOfRange` maps by `validated.Lines`.
- Also read `tests/Inventory.IntegrationTests/PostingVectorTests.cs` (how vector sets run through the API) and `PostingTests.cs:556-670` (helpers `PostDocumentAsync`, `Receipt`, `Line`, `Issue`, `IssueLine`, `NewKey`, `NewItemAsync` (a `pc` item), `NewLocationAsync`, `BalanceAsync`, `ScalarAsync`, `AssertValidationAsync`).

### 1. `Features/Posting/UnitConversion.cs`
```csharp
/// <summary>Plan D4/D5: a line's unit resolves to a factor of the item's base unit; quantities convert exactly or not at all.</summary>
internal static class UnitConversion
{
    /// <summary>quantity × factor when it is exact to 4 dp and within MaxAmount in magnitude; otherwise null.</summary>
    public static decimal? ToBase(decimal quantity, decimal factor) { ... }

    /// <summary>The active alternate units of these items, keyed (ItemId, Code). Codes are stored lowercase.</summary>
    public static async Task<Dictionary<(Guid ItemId, string Code), decimal>> ActiveFactorsAsync(
        InventoryDbContext db, IReadOnlyCollection<Guid> itemIds, CancellationToken ct) { ... }
}
```
`ToBase` is pure. The product of a ≤4 dp quantity and a ≤4 dp factor is exact in `decimal` and has at most 8 dp. Prompt 11 reuses both methods.

### 2. `DocumentPoster`
- After the unknown-item check and before the 400 gate: if any line has a non-null `Unit` that isn't its item's `BaseUnit`, load `ActiveFactorsAsync` once for those items. For each line whose item exists, resolve the factor (D5) and compute `ToBase(line.Quantity, factor)`. On an adjustment, `line.Quantity` is the signed delta, and `ToBase` keeps the sign. Add the D4/D5 errors, keyed by request index `LineNo - 1`.
- Keep the resolved `(factor, baseQuantity)` per `LineNo`. In the `postingLines` switch, use `baseQuantity` wherever `l.Quantity` is used today (`-baseQuantity` for a negative adjustment) and pass `UnitFactor: factor` on every line. Domain ignores it on outflows.
- Leave every other branch alone: replay, active checks, counterpart, `ToResult`. `insufficient-stock`'s `available` and `requested` come back in base units (D5), with no remapping.
- Add a comment above the conversion: "Plan D4/D5: units resolve and convert here, after replay and with the other database 400s, so every 400 still precedes any 422. The ledger only ever sees base units."

### 3. `UnitPostingTests.cs`
Use the `PostingTests` and `ItemUnitsTests` setup style: own tenant per test, Admin client. Add a unit with `POST /api/items/{id}/units { code: "case", factor: 12 }`.
- `Vector_set_U_through_the_api`: run U1–U4 from spec §7 set U (prompt 05) as API posts: receipts with `unit: "case"`, U3 an issue of 0.5 `case`, U4 an issue of 36 with no unit. After each step, assert the movement `Quantity` and `TotalCost` and the balance `OnHand`/`StockValue` with `BalanceAsync`, to the fourth decimal (`GetValue<decimal>()`, never text).
- `Base_unit_name_is_factor_1`: `unit: "pc"` on a `pc` item → movement quantity equals the request quantity.
- `Unknown_or_inactive_unit_is_400_on_unit`: `unit: "box"` → 400 `lines[0].unit`. Deactivate `case`, then post in `case` → 400 `lines[0].unit`. No document is written in either case.
- `Non_exact_conversion_is_400_on_quantity`: unit `g` with factor 0.001 on a `kg` item (create the item with `ItemsTests.CreateAsync(client, sku, sku, "kg", ct)`), quantity 0.5 g → 0.0005 kg is exact, so it's accepted; then 0.25 g → 400 `lines[0].quantity`. An adjustment with `quantityDelta: -0.25, unit: "g"` → 400 `lines[0].quantityDelta`.
- `Unit_errors_come_before_422s`: a receipt with one line in an unknown unit and an inactive item on another line → 400, not 422.
- `Replay_with_unit_is_200_and_a_changed_unit_is_409`: same key and body → 200 with `Idempotent-Replayed: true` and one document. The same key with the line in `pc` and quantity 12 → `409 idempotency-key-reused`, because the bodies differ.
- `Transfer_and_adjustment_accept_units`: transfer 1 `case` A→B → 12 pc moved, costed at A's average. Adjustment +1 `case` with `unitCost` 600 → cost 600.0000.
- `Old_requests_without_unit_post_unchanged`: run V1–V3 from set V with bodies that have no `unit` property. The costs match spec set V.

### 4. Spec §9
- In the request bodies block, add `"unit": "case"` to one receipt line and one adjustment line, followed by the sentence: "`unit` is optional on every line: the item's base unit or one of its active units. Quantities and unit costs are in that unit; the document, movements and balances are always in the base unit, and `insufficient-stock` reports base units."
- §11: add a test row `T16 | Vector set U; unit errors; replays with a unit | Unit + integration | Inflow cost in the entered unit; exact conversion; byte-stable hashes`.
- §15 Log: append "and `unit` on request lines (§9, T16)" to the Sprint 3 line prompt 05 added.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Change `PostingEngine`, `PostingRules`, `RequestHash`, `PostingValidation` or the reversal route.
- Round a converted quantity, ever (D4).
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*UnitPostingTests' --filter-class '*PostingTests' --filter-class '*PostingVectorTests' --filter-class '*AdjustmentTests' --filter-class '*TransferTests' --filter-class '*NegativeStockTests' --filter-class '*ReversalTests'
```
