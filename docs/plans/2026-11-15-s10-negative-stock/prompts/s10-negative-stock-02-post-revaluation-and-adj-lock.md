---
type: prompt
plan: "[[2026-11-15-s10-negative-stock]]"
id: 02
unit: IMS-14
status: done
model: opus
effort: xhigh
depends_on: [01]
parallel_group: B
files:
  create: []
  modify:
    - src/Modules/Inventory/Inventory/Domain/PostingRules.cs
    - src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs
    - tests/Inventory.UnitTests/LedgerHarness.cs
    - tests/Inventory.UnitTests/PostingRulesTests.cs
    - tests/Inventory.IntegrationTests/SeedTests.cs
  delete: []
---
# 02 · Revaluation in `Post` and the engine's `ADJ` lock

## Task
Replace the S10 placeholder in `PostingRules.Post` with spec §7's revaluation, which needs the tenant's Adjustment location, so `Post` gains a required `Guid adjustmentLocationId`. In `PostingEngine.AttemptAsync`, resolve that id, add `(item, ADJ)` to step 5/6's keys for receipt and transfer lines, pass the id to `Post`, and extend the range guard to movement costs. Update every `Post` caller and the seed's balance count.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-14`. Stop if `git branch --show-current` says otherwise. .NET 10, `Nullable` on, `TreatWarningsAsErrors`; types are `internal`; `decimal` only; raw SQL only through EF.
- Prompt 01 is done: `CostingPolicy.OutflowCost` estimates past zero under `AllowNegative`, and `CostingPolicy.Revaluation(decimal quantity, decimal cost, Balance balance)` exists (`Domain/CostingPolicy.cs`).
- **Ledger rules you must keep** (spec §6, CLAUDE.md): documents and movements are insert-only. Balance locks are **one** statement seeking the clustered key `(TenantId, ItemId, LocationId)`: don't change `LockBalancesAsync`'s SQL or hints, and add no index. Step 9 must keep writing `row.AllowNegative = locations[row.LocationId].AllowNegativeStock` (`PostingEngine.cs:521`), which repairs a stale flag copied at step 5 (spec audit `:18`). Retries stay inside the execution strategy.

### 1. `Domain/PostingRules.cs` (read it in full: `Post` `:24-147`, `Find` `:280-284`)
- Signature: `public static PostingResult Post(PostingKind kind, IReadOnlyList<PostingLine> lines, IReadOnlyDictionary<BalanceKey, Balance> balances, Guid adjustmentLocationId)`. Document the parameter: the tenant's Adjustment location, the counterpart of every revaluation (spec §6 `:303`).
- Replace the placeholder block (`:124-141`, the `if (target.OnHand < 0) throw new InvalidOperationException(… S10 …)`) so the physical-target branch reads:
  ```csharp
  var revaluation = CostingPolicy.Revaluation(qty, cost, target);
  var onHand = target.OnHand + qty;
  var value = target.StockValue + cost - revaluation;
  Store(target with
  {
      OnHand = onHand,
      StockValue = value,
      LastUnitCost = CostingPolicy.LastUnitCostAfterInflow(onHand, value, qty, cost),
  });

  if (revaluation != 0m)
  {
      var adjustmentKey = new BalanceKey(line.ItemId, adjustmentLocationId);
      var adjustment = Current(adjustmentKey);   // Find semantics: InvalidOperationException if step 5 didn't create it
      if (!adjustment.IsVirtual) throw new InvalidOperationException(...);
      Store(adjustment);                          // quantity 0: unchanged, but the engine sets its LastMovementId
      revaluations.Add(revaluation > 0m
          ? new PlannedMovement(1000 + line.LineNo, line.ItemId, line.ToLocationId, adjustmentLocationId, 0m, revaluation, null)
          : new PlannedMovement(1000 + line.LineNo, line.ItemId, adjustmentLocationId, line.ToLocationId, 0m, -revaluation, null));
  }
  ```
  `Current` today indexes `balances[key]` directly. Make it go through `Find`, so a missing `(item, ADJ)` throws the existing "step 5 creates every touched balance" `InvalidOperationException`, not `KeyNotFoundException`.
- `revaluations` is a new `List<PlannedMovement>`. Return `new PostingResult([.. movements, .. revaluations], …)`: **all lines first, then all revaluations, each in `LineNo` order** (plan D5: `1, 2, 1001, 1002`). The engine takes movement ids in this order, so on every balance the revaluation is the document's last movement, which is what S9's latest-only check and `CostingPolicy.LastUnitCost`'s `end` rely on.
- The revaluation applies to every physical target: receipts, positive adjustments (with a unit cost; without one at `OnHand <= 0` it is still `UnitCostRequiredException`) and transfer-ins. It never applies to `Reverse`: leave `Reverse` untouched (it mirrors qty-0 movements already).
- Update the class and `Post` XML docs: remove "S10" mentions and describe the revaluation movement at `LineNo 1000 + n` (spec §5 `:250`).

### 2. `Persistence/PostingEngine.cs` (read `AttemptAsync` `:117-246` and `WriteAsync` `:476-527`)
- Inside the transaction, before step 5, resolve the Adjustment location the way `Features/Posting/DocumentPoster.cs:106` resolves a counterpart:
  `var adjustmentId = await db.Locations.AsNoTracking().Where(l => l.Kind == LocationKind.Adjustment).Select(l => l.Id).SingleAsync(ct);`
  Add a comment: virtual locations can't be edited or deactivated, so a snapshot read is safe (plan D1).
- Step 5 keys (`:148-152`): keep `TouchedKeys(l.ItemId, l.FromLocationId, l.ToLocationId)` and, **when `command.Kind` is `PostingKind.Receipt` or `PostingKind.Transfer`**, also `new BalanceKey(l.ItemId, adjustmentId)`. Keep `.Distinct()` and the `SqlGuid` ordering. Comment: a possible revaluation touches `(item, ADJ)`, so step 6's one statement must lock it (spec §6 step 5; plan D1); an adjustment line already touches `ADJ`, and an issue never revalues.
- Update the comment above the count check (`:176-178`): a receipt line touches `{location, SUP, ADJ}`, a transfer line `{A, B, ADJ}`, an issue or adjustment line its two locations, so items × locations is still the touched set. Keep the check itself.
- Step 8: `PostingRules.Post(command.Kind, command.Lines, balances, adjustmentId)`.
- Range guard (plan D6), right after the existing balance check (`:228-232`):
  ```csharp
  // Plan D6: an estimate or a revaluation can exceed decimal(19,4) while both balances fit (8115, a 500).
  if (result.Movements.FirstOrDefault(m => m.TotalCost > MaxColumn) is { } costOutOfRange)
  {
      return new BalanceOutOfRangeOutcome(costOutOfRange.ItemId, costOutOfRange.FromLocationId);
  }
  ```
  `DocumentPoster` already maps this outcome by item to `lines[i].quantity` or `quantityDelta`.
- Don't change `ReverseAsync`, `FindExistingAsync`, `LockBalancesAsync`, `ReadMasterDataAsync` or `WriteAsync`. `WriteAsync` already sets `LastMovementId` per balance from the new movements, including `(item, ADJ)` when it carries a revaluation, and only updates balances `Post` returned.

### 3. `tests/Inventory.UnitTests/LedgerHarness.cs`
- `Post(...)` (`:146-155`): build the working copy from `[from, to, (item, ADJ)]` (`LocationId("ADJ")` is the virtual one created in the constructor) and call `PostingRules.Post(kind, [line], working, LocationId("ADJ"))`. An empty `(item, ADJ)` is only committed when `Post` returns it, so `BalanceCount` assertions don't change. Leave INV-3 and toggles alone: prompt 03 adds toggles.

### 4. `tests/Inventory.UnitTests/PostingRulesTests.cs`
- Pass `Adj` (already defined, `:13`) as the fourth argument of **every** `PostingRules.Post(...)` call. Where a test can reach a revaluation, its `BalancesOf(...)` must include `Virtual(Adj)`; elsewhere `Adj` is just passed through.
- Delete `Receipt_into_a_negative_balance_throws_invalid_operation_until_S10` (`:308-315`) and add, in a `// Post: revaluation (S10)` region (assert movement `LineNo`, `FromLocationId`, `ToLocationId`, `Quantity`, `TotalCost`, `ReversesMovementId == null`, and the resulting balances with `Of(...)`):
  - `Receipt_lifting_a_negative_balance_revalues_out_to_adjustment` (N4): `Physical(A, −3m, −150m, luc: 50m, allowNegative: true)`, `Virtual(Sup)`, `Virtual(Adj)`; receipt 10 @ 56 → movements `[ (1, Sup→A, 10, 560.0000), (1001, A→Adj, 0, 18.0000) ]`; A `7 / 392.0000`, LUC `56.0000`; `Of(result, Adj).OnHand` unchanged.
  - `Receipt_lifting_a_negative_balance_revalues_in_from_adjustment` (X3): `Physical(A, −2m, −100m, luc: 50m, allowNegative: true)`; receipt 3 @ 10 → `[ (1, Sup→A, 3, 30.0000), (1001, Adj→A, 0, 80.0000) ]`; A `1 / 10.0000`, LUC `10.0000`.
  - `Receipt_to_exactly_zero_revalues_the_remainder` (N7): `Physical(A, −2m, −112m, luc: 56m, allowNegative: true)`; receipt 2 @ 60 → `(1001, A→Adj, 0, 8.0000)`; A `0 / 0.0000`, LUC `60.0000`.
  - `Inflow_that_leaves_the_balance_negative_posts_no_revaluation`: `Physical(A, −5m, −250m, luc: 50m, allowNegative: true)`; receipt 2 @ 56 → one movement; A `−3 / −138.0000`, LUC `56.0000` (OnHand ≤ 0 → cost / qty); `Adj` not in `result.Balances`.
  - `Zero_variance_posts_no_revaluation`: `Physical(A, −2m, −100m, luc: 50m, allowNegative: true)`; receipt 2 @ 50 → one movement; A `0 / 0.0000`, LUC `50.0000`.
  - `Transfer_into_a_negative_balance_revalues_the_destination`: `Physical(A, 10m, 600m, luc: 60m)`, `Physical(B, −2m, −100m, luc: 50m, allowNegative: true)`, `Virtual(Adj)`; transfer 5 A→B → `[ (1, A→B, 5, 300.0000), (1001, B→Adj, 0, 20.0000) ]`; A `5 / 300.0000`; B `3 / 180.0000`, LUC `60.0000`.
  - `Positive_adjustment_into_a_negative_balance_revalues`: `Physical(A, −2m, −100m, luc: 50m, allowNegative: true)`, `Virtual(Adj)`; adjustment `Line(Adj, A, 5m, 40m)` → `[ (1, Adj→A, 5, 200.0000), (1001, Adj→A, 0, 20.0000) ]`; A `3 / 120.0000`, LUC `40.0000`; `Of(result, Adj).OnHand` is −5 (the line's source).
  - `Revaluations_follow_all_lines_in_line_order`: two items (`Item`, `OtherItem`) both at A `−2 / −100`, LUC 50, toggle on, plus their `Sup` and `Adj` balances; one receipt with lines 1 (`Item`, 3 @ 10) and 2 (`OtherItem`, 3 @ 10) → `LineNo`s `[1, 2, 1001, 1002]`.
  - `Missing_adjustment_balance_when_a_revaluation_is_needed_throws`: the X3 setup without `Virtual(Adj)` → `InvalidOperationException`.

### 5. `tests/Inventory.IntegrationTests/SeedTests.cs`
- `BalanceTotal` (`:28`) counts the seed's balances: 31 physical plus 19 `SUP` today. Every opening-stock receipt now also creates an empty `(item, ADJ)` row. Count the distinct `ItemId`s in `InventorySeeder.OpeningStock` (`src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs`); it should be 19 (`CANDLE-WHT` has none). Set `BalanceTotal = 69`, with a comment: physical + SUP + ADJ (IMS-14 plan D1). If the distinct count isn't 19, stop and report it.

## Do not
- Touch files outside `files`, or touch `PostingRules.Reverse`, `CostingPolicy`, `ReverseAsync`, `DocumentPoster` or migrations. If you must, stop and report why.
- Add or edit raw SQL in the engine: the `ADJ` lookup is a LINQ query.
- Change the step-6 statement, its hints, or the step-5 insert's SQL.
- Add an optional/default value for `adjustmentLocationId`.
- Add dependencies, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingTests' --filter-class '*TransferTests' --filter-class '*AdjustmentTests' --filter-class '*PostingConcurrencyTests' --filter-class '*PostingVectorTests' --filter-class '*ReversalTests' --filter-class '*SeedTests' --filter-class '*PostingClockTests' --filter-class '*DocumentQueryTests' --filter-class '*ItemHistoryTests' --filter-class '*StockTests' --filter-class '*ItemsTests' --filter-class '*LocationsTests'
dotnet test            # full suite; if the Testcontainers flake shows: dotnet test -- --max-parallel-test-modules 1
! grep -n 'S10' src/Modules/Inventory/Inventory/Domain/*.cs
```
- T2, T3 (50 parallel transfers), T4 and the step-4/7 race tests stay green with `(item, ADJ)` in the lock set.
