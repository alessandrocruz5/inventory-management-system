---
type: prompt
plan: "[[2026-11-15-s9-reversals]]"
id: 04
unit: IMS-13
status: todo
model: sonnet
effort: high
depends_on: [03]
parallel_group: D
files:
  create:
    - tests/Inventory.IntegrationTests/ReversalTests.cs
  modify:
    - tests/Inventory.IntegrationTests/LedgerInvariants.cs
    - tests/Inventory.IntegrationTests/PostingVectorTests.cs
    - tests/Inventory.IntegrationTests/PostingClockTests.cs
  delete: []
---
# 04 · T8, set V, INV-4, T10

## Task
Prove the reversal route through the real API: T8 (spec §11) and the route's rejections in a new `ReversalTests`, the T1 API run of vector set V in `PostingVectorTests`, INV-4 in the `LedgerInvariants` oracle, and a reversal in T10. Tests only; if a test exposes a product bug, stop and report it with the failing assertion. Never weaken a test to pass.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-13`. The route exists (prompt 03): `POST /api/documents/{id}/reversal`, Admin only, `Idempotency-Key` required, empty body. It returns `201` + `Location: /api/documents/{reversalId}` with the posting response `{ id, type, reversesDocumentId, reasonCode, reference, notes, postedAt, postedBy, movements: [{ id, lineNo, itemId, fromLocationId, toLocationId, quantity, totalCost, reversesMovementId }], balances: [{ itemId, locationId, onHand, stockValue, lastUnitCost }] (physical only), balancesAsOf }`, or a replay `200` + `Idempotent-Replayed: true`.
- Problems (spec §9, plan D2): `404 not-found`; `422 cannot-reverse-reversal` (`reversesDocumentId`); `409 already-reversed` (`reversalDocumentId`); `409 reversal-not-latest` (`itemId`, `locationId`, `blockingDocumentId`); `422 inactive-item` (`itemId`) / `inactive-location` (`locationId`); `409 idempotency-key-reused` (`originalDocumentId`); `400 idempotency-key-missing`; Staff `403`.
- Test conventions: xUnit v3 + Shouldly; `public sealed class X(DatabaseFixture db)`; `var ct = TestContext.Current.CancellationToken; await using var app = db.CreateApp(); var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin); using var client = await DatabaseFixture.SignInAsync(app, email);` (`Roles` from `Platform.Identity`). Every test makes its own tenant. Compare decimals as numbers (`GetValue<decimal>()`), never text (IMS-8). Copy shapes from `PostingTests.cs` and `AdjustmentTests.cs:20-38`.
- Helpers to reuse: `PostingTests.PostDocumentAsync(client, route, key, body, ct)`, `Receipts`, `Issues`, `Receipt(location, lines…)`, `Line(item, qty, unitCost)`, `Issue(location, lines…)`, `IssueLine(item, qty)`, `BalanceAsync(connection, item, location, ct)` → `(OnHand, StockValue)?`, `NewKey()`, `ReceiveAsync`, `NewLocationAsync(client, ct, code)`, `NewItemAsync(client, sku, ct)`, `AddUserAsync(app, tenant, role)`, `ScalarAsync<T>`; `TransferTests.Transfers`, `TransferTests.Transfer(from, to, lines…)`, `TransferTests.TransferLine(item, qty)`; `AdjustmentTests.Adjustments`, `AdjustmentTests.Adjustment(location, reason, lines…)`, `AdjustmentTests.Delta(item, delta, unitCost?)`; `LedgerRows.OpenAsync(db, tenant, ct)`, `LedgerRows.LocationIdAsync(connection, "SUP"|"CUS"|"ADJ", ct)`; `LedgerInvariants.AssertHoldAsync(connection, ct)`. Deactivate with `POST /api/items/{id}/deactivate` / `POST /api/locations/{id}/deactivate` (no body; `200` once stock is zero).

