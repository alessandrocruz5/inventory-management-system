---
type: prompt
plan: "[[2026-11-15-s10-negative-stock]]"
id: 05
unit: IMS-14
status: todo
model: sonnet
effort: high
depends_on: [02, 04]
parallel_group: E
files:
  create:
    - tests/Inventory.IntegrationTests/NegativeStockTests.cs
  modify:
    - tests/Inventory.IntegrationTests/PostingVectorTests.cs
  delete: []
---
# 05 · T15 through the API: N, X and posting cases

## Task
Prove S10 through the real routes against SQL Server. Add vector sets N and X to `PostingVectorTests`, and create `NegativeStockTests` for transfer-in, adjustment, never-received, replay and reversal cases. Test code only.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-14`. Stop if `git branch --show-current` says otherwise. xUnit v3 + Shouldly + Testcontainers; every test creates its own tenant (`DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin)`, `SignInAsync`). Compare JSON numbers with `GetValue<decimal>()`, never as text (a 201 and a replay format decimals differently, IMS-8).
- Done before you: prompts 01–02 (estimate, revaluation at `LineNo 1000 + n`, `(item, ADJ)` locked) and 04 (`LocationsTests.AllowNegativeAsync(client, locationId, allow, ct)` flips the toggle and asserts `200`).
- Read first: `PostingVectorTests.cs:14-60` (the step-table pattern to copy), `PostingTests.cs:556-600,648-660` (`PostDocumentAsync`, `Receipts`, `Issues`, `Receipt`, `Line`, `Issue`, `IssueLine`, `NewKey`, `ReceiveAsync`, `NewLocationAsync`, `NewItemAsync`, `BalanceAsync`, `AssertInsufficientStockAsync`, `AssertValidationAsync`), `TransferTests.cs` (its route constant and `TransferLine`, body helper near `:345`), `AdjustmentTests.cs:437-440` (`Adjustment(location, reasonCode, lines)`, `Delta(itemId, quantityDelta, unitCost)` and its route constant), `ReversalTests.cs:20-58` (`ReverseAsync`, `IdAsync`, `BalanceAsync` → `(OnHand, StockValue, LastUnitCost)`, `AssertProblemAsync`), `LedgerRows.cs` (`OpenAsync`, `LocationIdAsync(c, "ADJ")`), `LedgerInvariants.AssertHoldAsync(connection, ct)` (toggle-aware; checks INV-1 to INV-5), `StockTests.cs` (how `GET /api/stock` is called and read).
- A posting response has `movements` (sorted by `lineNo`: `id`, `lineNo`, `itemId`, `fromLocationId`, `toLocationId`, `quantity`, `totalCost`, `reversesMovementId`) and `balances` (physical only).

### 1. `PostingVectorTests`: two facts, at a location created and toggled on through the API
`Set_N_negative_stock_through_the_api` (location `K`). For each step, assert `201`, the line movement's `totalCost`, and the revaluation if any (the movement with `lineNo` 1001: `fromLocationId`/`toLocationId` against `K` and the tenant's `ADJ` id, `quantity` 0, `totalCost`), else exactly one movement. Also assert the single `balances` entry (`onHand`, `stockValue`) and `ReversalTests.BalanceAsync(...)` for `LastUnitCost`:

| Step | Post | Cost | Revaluation | K | LUC |
|---|---|---|---|---|---|
| N1 | receipt 4 @ 50.00 | 200.0000 | — | 4 / 200.0000 | 50.0000 |
| N2 | issue 6 | 300.0000 | — | −2 / −100.0000 | 50.0000 |
| N3 | issue 1 | 50.0000 | — | −3 / −150.0000 | 50.0000 |
| N4 | receipt 10 @ 56.00 | 560.0000 | K → ADJ 18.0000 | 7 / 392.0000 | 56.0000 |
| N5 | issue 7 | 392.0000 | — | 0 / 0.0000 | 56.0000 |
| N6 | issue 2 | 112.0000 | — | −2 / −112.0000 | 56.0000 |
| N7 | receipt 2 @ 60.00 | 120.0000 | K → ADJ 8.0000 | 0 / 0.0000 | 60.0000 |
| N8 | receipt 5 @ 60.00 | 300.0000 | — | 5 / 300.0000 | 60.0000 |
| N9 | issue 5 | 300.0000 | — | 0 / 0.0000 | 60.0000 |

Then: received 1,180.0000 = issued 1,154.0000 + revaluations 26.0000 (summed from the responses), and `LedgerInvariants.AssertHoldAsync` passes.

`Set_X_estimate_above_the_next_price_through_the_api` (location `K2`):

| Step | Post | Cost | Revaluation | K2 | LUC |
|---|---|---|---|---|---|
| X1 | receipt 4 @ 50.00 | 200.0000 | — | 4 / 200.0000 | 50.0000 |
| X2 | issue 6 | 300.0000 | — | −2 / −100.0000 | 50.0000 |
| X3 | receipt 3 @ 10.00 | 30.0000 | ADJ → K2 80.0000 | 1 / 10.0000 | 10.0000 |
| X4 | issue 1 | 10.0000 | — | 0 / 0.0000 | 10.0000 |

Then invariants. Update the class summary to mention sets N and X (T15).

### 2. `NegativeStockTests.cs` (new; `public sealed class NegativeStockTests(DatabaseFixture db)`, with a summary: "T15 (spec §11): negative stock through the API: estimates, revaluations, reversals and the toggle.")
Each fact creates its own tenant, locations (`PostingTests.NewLocationAsync(client, ct, code)`) and items, turns toggles on with `LocationsTests.AllowNegativeAsync`, and ends with `LedgerInvariants.AssertHoldAsync`.
- `Transfer_into_a_negative_balance_revalues_at_the_destination`: A (toggle off) receive 10 @ 60 → 10 / 600. B (toggle on) receive 4 @ 50, issue 6 → −2 / −100, LUC 50. Transfer 5 A→B → `201`; movements `[ (1, A→B, 5, 300.0000), (1001, B→ADJ, 0, 20.0000) ]`; balances A `5 / 300.0000`, B `3 / 180.0000`; B's LUC `60.0000`.
- `Never_received_item_is_estimated_at_zero_and_shows_negative`: K on; issue 2 of a new item → `201`, cost `0.0000`, K `−2 / 0.0000`, LUC null. `GET /api/stock` filtered to K has that row with `onHand` −2, `stockValue` 0 and `averageCost` null. Then receive 3 @ 10 → cost `30.0000`, revaluation `(1001, K→ADJ, 0, 20.0000)`, K `1 / 10.0000`, LUC `10.0000`.
- `Positive_adjustment_into_a_negative_balance_revalues`: K on; receive 4 @ 50, issue 6 → −2 / −100. Adjustment `Delta(item, 1)` with no unit cost → `400` with `errors["lines[0].unitCost"]` (no stock to average). Adjustment `Delta(item, 5, 40)`, reason `"Found"` → `201`; movements `[ (1, ADJ→K, 5, 200.0000), (1001, ADJ→K, 0, 20.0000) ]`; K `3 / 120.0000`, LUC `40.0000`.
- `Issue_past_zero_at_a_location_with_the_toggle_off_is_still_422`: receive 4 @ 50, issue 6 → `PostingTests.AssertInsufficientStockAsync` (available 4, requested 6); balance 4 / 200.
- `Replay_of_a_revaluing_receipt_returns_the_same_movements`: K on; N1, N2, N3; post N4 with key `k` → `201`; post the same key and body again → `200` with `Idempotent-Replayed: true`, the same document id, and identical movement `id`s, `lineNo`s `[1, 1001]` and `totalCost`s `[560, 18]`. Exactly one document holds `k`.
- `Reversal_restores_a_negative_balance_exactly`: K on; receive 4 @ 50, issue 6 (−2 / −100, LUC 50); receive 10 @ 56 → revaluation `K→ADJ 12.0000`, K `8 / 448.0000`, LUC 56. Reverse that receipt (`ReversalTests.ReverseAsync`) → `201`; movements `[ (1, K→SUP, 10, 560.0000, reversesMovementId = the receipt's line), (1001, ADJ→K, 0, 12.0000, reversesMovementId = its revaluation) ]`; K back to `−2 / −100.0000`, LUC `50.0000`; the `(item, ADJ)` balance's `OnHand` 0.
- `Reversal_after_the_toggle_is_turned_off_is_422` (S9 Downstream): the same up to K `8 / 448.0000`; `AllowNegativeAsync(false)` → `200` (8 ≥ 0); reverse the receipt → `422 insufficient-stock` with `itemId`, `locationId` K, `available` 8, `requested` 10. No `Reversal` document exists, and K is unchanged.

## Do not
- Touch files outside `files`, or production code. If an expected value doesn't reproduce, stop and report the case, the expected and the actual values; never change an expected value to match.
- Insert ledger rows directly: every posting here goes through the API.
- Add dependencies, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*NegativeStockTests' --filter-class '*PostingVectorTests'
```
- Every fact above passes, and `Set_R`, `Set_A` and `Set_V` stay green.
