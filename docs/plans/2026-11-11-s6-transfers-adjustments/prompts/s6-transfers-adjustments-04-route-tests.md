---
type: prompt
plan: "[[2026-11-11-s6-transfers-adjustments]]"
id: 04
unit: IMS-10
status: done
model: sonnet
effort: high
depends_on: [03]
parallel_group: D
files:
  create:
    - tests/Inventory.IntegrationTests/TransferTests.cs
    - tests/Inventory.IntegrationTests/AdjustmentTests.cs
  modify:
    - tests/Inventory.IntegrationTests/PostingVectorTests.cs
    - tests/Inventory.IntegrationTests/PostingTests.cs
  delete: []
---
# 04 · Route behaviour tests and the T1 API run of set A

## Task
Add integration tests that prove the transfer and adjustment routes end to end, through the real API and SQL Server, plus T1's API run of vector set A. Tests only: no production code changes. If a test exposes a production bug, stop and report it with the failing assertion. Don't change `src/`.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-10`. Don't switch it. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any `dotnet` command.
- **Stack:** xUnit v3 (`[Fact]`, `[Theory]`, `TestContext.Current.CancellationToken`), Shouldly, `System.Text.Json.Nodes`. The test class takes `DatabaseFixture db` by primary constructor (assembly fixture). **Every test creates its own tenant** (spec §11).
- **Helpers to reuse (all `internal static`):**
  - `PostingTests`: `PostDocumentAsync(client, route, key, body, ct)`, `Receipts`, `Issues`, `Receipt(location, lines…)`, `Line(item, qty, cost)`, `Issue(location, lines…)`, `IssueLine(item, qty)`, `BalanceAsync(connection, item, location, ct)` → `(OnHand, StockValue)?`, `NewKey()`, `ReceiveAsync(client, location, item, qty, cost, ct)`, `NewLocationAsync(client, ct, code = "WH-1")`, `NewItemAsync(client, sku, ct)`, `ScalarAsync<T>(connection, sql, parameter, ct)`.
  - `DatabaseFixture`: `CreateApp()`, `CreateTenantWithUserAsync(app, Roles.Admin|Roles.Staff)` → `(TenantId, Email)`, `SignInAsync(app, email)`.
  - `LedgerRows`: `OpenAsync(db, tenant, ct)` (an RLS connection as `inventory_app`), `LocationIdAsync(connection, "SUP"|"CUS"|"ADJ", ct)`.
  - **Reuse, don't copy:** in `PostingTests.cs`, change exactly three modifiers from `private static` to `internal static`:
    - `AddUserAsync(ApiFactory app, Guid tenant, string role)` → `(Guid Id, string Email)` (`:602`), which makes a Staff user in the same tenant;
    - `AssertInsufficientStockAsync(response, item, location, available, requested, ct)` (`:648`);
    - `AssertValidationAsync(response, key, ct)` (`:662`), which checks the errors' keys are exactly `[key]`.

    Change nothing else in that file. Call them as `PostingTests.AddUserAsync(...)` and so on.
  - Location deactivation is `POST /api/locations/{id}/deactivate` as Admin (S4).
- **Response shape:** `{ id, type, reasonCode, reference, notes, postedAt, postedBy, movements: [{ id, lineNo, itemId, fromLocationId, toLocationId, quantity, totalCost, reversesMovementId }], balances: [{ itemId, locationId, onHand, stockValue, lastUnitCost }], balancesAsOf }`. `balances` lists only physical balances. **Compare decimals as numbers** (`GetValue<decimal>()`), never as text: a 201 and a replay format differently (IMS-8 Downstream).
- **Bodies:** add `internal static` builders in each new class, for prompt 05 and later slices to reuse:
  - `TransferTests.Transfer(Guid from, Guid to, params object[] lines)` → `new { fromLocationId = from, toLocationId = to, reference = (string?)null, notes = (string?)null, lines }`, and `TransferTests.TransferLine(Guid itemId, decimal quantity)` → `new { itemId, quantity }`. `internal const string Transfers = "/api/documents/transfers";`
  - `AdjustmentTests.Adjustment(Guid location, string? reasonCode, params object[] lines)` → `new { locationId = location, reasonCode, reference = (string?)null, notes = (string?)null, lines }`, and `AdjustmentTests.Delta(Guid itemId, decimal quantityDelta, decimal? unitCost = null)` → `new { itemId, quantityDelta, unitCost }`. `internal const string Adjustments = "/api/documents/adjustments";`

### `TransferTests.cs` (create): `public sealed class TransferTests(DatabaseFixture db)`
1. `Transfer_costs_at_the_source_average_and_carries_it` (V1–V5, spec §7). Admin creates locations A and B and an item. Then:
   - Receive 10 @ 100.00 and 10 @ 120.00 into A; issue 15 from A; receive 5 @ 130.00 into A. A is now 10 / 1,200.0000.
   - **As a Staff user**, transfer 4 A → B.
   - Expect `201`, `type` `"Transfer"` and `reasonCode` null.
   - One movement A → B: `lineNo` 1, `quantity` 4, `totalCost` 480.0000.
   - `balances` has exactly A (6 / 720.0000, `lastUnitCost` 120.0000) and B (4 / 480.0000, `lastUnitCost` 120.0000).
   - The database agrees: `BalanceAsync`, and `SELECT ReasonCode FROM inventory.StockDocuments WHERE Id = @id` is `DBNull`.
2. `Multi_line_transfer_numbers_movements_in_line_order`: two items; movement ids ascend with `lineNo`. Each of the 4 physical balances' `LastMovementId` is its line's movement id (read `LastMovementId` on the RLS connection).
3. `Transfer_beyond_on_hand_is_422_and_writes_nothing`: A holds 3, transfer 4 → `insufficient-stock` (`itemId`, `locationId` A, `available` 3, `requested` 4). The document count is unchanged, A is unchanged, and B has no balance row.
4. `Transfer_to_the_same_location_is_400`: keys `["toLocationId"]`.
5. `[Theory] Transfer_to_or_from_a_non_physical_location_is_400`, with `from`/`to` ∈ {SUP, CUS, ADJ, an unknown `Guid.NewGuid()`} → the key of the side that's wrong. Use a `MemberData` of `(string side, string code)`. Resolve the code via `LedgerRows.LocationIdAsync`, with `"none"` meaning a new Guid.
6. `Transfer_into_an_inactive_location_is_422`: B created and deactivated while empty → `inactive-location`, `locationId` B. A is unchanged.
7. `Same_key_and_body_replays_with_200` (`Idempotent-Replayed: true`, same `id`, one document), and `Same_key_different_body_is_409` (`originalDocumentId`).
8. `Same_key_and_body_on_the_issue_route_is_409`: post a transfer, then the same key on `/issues` with an issue body. Expect `409 idempotency-key-reused`: the route is part of the hash.

### `AdjustmentTests.cs` (create): `public sealed class AdjustmentTests(DatabaseFixture db)`
1. `Staff_gets_403_and_nothing_is_written`: a Staff user posts a valid adjustment → `403`. The tenant has 0 `Adjustment` documents.
2. `Negative_line_costs_at_the_average_and_stores_the_reason`. Receive 3 @ 10.00 and 3 @ 12.00 (6 / 66.0000), then post `-2` with `reasonCode` `"Damaged"`. Expect:
   - `201`, `type` `"Adjustment"`, `reasonCode` `"Damaged"`.
   - The movement goes location → ADJ with cost 22.0000; the balance is 4 / 44.0000.
   - The database has `ReasonCode` `'Damaged'`.
   - The ADJ balance `OnHand` is `+2`. Read it with a `SELECT OnHand … LocationId = @adj`: virtual balances aren't in the response.
3. `Positive_line_without_a_cost_takes_the_average`: from 3 / 30.0200 (receive 1 @ 10.00, 2 @ 10.01), `+3` with no cost → movement ADJ → location, 30.0200, balance 6 / 60.0400, `lastUnitCost` 10.0067.
4. `Positive_line_with_a_cost_uses_it`: `+2 @ 12.50` → 25.0000.
5. `Mixed_sign_document_moves_each_line_its_own_way`: items X (stock 5 @ 10) and Y (none). Lines `X -1` and `Y +4 @ 2.50` give two movements: X location → ADJ at 10.0000, and Y ADJ → location at 10.0000. Both physical balances are in the response.
6. `Unit_cost_is_required_when_nothing_is_on_hand` (A7 shape):
   - `+1` with no cost, for an item never received → `400`, keys exactly `["lines[0].unitCost"]`.
   - No document; no balance row for the item at the location or at ADJ.
   - **The key isn't consumed**: the same key with `unitCost` 5 then returns `201`.
7. `[Theory] An_invalid_reason_is_400`, with `null`, `""`, `"damaged"`, `"1"`, `"Bogus"` → keys exactly `["reasonCode"]`.
8. `A_unit_cost_on_a_negative_line_is_400` → `["lines[0].unitCost"]`; `A_zero_delta_is_400` → `["lines[0].quantityDelta"]`.
9. `Negative_line_beyond_on_hand_is_422`: holds 2, `-3` → `insufficient-stock`, `available` 2, `requested` 3.
10. `Adjustment_of_a_virtual_location_is_400` (SUP) → `["locationId"]`.
11. `Balance_beyond_the_column_range_names_quantityDelta`: mirror `PostingTests.cs:189-212`.
    - Receive `999_999_999.9999` @ `1_000_000` of X.
    - Adjust lines `Y +1 @ 1` and `X +1 @ 100` → keys exactly `["lines[1].quantityDelta"]`; no new document.
12. `Same_key_and_body_replays_with_200` (with `reasonCode` in the replayed body), and `A_changed_reason_with_the_same_key_is_409`.

### `PostingVectorTests.cs` (modify): add T1's API run of set A
- Add `[Fact] Set_A_adjusts_at_the_average_and_takes_the_remainder` beside the existing set-R test (`:14-60`), in the same style: a table of steps, posted as an Admin at location `"D"`.

| Step | Request | Movement cost | D after |
|---|---|---|---|
| A1 | Receipt 1 @ 10.00 | 10.0000 | 1 / 10.0000 |
| A2 | Receipt 2 @ 10.01 | 20.0200 | 3 / 30.0200 |
| A3 | Adjustment `+3`, no cost | 30.0200 | 6 / 60.0400 |
| A4 | Adjustment `-1` | 10.0067 | 5 / 50.0333 |
| A5 | Adjustment `+2 @ 12.50` | 25.0000 | 7 / 75.0333 |
| A6 | Adjustment `-7` | 75.0333 (the remainder) | 0 / 0.0000 |
| A7 | Adjustment `+1`, no cost | `400`, keys exactly `["lines[0].unitCost"]` | 0 / 0.0000, unchanged |

- Comment A3 with the spec note: "3 × 30.02 ÷ 3; rounding the average first gives 30.0201, which is wrong." Each step asserts the response and `BalanceAsync`, like set R.
- After A6, assert the received total (A1 + A2 + A3 + A5 = 85.0400) equals the removed total (A4 + A6 = 85.0400).
- Update the class summary to "vector sets R and A".

## Do not
- Touch files outside the `files` list. If you must, stop and report why. In particular, don't edit anything in `src/`. In `PostingTests.cs`, change only the three modifiers above: no test, body or other member.
- Weaken or skip an assertion to make a test pass. Report a mismatch instead.
- Add packages.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*TransferTests' --filter-class '*AdjustmentTests' --filter-class '*PostingVectorTests'
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
git diff --stat develop -- src   # unchanged since prompt 03
git diff -U0 develop -- tests/Inventory.IntegrationTests/PostingTests.cs | grep -E '^[-+][^-+]'   # exactly 3 '-private static' / '+internal static' pairs
```