### `ReversalTests.cs` (create)
Summary: T8 (spec §11) and the reversal route's rejections; every fact ends with `LedgerInvariants.AssertHoldAsync`. Public helpers for prompt 05 and T10:
```csharp
internal static string Route(Guid documentId) => $"/api/documents/{documentId}/reversal";
/// <summary>Posts a reversal with no body, with the Idempotency-Key header when key isn't null.</summary>
internal static async Task<HttpResponseMessage> ReverseAsync(HttpClient client, Guid documentId, string? key, CancellationToken ct)
internal static async Task<Guid> IdAsync(HttpResponseMessage response, CancellationToken ct)
/// <summary>OnHand, StockValue and LastUnitCost of the balance row, or null when there is none.</summary>
internal static async Task<(decimal OnHand, decimal StockValue, decimal? LastUnitCost)?> BalanceAsync(SqlConnection connection, Guid item, Guid location, CancellationToken ct)
internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string slug, CancellationToken ct) // returns nothing; asserts status and type "/problems/<slug>"
```
Facts (names may be adjusted for clarity; each must exist):
1. `Reversing_a_two_line_receipt_restores_both_balances`: items X, Y at A; one receipt with `Line(X, 3m, 10m)` and `Line(Y, 2m, 7.5m)`. Reverse → `201`; `Location` = `/api/documents/{reversalId}`; `type` `Reversal`; `reversesDocumentId` = the receipt; `reasonCode` null; two movements, each `fromLocationId` = A, `toLocationId` = SUP, `quantity`/`totalCost`/`lineNo` equal to the receipt movement it names in `reversesMovementId`; `balances` X@A and Y@A at 0 / 0 with `lastUnitCost` null. DB: X@A and Y@A `(0, 0, null)`; X@SUP and Y@SUP `OnHand` 0.
2. `Reversing_an_issue_restores_value_and_last_unit_cost`: receive 10 @ 100 at A, issue 3 → A 7 / 700. Reverse the issue → A `(10, 1000, 100)`; CUS `OnHand` 0.
3. `Reversing_a_transfer_restores_both_locations`: receive 10 @ 100 at A, 5 @ 120 at B; transfer 4 A→B. Assert B is `(9, 1000, 111.1111)` (transfer-in sets LUC). Reverse → A `(10, 1000, 100)`, B `(5, 600, 120)`.
4. `Reversing_an_adjustment_stores_no_reason`: receive 3 @ 10 at D, adjustment `Damaged` with `Delta(item, -1m)` → D 2 / 20. Reverse → `201`, `reasonCode` null; D `(3, 30, 10)`; `SELECT ReasonCode FROM inventory.StockDocuments WHERE Id = @id` is DBNull.
5. `Reversing_twice_is_409_already_reversed`: reverse once (key K1), again with K2 → `409 already-reversed`, `reversalDocumentId` = the first reversal; one `Reversal` document.
6. `Reversing_a_reversal_is_422`: reverse a receipt, then reverse that reversal → `422 cannot-reverse-reversal`, `reversesDocumentId` = the receipt.
7. `Reversing_a_non_latest_document_is_409`: receive 5 @ 10 at A (R), issue 1 at A (I); reverse R → `409 reversal-not-latest`, `itemId` = item, `locationId` = A, `blockingDocumentId` = I; A still `(4, 40)`; no `Reversal` document.
8. `Same_key_replays_with_200`: reverse with K twice → second `200`, `Idempotent-Replayed: true`, same `id`; one `Reversal` document.
9. `Same_key_elsewhere_is_409_idempotency_key_reused`: (a) reverse R1 with K, then reverse R2 (another item, still latest) with K → `409 idempotency-key-reused`, `originalDocumentId` = the R1 reversal. (b) post a receipt with K2, then reverse R2 with K2 → `409`, `originalDocumentId` = that receipt. R2 unreversed afterwards.
10. `Missing_key_is_400_and_unknown_id_is_404`: `ReverseAsync(client, receiptId, null, ct)` → `400 idempotency-key-missing`; `ReverseAsync(client, Guid.NewGuid(), NewKey(), ct)` → `404 not-found`.
11. `Staff_gets_403_and_nothing_is_written` (copy `AdjustmentTests.cs:20-38`): Admin receives; Staff reverses → `403`; no `Reversal` document; balance unchanged.
12. `Inactive_item_is_422_and_the_key_stays_free`: receive 5 @ 10 at A, issue 5 (latest), deactivate the item; reverse the issue with K → `422 inactive-item`, `itemId`; no `Reversal` document; then a receipt of a **new** item with the same K → `201` (only a successful post consumes a key).
13. `Inactive_location_is_422`: receive 5 @ 10 at A, issue 5, deactivate A; reverse the issue → `422 inactive-location`, `locationId` = A.
14. `Reversal_links_both_ways_and_shows_in_item_history`: receive 10 @ 100 at A, issue 3, reverse the issue. `GET /api/documents/{issue}` → `reversedByDocumentId` = reversal; `GET /api/documents/{reversal}` → `reversesDocumentId` = issue, `type` `Reversal`, one line with `reversesMovementId` set. `GET /api/items/{item}/movements?pageSize=100` → the newest row has `documentType` `Reversal`, `direction` `In`, `locationId` A, `quantityChange` 3, `valueChange` 300, `runningOnHand` 10, `runningValue` 1000.

