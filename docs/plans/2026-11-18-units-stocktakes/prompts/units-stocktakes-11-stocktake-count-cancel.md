---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 11
unit: IMS-30
status: todo
model: sonnet
effort: high
depends_on: [10]
parallel_group: K
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Stocktakes/CountStocktakeLine/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/CountStocktakeLine/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/CountStocktakeLine/Request.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/CancelStocktake/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/CancelStocktake/Handler.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Stocktakes/StocktakeEndpoints.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/StocktakeTests.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 11 · Stocktakes: count a line (with units and a snapshot), cancel

## Task
Add `PUT /api/stocktakes/{id}/lines/{itemId}` (Staff and Admin), which records a count with the balance snapshot and adds the line if it isn't on the sheet yet. Add `POST /api/stocktakes/{id}/cancel` (Admin). Add T5 probes and tests.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't.
- Prompt 10 added `StocktakeEndpoints`, `StocktakeReader`, the responses, `Problems.StocktakeNotOpen(status)`, `StocktakeTests.StartAsync` and the T5 probes for start, list and get.
- IMS-29 added `Features/Posting/UnitConversion.{ToBase, ActiveFactorsAsync}` and `PostingValidation.MaxAmount`.
- Plan D8: a count stores `CountedQuantity` (base units), `ExpectedQuantity` = the balance's `OnHand` **now** (0 if there's no balance row), `ExpectedLastMovementId` = its `LastMovementId` (null if none), `CountedAt` = `clock.GetUtcNow()` and `CountedBy` = `user.UserId`. A re-count overwrites all of them and takes a new snapshot (D15: last write wins, no version).
- Plan D15: every count runs in a strategy transaction that first U-locks the stocktake row: `db.Stocktakes.FromSql($"SELECT * FROM [inventory].[Stocktakes] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}")`. That serializes counts against posting's Draft → Posting step (prompt 12), so no count slips in after the lines freeze. The snapshot read of `StockBalances` is a plain read with no lock hint (plan Assumptions).
- Plan D11: count is any signed-in user. Cancel is `.RequireAuthorization(InventoryPolicies.Admin)`.
- T10: request types carry no dates.
- Patterns: `Features/Locations/DeactivateLocation/Handler.cs:13-73` (strategy, `ChangeTracker.Clear()` per attempt, transaction, `FromSql` lock) and `Features/Posting/PostingValidation.cs:371-385` (the 4-dp and `MaxAmount` messages; mirror them, don't change that file).

### 1. PUT /stocktakes/{id}/lines/{itemId}
- `CountStocktakeLineRequest(decimal? Quantity, string? Unit, decimal? UnitCost)`.
- Pure checks:
  - `quantity` is required, ≥ 0, at most 4 dp and ≤ `MaxAmount` → 400 `quantity`.
  - `unit` is normalized like posting lines (trim, `ToLowerInvariant`, empty → null, ≤ 16) → 400 `unit`.
  - `unitCost` is optional, ≥ 0, at most 4 dp and ≤ `MaxAmount` → 400 `unitCost`. It's per **base** unit (plan Assumptions).
- In the strategy transaction:
  1. Lock the stocktake row as above. Missing → 404. Status ≠ Draft → `Problems.StocktakeNotOpen(status)`.
  2. The item from the route: missing → 404, because a route id from tenant B is 404 under the T5 rule. Inactive → `Problems.InactiveItem(itemId)` (422).
  3. Resolve the unit:
     - null or `item.BaseUnit` → factor 1.
     - Otherwise an active unit from `UnitConversion.ActiveFactorsAsync(db, [itemId], ct)`, or 400 `unit` "Choose one of this item's units."
     - Then `UnitConversion.ToBase(quantity, factor)`; null → 400 `quantity` "This doesn't convert to whole 0.0001 {baseUnit}; enter it in {baseUnit}."
  4. Snapshot: `await db.StockBalances.AsNoTracking().Where(b => b.ItemId == itemId && b.LocationId == stocktake.LocationId).Select(b => new { b.OnHand, b.LastMovementId }).SingleOrDefaultAsync(ct)`.
  5. Upsert the `StocktakeLine` (add it if missing), set the five count fields and `UnitCost`, and set the stocktake's `UpdatedAt`/`UpdatedBy`. Save and commit.
- Return 200 with the full `StocktakeDetailResponse` from `StocktakeReader`, so the client re-renders the sheet.

### 2. POST /stocktakes/{id}/cancel (Admin)
In a strategy transaction with the same U lock:
- Missing → 404.
- Cancelled → 200 with the detail, writing nothing (idempotent).
- Draft → `Cancelled`, with `UpdatedAt`/`UpdatedBy`.
- Posting or Posted → `Problems.StocktakeNotOpen(status)`. A Posting stocktake is resolved by posting again (plan D10).
- Return 200 with the detail.

### 3. Map and register
Add the routes `"/{id:guid}/lines/{itemId:guid}"` (`MapPut`) and `"/{id:guid}/cancel"` (`MapPost`) to `StocktakeEndpoints`, and register both handlers.

### 4. Tests (`StocktakeTests.cs`, appended)
- `Count_records_the_snapshot_and_difference`: receive 10 @ 5 at L, start, count 8 → line `expectedQuantity` 10, `countedQuantity` 8, `difference` −2, `countedBy` = the user, `movedSinceCount` false. Then issue 1 through the API → GET shows `movedSinceCount` true, `currentOnHand` 9, and the expected value still 10.
- `Recount_takes_a_new_snapshot`: after the issue above, count 7 again → expected 9, difference −2, `movedSinceCount` false.
- `Count_in_a_unit_converts_to_base`: add unit `case` = 12. Count `{ quantity: 1.5, unit: "case" }` → counted 18. Count `{ quantity: 0.00001 }` → 400 `quantity`. Unit `box` → 400 `unit`.
- `Count_adds_an_item_not_on_the_sheet`: an item with no balance at L → the line is added with expected 0 and `expectedLastMovementId` null (check the row through a tenant scope).
- `Staff_can_count`.
- `Count_on_an_inactive_item_is_422` and `Count_after_cancel_is_409_stocktake_not_open` (`status` "Cancelled").
- `Cancel_is_idempotent_and_frees_the_location`: cancel twice → 200 and 200. A new start at L → 201.
- `Staff_gets_403_on_cancel`: no `Location` header, and the status is unchanged.
- `Concurrent_counts_on_one_line_leave_one_consistent_row`: 10 parallel PUTs with quantities 1..10 → all 200. The row's counted value is one of them, and its snapshot is consistent (`CK_StocktakeLines_Counted` holds).
- `TenantIsolationTests.cs`:
  - `PUT /api/stocktakes/{id:guid}/lines/{itemId:guid}`: B's stocktake id → 404, and A's stocktake with B's item id → 404.
  - `POST /api/stocktakes/{id:guid}/cancel`: B's id → 404.

## Do not
- Touch files outside `files`. If you must, stop and report why. Posting is prompt 12.
- Lock `StockBalances` in the count. Change `UnitConversion` or `PostingValidation`.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*StocktakeTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests'
dotnet test --project tests/Architecture.Tests
```
