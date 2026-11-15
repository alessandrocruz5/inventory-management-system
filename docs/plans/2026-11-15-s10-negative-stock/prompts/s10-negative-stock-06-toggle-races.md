---
type: prompt
plan: "[[2026-11-15-s10-negative-stock]]"
id: 06
unit: IMS-14
status: todo
model: opus
effort: high
depends_on: [04, 05]
parallel_group: F
files:
  create: []
  modify:
    - tests/Inventory.IntegrationTests/LocationsTests.cs
    - tests/Inventory.IntegrationTests/PostingConcurrencyTests.cs
  delete: []
---
# 06 · Toggle lock order and parallel issues past zero

## Task
Add deterministic lock-holder tests showing that the toggle follows spec §6's order (lock balances → check → update, and a posting that starts later sees the change at step 7). Add a parallel test of issues past zero that a receipt then settles. Test code only.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-14`. Stop if `git branch --show-current` says otherwise. xUnit v3 + Shouldly + Testcontainers; each test creates its own tenant. Compare numbers with `GetValue<decimal>()`.
- Spec §6 `:326`: *"Deactivation and toggle changes first lock the affected balance rows `WITH (UPDLOCK, HOLDLOCK)` (through `IX_Balances_Location` for a location), then check them, then update the item or location row. A posting that started earlier finishes first; one that starts later sees the change at step 7."* Prompt 04 implemented this in `Features/Locations/UpdateLocation/Handler.cs`: inside the execution strategy and a transaction, the tracked lock statement, then `422 negative-balances-exist` when turning off over `OnHand < 0`, then `AllowNegative` on every balance and the location row in one `SaveChanges`.
- **Copy these existing tests closely**: `LocationsTests.Deactivate_waits_for_an_uncommitted_balance` (`LocationsTests.cs`, around `:315-336`) and `LocationsTests.Deactivate_on_zero_stock_holds_off_postings_until_commit` (around `:338-371`). They use `LedgerRows.OpenAsync`, `LedgerRows.AddItemAsync`, `LedgerRows.AddBalanceAsync(c, item, location, isVirtual, allowNegative, onHand, value, ct, tx)`, `LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx)`, `ItemsTests.ExecuteAsync(conn, tx, sql, params)`, `ItemsTests.AssertLockTimeoutAsync(probe, sql, params)` and `ItemsTests.AssertRangeLockedAsync(db, probe, otherItem, location)`. Also read `LocationsTests.AllowNegativeAsync` (prompt 04) and `PostingConcurrencyTests.cs:33-60` (T2, with the `InParallelAsync`, `Count`, `Statuses` and `DocumentsOfTypeAsync` helpers at the end of the file).
- Posting helpers: `PostingTests.{PostDocumentAsync, Issues, Issue, IssueLine, Receipts, Receipt, Line, ReceiveAsync, NewLocationAsync, NewItemAsync, NewKey, BalanceAsync, AssertInsufficientStockAsync}`; `ReversalTests.BalanceAsync` returns `(OnHand, StockValue, LastUnitCost)`; `LedgerInvariants.AssertHoldAsync(connection, ct)`.

### 1. `LocationsTests`
- `Toggle_off_waits_for_an_uncommitted_negative_balance`: an Admin creates location `BR-1` and turns the toggle on (`AllowNegativeAsync`, keep the returned `version`). The holder connection adds an item (committed), then inside an open transaction adds a balance for it at `BR-1` with `allowNegative: true`, `−2m`, `−100m`. Start the PUT `{ code, name, version, allowNegativeStock = false }` without awaiting it; `WaitUntilBlockedAsync(db, holder, ct, tx)`; commit the holder. The PUT answers `422`, `/problems/negative-balances-exist`, with exactly that balance (`onHand` −2). Comment, as the deactivate test does, that under RCSI a plain read would skip the uncommitted balance; only the locking read makes the toggle wait for it. Don't assert ledger invariants here: the raw balance has no movements.
- `Toggle_off_holds_off_postings_until_commit`: an Admin creates location `BR-1`, turns the toggle on, creates an item through the API, and receives 1 @ 10 (balance 1 / 10). A holder transaction X-locks the location row (`UPDATE inventory.Locations SET Name = Name WHERE Id = @id`). Start the PUT `allowNegativeStock = false` with the current version, then `WaitUntilBlockedAsync`: the PUT now holds the balance locks and waits in `SaveChanges`. A probe with `SET LOCK_TIMEOUT 500;` shows that `SELECT … WITH (UPDLOCK, ROWLOCK)` on `(item, BR-1)` times out (`AssertLockTimeoutAsync`), and that a first balance for another item at `BR-1` is range-locked (`AssertRangeLockedAsync`). Start an issue of 3 of the item at `BR-1` through the API, without awaiting it, then roll the holder back. The PUT answers `200` with `allowNegativeStock` false. The issue answers `422 insufficient-stock` (available 1, requested 3): it either waited at step 6 or started later, and either way saw the change at step 7. The balance is still 1 / 10 with `AllowNegative` 0, and the invariants hold.

### 2. `PostingConcurrencyTests`
- `Twenty_parallel_issues_past_zero_all_post_then_a_receipt_settles`: create a location, turn the toggle on (`LocationsTests.AllowNegativeAsync`), create an item and receive 5 @ 10. Post 20 parallel issues of 1 with distinct keys (`InParallelAsync`) → all 20 are `201` (use `Statuses` in the message), and each response's single movement costs `10.0000`: the average is 10 while stock lasts, then `LastUnitCost` 10. The balance is −15 / −150.0000 with `LastUnitCost` 10.0000, and there are 20 `Issue` documents. Then receive 20 @ 12 → movements `[ (1, SUP→K, 20, 240.0000), (1001, K→ADJ, 0, 30.0000) ]`; the balance is 5 / 60.0000 with `LastUnitCost` 12.0000; `LedgerInvariants.AssertHoldAsync` passes.

## Do not
- Touch files outside `files`, or production code. If a test shows the handler takes locks in another order, stop and report it with the observed blocking; don't loosen the test.
- Use `Task.Delay` for ordering: use `WaitUntilBlockedAsync` and lock timeouts, as the existing tests do.
- Add dependencies, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LocationsTests' --filter-class '*PostingConcurrencyTests'
# run the two lock tests five times to catch flakiness
for i in 1 2 3 4 5; do dotnet test --project tests/Inventory.IntegrationTests --filter-method '*Toggle_off_*' || exit 1; done
```
- Both lock tests and the parallel test pass on every run, and the existing T2, T3, T4 and deactivate lock tests stay green.