### `LedgerInvariants.cs` (modify)
Add INV-4 to `AssertHoldAsync` after INV-2, and update the class summary ("INV-1 to INV-5"; drop "INV-4 … is added with reversals in S9"):
- For every movement `m` with `ReversesMovementId = r`: the movement `r` exists (else failure `INV-4 movement {m.Id} reverses missing {r}`); `r` is not itself a reversal; `m.ItemId == o.ItemId`, `m.Quantity == o.Quantity`, `m.TotalCost == o.TotalCost`, `m.FromLocationId == o.ToLocationId`, `m.ToLocationId == o.FromLocationId` (one `Check` per rule, message naming both ids).
- No movement is reversed more than once (group by `ReversesMovementId`).

### `PostingVectorTests.cs` (modify)
Add `Set_V_moves_reverses_and_rejects_through_the_api` (spec §7 `:395-408`); update the class summary to "sets R, A and V". Locations `A`, `B` (`LocationsTests.CreateAsync`), item `V-1`. After each step assert the movement cost from the response and A's and B's `(OnHand, StockValue)` from the DB (`ReversalTests.BalanceAsync`):
| Step | Action | Cost | A | B |
|---|---|---|---|---|
| V1 | receive 10 @ 100.00 into A | 1000 | 10 / 1000 | — |
| V2 | receive 10 @ 120.00 into A | 1200 | 20 / 2200 | — |
| V3 | issue 15 from A | 1650 | 5 / 550 | — |
| V4 | receive 5 @ 130.00 into A | 650 | 10 / 1200 (LUC 120) | — |
| V5 | transfer 4 A→B | 480 | 6 / 720 | 4 / 480 |
| V6 | receive 6 @ 125.00 into B | 750 | 6 / 720 | 10 / 1230 (LUC 123) |
| V7 | reverse V6 | 750 (B → SUP) | 6 / 720 | 4 / 480 (LUC 120) |
| V8 | reverse V4 | `409 reversal-not-latest`, `locationId` A, `blockingDocumentId` V5 | unchanged | unchanged |
| V9 | issue 7 from A | `422 insufficient-stock`, `available` 6, `requested` 7 | unchanged | unchanged |
| V10 | issue 6 from A | 720 | 0 / 0 | 4 / 480 |
End with `LedgerInvariants.AssertHoldAsync`.

### `PostingClockTests.cs` (modify)
In `PostedAt_comes_from_the_clock` (`:23-66`), after the issue: `fake.Advance(TimeSpan.FromMinutes(5));` reverse the issue (`ReversalTests.ReverseAsync(client, issueId, PostingTests.NewKey(), ct)`, the client is Admin) → `201`; `postedAt` and `balancesAsOf` = `now.AddMinutes(10)`; `DocumentPostedAtAsync` and `MovementPostedAtAsync` for the reversal equal it. Leave `No_request_type_carries_a_date` unchanged (the route has no request type).

## Do not
- Touch files outside the `files` list (no product code, no `PostingConcurrencyTests.cs`: prompt 05). If a product bug blocks a test, stop and report.
- Write to `StockDocuments`/`StockMovements` except through the API.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*ReversalTests' --filter-class '*PostingVectorTests' --filter-class '*PostingClockTests' --filter-class '*TransferTests' --filter-class '*AdjustmentTests' --filter-class '*PostingConcurrencyTests'
dotnet test            # full suite (INV-4 now runs wherever AssertHoldAsync is called)
git diff --stat        # only the four declared files
```
