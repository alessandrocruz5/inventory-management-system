---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 02
unit: IMS-33
status: todo
model: sonnet
effort: medium
depends_on: [01]
parallel_group: B
files:
  create:
    - tests/Inventory.IntegrationTests/InventoryReaderTests.cs
  modify: []
  delete: []
---
# 02 · Reader integration tests

## Task
Prove that `IInventoryReader` (prompt 01) returns what the HTTP read routes return for the caller's tenant, sees nothing of another tenant, and exposes no user identity.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Contract: `src/Modules/Inventory/Inventory.Contracts/IInventoryReader.cs` and `InventoryReadModels.cs`. Implementation: `src/Modules/Inventory/Inventory/InventoryReader.cs`.
- Fixture and helpers to reuse (read them first):
  - `DatabaseFixture` (assembly fixture): `db.CreateApp()`, `DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin)` → `(TenantId, Email)`, and `DatabaseFixture.SignInAsync(app, email)` → `HttpClient` (`tests/Inventory.IntegrationTests/DatabaseFixture.cs:64,78`).
  - `PostingTests.NewLocationAsync(client, ct)`, `PostingTests.NewItemAsync(client, sku, ct)`, `PostingTests.ReceiveAsync(client, location, item, qty, unitCost, ct)`. Read `PostingTests.cs` for the issue and transfer helpers, and post through the API the way they do.
  - A tenant-bound scope outside requests: `app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenantId)` (`Kernel.Tenancy`). Resolve `IInventoryReader` from `scope.Services`. This is the same scoping a request gets.
- Every test creates its own tenant (spec §11 "Test isolation"). Use `TestContext.Current.CancellationToken`. Shouldly. Copy the style of `StockTests.cs` and `DocumentQueryTests.cs`.
- Compare numbers as `decimal` values, never as text (TASKS.md Downstream IMS-8: `1` vs `1.0000`). Read the HTTP JSON with `GetDecimal()`.

### Tests (class `InventoryReaderTests(DatabaseFixture db)`)
1. `Stock_matches_the_stock_route`: two items × two locations with receipts at different costs and one issue. `GetStockAsync(null, null, 100, ct)` rows and `TotalStockValue` equal `GET /api/stock?pageSize=100`, row for row (`sku`, `locationCode`, `onHand`, `stockValue`, `averageCost`, `lastUnitCost`). The item and location filters match `?itemId=` and `?locationId=`.
2. `Item_and_history_match_their_routes`: `GetItemAsync` balances and units equal `GET /api/items/{id}`. `GetItemBySkuAsync` finds it with the SKU in lower case. `GetItemMovementsAsync(id, 25)` equals `GET /api/items/{id}/movements?pageSize=25`, in the same order (newest first).
3. `Documents_match_their_routes`: `ListDocumentsAsync("Receipt", from, to, 25)` equals `GET /api/documents?type=Receipt&postedFrom=…&postedTo=…`. `GetDocumentAsync` lines equal `GET /api/documents/{id}`. `ListDocumentsAsync("receipt", …)` throws `ArgumentException` with `ParamName == "type"`. `max: 0` and `max: 101` throw `ArgumentOutOfRangeException`.
4. `Locations_flag_physical_and_virtual`: the list includes the tenant's SUP/CUS/ADJ with `IsPhysical == false` and the new location with `IsPhysical == true`.
5. `Ledger_health_matches_the_route`: as an Admin, `GetLedgerHealthAsync` `Healthy` and the invariant ids, passes and failure counts equal `GET /api/ledger/health`.
6. `Another_tenants_rows_are_invisible`: tenants A and B each post. In A's scope, B's item id, B's SKU (B-only SKU) and B's document id read as `null`; `GetItemMovementsAsync(B item)` is `null`; A's stock, documents and locations contain none of B's ids.
7. `Contract_types_carry_no_user_identity`: reflect over every public type in `typeof(IInventoryReader).Assembly`. No property name contains `PostedBy`, `CreatedBy`, `UpdatedBy`, `Email`, `Version` or `UserId` (plan D10).

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change the reader or any handler. If a test exposes a mismatch, stop and report it.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*InventoryReaderTests'
dotnet test
```
- All 7 tests pass. Run the class twice: it's stable (each run makes its own tenants).
