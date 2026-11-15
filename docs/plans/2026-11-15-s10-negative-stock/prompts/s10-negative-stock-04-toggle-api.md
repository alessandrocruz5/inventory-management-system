---
type: prompt
plan: "[[2026-11-15-s10-negative-stock]]"
id: 04
unit: IMS-14
status: todo
model: opus
effort: high
depends_on: [01]
parallel_group: D
files:
  create: []
  modify:
    - src/Shared/Kernel/Problems/ProblemTypes.cs
    - src/Modules/Inventory/Inventory/Features/Problems.cs
    - src/Modules/Inventory/Inventory/Features/Locations/UpdateLocation/Request.cs
    - src/Modules/Inventory/Inventory/Features/Locations/UpdateLocation/Handler.cs
    - tests/Inventory.IntegrationTests/LocationsTests.cs
  delete: []
---
# 04 · Toggle API: `PUT allowNegativeStock` with §6 lock order

## Task
Let `PUT /api/locations/{id}` change a physical location's negative-stock toggle. The field is optional, and absent means unchanged. A change follows spec §6's order: lock the location's balances, check them, then update the location and mirror the flag onto every balance, in one transaction. Turning the toggle off over a negative balance answers `422 negative-balances-exist`. Add the slug to Kernel and the problem result, then rewrite and extend `LocationsTests`.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-14`. Stop if `git branch --show-current` says otherwise. .NET 10, `Nullable` on, `TreatWarningsAsErrors`; module types are `internal`. Raw SQL only through EF (`FromSql`), never a hand-opened connection. `TenantId` never comes from a request.
- Spec §5 `:286`: *"Changing the toggle updates the location and all its balance rows in one transaction, in the lock order of section 6."* §6 `:325-326`: *"Turning a location's negative-stock toggle off fails with `422 negative-balances-exist` while any of its balances has `OnHand < 0`. Deactivation and toggle changes first lock the affected balance rows `WITH (UPDLOCK, HOLDLOCK)` (through `IX_Balances_Location` for a location), then check them, then update the item or location row. A posting that started earlier finishes first; one that starts later sees the change at step 7."* §9 `:591`: `negative-balances-exist` · 422 · "balances that block turning the toggle off".
- Hidden coupling (CLAUDE.md): `StockBalances.AllowNegative` mirrors `Locations.AllowNegativeStock`; each new slug lands in Kernel `ProblemTypes` and Angular `PROBLEM_MESSAGES` in the same PR (prompt 07 does Angular; spec §9 already lists it).
- Read first: `Features/Locations/UpdateLocation/{Endpoint,Handler,Request}.cs` (today: validate code/name and a required `version` → load (404) → system location (400 `location`) → duplicate code (400 `code`) → set `RowVersion` `OriginalValue` from `request.Version` → save; `DbUpdateConcurrencyException` → `Problems.ConcurrencyConflict()`, unique violation → 400 `code`). Then `Features/Locations/DeactivateLocation/Handler.cs:13-73`, **the pattern to copy**: `CreateExecutionStrategy().ExecuteAsync(...)`, `db.ChangeTracker.Clear()` per attempt, `BeginTransactionAsync`, then the lock statement at `:43-48`. Also `Features/Problems.cs:24-31,95-96` (`StockNotZero`, `BlockingBalance`), `src/Shared/Kernel/Problems/ProblemTypes.cs`, and `Persistence/StockBalance.cs` (`AllowNegative` is settable; `RowVersion` is `IsRowVersion()`).

### 1. Kernel and problem result
- `ProblemTypes.NegativeBalancesExist = "negative-balances-exist"`, with an XML doc in the file's style: "Turning a location's negative-stock toggle off is blocked: a balance there is below zero (spec §6)."
- `Problems.NegativeBalancesExist(IEnumerable<StockBalance> balances)`: 422, title `"Negative balances exist"`, type `ProblemTypes.Uri(ProblemTypes.NegativeBalancesExist)`, and extension `["balances"]` = the `BlockingBalance(ItemId, LocationId, OnHand, StockValue)` list, the same shape as `StockNotZero`.

### 2. Request
`internal sealed record UpdateLocationRequest(string? Code, string? Name, byte[]? Version, bool? AllowNegativeStock);`. When the field is null or absent, the toggle is left unchanged (plan D7). Don't add it to `CreateLocationRequest`.

