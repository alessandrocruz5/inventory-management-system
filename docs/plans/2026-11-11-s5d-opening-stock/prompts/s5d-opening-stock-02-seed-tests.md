---
type: prompt
plan: "[[2026-11-11-s5d-opening-stock]]"
id: 02
unit: IMS-9
status: todo
model: sonnet
effort: medium
depends_on: [01]
parallel_group: B
files:
  create:
    - tests/Inventory.IntegrationTests/SeedTests.cs
  modify: []
  delete: []
---
# 02 · SeedTests: content, replay, rename, isolation

## Task
Add `tests/Inventory.IntegrationTests/SeedTests.cs` with **one** `[Fact]`, `Seed_posts_opening_stock_once_through_the_engine`. It runs the real seed against the Inventory test container and proves four things in sequence:
- (a) the opening-stock content;
- (b) that an API post with a seed key and the same body replays it;
- (c) that a second seed after a branch rename posts nothing and doesn't throw;
- (d) that the isolation tenant has no stock.

Change only this test file.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-9`. Don't switch it. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any `dotnet` command.
- **Why one test (plan D8):** the fixture is an assembly fixture (`DatabaseFixture.cs:13`), so every class shares one container. Step (c) mutates the demo tenant permanently. Separate `[Fact]`s would depend on their run order. `SeedTests` owns `SeedTenants.Demo` in this assembly, and no other test uses it. This is a deliberate exception to spec §11 ("every integration test creates its own tenant"): the seed targets only the demo tenant. Say so in the class's `<summary>`.
- **From prompt 01, `Inventory.Seeding.InventorySeeder`** (internal; this project has `InternalsVisibleTo`):
  - `SystemPrincipal` (`Guid.Empty`) and `OpeningStockReference` (`"Opening stock"`).
  - `OpeningStock`, a `(string Code, string Sku, decimal Quantity, decimal UnitCost)[]` with 31 rows over 19 distinct items at QC and MKT. `CANDLE-WHT` isn't in it.
  - `OpeningStockKey(sku, code)` returns `$"seed:{SeedTenants.Demo:D}:{sku}:{code}"`.
- **Running the seed:** `await using var app = db.CreateApp(("Seed:Password", DatabaseFixture.UserPassword));` then `await SeedCommand.RunAsync(app.Services, ct);`. The method is `Api.Setup.SeedCommand`, which is internal, and `Api.csproj:24` grants this project `InternalsVisibleTo`. It runs `PlatformSeeder` (tenants `SeedTenants.Demo` and `SeedTenants.Isolation`, users `admin@demo.test` and `staff@demo.test` with that password, virtual locations SUP/CUS/ADJ), then `InventorySeeder`. Copy the shape of `tests/Platform.IntegrationTests/SetupTests.cs:164-180`.
- **Helpers to reuse** (don't copy them):
  - `LedgerRows.OpenAsync(db, tenantId, ct)` (`LedgerRows.cs:20`): a raw `SqlConnection` as `inventory_app` with the tenant's session context, so RLS limits it to that tenant.
  - `LedgerRows.LocationIdAsync(connection, code, ct)` (`:32`).
  - `PostingTests.ScalarAsync<T>(connection, sql, parameter, ct)` (`PostingTests.cs:634`).
  - `PostingTests.PostDocumentAsync(client, route, key, body, ct)` (`:556`) and `PostingTests.Receipts` (the receipts route constant).
  - `DatabaseFixture.SignInAsync(app, email)` (`DatabaseFixture.cs:78`, which signs in with `UserPassword`).
  - For multi-row reads, write a small private `SqlCommand` and reader in this file, like `PostingTests.BalanceAsync` (`:579`).
- **Class shape:** `public sealed class SeedTests(DatabaseFixture db)`, using `TestContext.Current.CancellationToken`, as in `PostingTests`. Use Shouldly. Use `CostingPolicy.Round4` (`Inventory.Domain`) for the expected `TotalCost` = Round4(qty × cost).

### Steps and assertions
1. **First seed.** Open `c = LedgerRows.OpenAsync(db, SeedTenants.Demo, ct)`. Resolve the QC, MKT and SUP ids with `LocationIdAsync`, and the item ids by `SELECT Id, Sku FROM inventory.Items`.
2. **(a) Content.**
   - `inventory.StockDocuments` has 31 rows. Every row has `Type = 'Receipt'`, `Reference = 'Opening stock'`, `Notes IS NULL`, `ReversesDocumentId IS NULL` and `PostedBy = SystemPrincipal`.
   - The keys, in posting order, match the table. Read each document's `IdempotencyKey` with the `Id` of its movement: `SELECT d.IdempotencyKey FROM inventory.StockDocuments d JOIN inventory.StockMovements m ON m.DocumentId = d.Id ORDER BY m.Id`. Movement ids come from the `MovementIds` sequence in posting order. Assert that list equals `OpeningStock.Select(r => OpeningStockKey(r.Sku, r.Code)).ToList()` in table order, case-sensitively. Never `ORDER BY IdempotencyKey`: it's `varchar` under the server's default collation. Never sort with a culture-sensitive C# `Order()`. If you also compare as sets, sort both sides in C# with `StringComparer.Ordinal`.
   - For each `OpeningStock` row, the document with its key has exactly one movement in `inventory.StockMovements`:
     - `[LineNo] = 1`. Quote `LineNo`: it's a reserved word.
     - `ItemId` = the item, `FromLocationId` = SUP, `ToLocationId` = the branch.
     - `Quantity` = qty, `TotalCost` = `CostingPolicy.Round4(qty * cost)`, `ReversesMovementId IS NULL`.
   - The branch's `inventory.StockBalances` row has `IsVirtual = 0`, `OnHand` = qty, `StockValue` = that `TotalCost`, `LastUnitCost` = cost and `LastMovementId` = that movement's `Id`.
   - Two literal spot checks act as an independent oracle:
     - RICE-WHT@QC is `120.0000 / 5820.0000 / 48.5000`.
     - VINEGAR-REFILL@MKT is `12.2500 / 426.3000 / 34.8000`.
   - `CANDLE-WHT` has no `StockBalances` rows.
   - INV-2: `SELECT COUNT(*) FROM (SELECT ItemId FROM inventory.StockBalances GROUP BY ItemId HAVING SUM(OnHand) <> 0) x` is 0.
   - `inventory.StockBalances` has 50 rows in total.
3. **(b) API replay (R3).** Sign in as `admin@demo.test`. Post to `PostingTests.Receipts` with key `OpeningStockKey("RICE-WHT", "QC")` and body `new { locationId = qc, reference = "Opening stock", notes = (string?)null, lines = new[] { new { itemId = rice, quantity = 120m, unitCost = 48.5m } } }`.
   - Expect `200`, the header `Idempotent-Replayed: true`, and a JSON `id` equal to that document's `Id`.
   - Then the document count is still 31.
4. **(c) Rename, then seed again (R4).**
   - On `c`, run `UPDATE inventory.Locations SET Code = N'QC-OLD' WHERE Code = N'QC'`. `inventory_app` has `UPDATE` on `Locations`; only the ledger tables are denied. Record the old QC id, and the `OnHand`, `StockValue` and `LastMovementId` of every balance at it, beforehand.
   - Run `SeedCommand.RunAsync` again in a fresh `db.CreateApp(...)`. It must not throw.
   - Then:
     - Documents are still 31, and movements are still 31.
     - A physical location with `Code = 'QC'` exists again, with an id other than the old one (S4's seeder re-created it), and it has no `StockBalances` rows.
     - Every balance at the old id (now `QC-OLD`) is unchanged.
5. **(d) Isolation.** On `LedgerRows.OpenAsync(db, SeedTenants.Isolation, ct)`, `inventory.StockDocuments` and `inventory.StockBalances` both have 0 rows.

### Conventions
- Decimals stay `decimal`. Read them with `reader.GetDecimal`. Compare numbers, not formatted text (IMS-8 Downstream: `1` vs `1.0000`).
- Raw SQL only on the `LedgerRows` connection, always parameterized (`SqlParameter` with an explicit `SqlDbType`, as in `PostingTests.BalanceAsync`).
- `TreatWarningsAsErrors` is on, with nullable enabled. Match the style of `PostingTests.cs`: file-scoped namespace `Inventory.IntegrationTests`, `await using`.

## Do not
- Touch files outside the `files` list. Specifically, don't edit `InventorySeeder`, the fixtures or the helpers. If you need a helper they lack, write it privately in this file. If the seeder behaves differently from the steps above, stop and report it; don't change the seeder.
- Split the scenario into several `[Fact]`s, or use the demo tenant in any other test.
- Add packages, or create or switch branches, commit or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*SeedTests'
dotnet test --project tests/Inventory.IntegrationTests
dotnet test --project tests/Platform.IntegrationTests
```
- `Seed_posts_opening_stock_once_through_the_engine` passes. The whole Inventory integration suite still passes with the demo tenant seeded in its container.
- If Testcontainers crashes at start-up, rerun with `dotnet test --max-parallel-test-modules 1` and say so in the report.