### 3. Handler (plan D8, D9)
- `HandleAsync` keeps the pure validation (code, name, version → `Problems.Validation`) **before** any database work, so the T5 probe (`TenantIsolationTests.cs:409-432`, a body without the toggle) still gets its `404` from the load. Then: `return await db.Database.CreateExecutionStrategy().ExecuteAsync(token => UpdateAsync(id, request, code, name, token), cancellationToken);`. The return type stays `Results<Ok<LocationResponse>, ValidationProblem, ProblemHttpResult>`.
- `UpdateAsync`: add a comment that the lock lasts until commit, so this runs in a transaction inside the retrying strategy (IMS-3). Then:
  1. `db.ChangeTracker.Clear();` and `await using var transaction = await db.Database.BeginTransactionAsync(ct);`.
  2. Load, then the 404, system-location and duplicate-code checks exactly as today.
  3. `if (request.AllowNegativeStock is { } allow && allow != location.AllowNegativeStock)`:
     - Comment: spec §6, lock the location's balances through `IX_Balances_Location`, then check them, then update the location. `HOLDLOCK` also locks the empty range, so no posting adds a balance here before we commit. A posting that waits on these locks reads the new flag at step 7.
     - `var balances = await db.StockBalances.FromSql($"SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, HOLDLOCK, FORCESEEK, INDEX([IX_Balances_Location])) WHERE [LocationId] = {id}").AsTracking().ToListAsync(ct);`. This is the same SQL text as `DeactivateLocation/Handler.cs:44`, but **tracked** and with no `.Where` composed on top.
     - If `!allow`: `var negative = balances.Where(b => b.OnHand < 0).OrderBy(b => b.ItemId).ToList();` and, if it's not empty, `return Problems.NegativeBalancesExist(negative);` (no commit, so it rolls back).
     - `foreach (var balance in balances) balance.AllowNegative = allow;` then `location.AllowNegativeStock = allow;`. The balance updates are per-row by primary key from tracked entities. **Never** write a set-based `UPDATE … WHERE LocationId = …`: it could scan and U-lock other locations' rows.
  4. Code, name, `UpdatedAt`/`UpdatedBy` and `db.Entry(location).Property(l => l.RowVersion).OriginalValue = request.Version!` as today; one `SaveChangesAsync`, with the same two `catch`es returning the same results (no commit, so a stale version rolls back the balance updates too); then `await transaction.CommitAsync(ct);` and `TypedResults.Ok(LocationResponse.From(location))`.
- `LocationResponse` already returns `allowNegativeStock` and the new `version`.

### 4. `tests/Inventory.IntegrationTests/LocationsTests.cs`
Read the file first. It has `CreateAsync(client, code, name, ct)` → the 201 body, `AddUserAsync`, the tenant scope pattern (`ITenantScopeFactory.BeginScope(tenant)` → `InventoryDbContext`), and `ItemsTests.AssertStockNotZeroAsync`. Posting helpers live in `PostingTests` (`ReceiveAsync`, `PostDocumentAsync`, `Issues`, `Issue`, `IssueLine`, `NewItemAsync`, `NewKey`) and `LedgerRows` (`OpenAsync`).
- Add the shared helper `internal static async Task<JsonNode> AllowNegativeAsync(HttpClient client, Guid id, bool allow, CancellationToken ct)`: `GET /api/locations?page=1&pageSize=100`, find the row with that `id`, PUT `{ code, name, version, allowNegativeStock = allow }` from it, assert `200`, return the body. Prompts 05 and 06 call it.
- Rewrite `Update_changes_code_and_name_and_ignores_allowNegativeStock` (`:104-132`) as `Update_changes_code_name_and_allowNegativeStock`: the same PUT now returns `allowNegativeStock: true`, and the row's `AllowNegativeStock` is true.
- Add:
  - `Toggle_on_and_off_mirrors_every_balance`: create a location and two items; receive into it through the API so balances exist; `AllowNegativeAsync(true)` → `SELECT AllowNegative FROM inventory.StockBalances WHERE LocationId = @id` (on a `LedgerRows.OpenAsync` connection) is all 1; `AllowNegativeAsync(false)` → all 0. The `SUP` balances keep `AllowNegative` 0.
  - `Toggle_off_over_a_negative_balance_is_422`: toggle on; receive 4 @ 50; issue 6 (→ −2 / −100.0000, `201` thanks to prompt 01); PUT `allowNegativeStock: false` with the current version → `422`, `type` `/problems/negative-balances-exist`, `balances` has exactly one entry with `itemId`, `locationId`, `onHand` −2, `stockValue` −100 (compare with `GetValue<decimal>()`). The location is still on, the balance's `AllowNegative` is still 1, and the version is unchanged.
  - `Absent_allowNegativeStock_leaves_the_toggle_unchanged`: toggle on, then PUT `{ code, name, version }` → `200`, `allowNegativeStock` still true.
  - `Toggle_with_a_stale_version_is_409`: toggle on using version v1 → v2; PUT `false` with v1 → `409 concurrency-conflict`; the row is still on, and its balances still have 1.
  - `Staff_gets_403_when_flipping_the_toggle` (T15): Staff PUT `{ code, name, version, allowNegativeStock = true }` → `403`, no `Location` header; the row is still off, with the same version.
- Leave `Staff_gets_403_on_every_admin_route` and the T5 probe unchanged.

## Do not
- Touch files outside `files`. If you must, stop and report why. The Angular map is prompt 07's.
- Change `CreateLocation`, `DeactivateLocation`, `LocationResponse`, the posting engine, migrations or spec §6.
- Add dependencies, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LocationsTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests'
dotnet test --project tests/Architecture.Tests
grep -n 'NegativeBalancesExist' src/Shared/Kernel/Problems/ProblemTypes.cs src/Modules/Inventory/Inventory/Features/Problems.cs
```
- T5 still gets `404` for tenant B's location on PUT, and T10's `No_request_type_carries_a_date` stays green.
