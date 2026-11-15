---
type: prompt
plan: "[[2026-11-15-s11-ledger-health]]"
id: 01
unit: IMS-15
status: todo
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Persistence/LedgerLock.cs
    - src/Modules/Inventory/Inventory/Persistence/LedgerProjection.cs
    - tests/Inventory.IntegrationTests/LedgerProjectionTests.cs
  modify:
    - src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs
    - tests/Inventory.IntegrationTests/LedgerInvariants.cs
  delete: []
---
# 01 · Ledger lock and projection

## Task
Add two internal Persistence building blocks that S11's health and rebuild share. Write no endpoints yet.
1. `LedgerLock`: the tenant's `ledger:<TenantId>` applock in `Shared` or `Exclusive` mode. `PostingEngine` uses it with no change in behaviour.
2. `LedgerProjection`: one SQL statement that returns, for every `(item, location)` key of the tenant, the stored balance next to its value derived from the ledger. `LastUnitCost` is finished in C# with `CostingPolicy`.

Then prove the projection with integration tests.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-15`. Stop if `git branch --show-current` says otherwise. .NET 10, `Nullable` on, `TreatWarningsAsErrors`; module types are `internal`. Decimals are `decimal`, and rounding is `CostingPolicy.Round4` (`AwayFromZero`, 4 dp). Raw SQL runs only through EF (`db.Database.SqlQuery…`/`ExecuteSql…`), never on a hand-opened connection. `TenantId` comes from `ITenantContext`, never a request.
- Spec §6 `:343`: *"Rebuild … recomputes `OnHand`, `StockValue` and `LastMovementId` for every balance … as a `GROUP BY` over `StockMovements` … `LastUnitCost` comes from the same windowed query that checks INV-5 … Health takes no lock: each invariant is a single statement, so snapshot reads give it a consistent view."* §7 `:383-390`: *"LastUnitCost is a function of the ledger: the value set by the latest inflow into the balance, ignoring reversed movements and reversals, with OnHand and StockValue as running sums after it."*
- Locked downstream: IMS-7 *"INV-5's windowed SQL must return what `CostingPolicy.LastUnitCost` returns."* IMS-14 *"rebuild must also reset empty `(item, ADJ)` balance rows (0 / 0 / null `LastMovementId`); INV-5 must include revaluation movements (qty 0, same document as the inflow)."*

### 1. `Persistence/LedgerLock.cs` (plan D5, D6)
- Read `Persistence/PostingEngine.cs:415-427` (`TakeLedgerLockAsync`) first. Move its body into:
  ```csharp
  internal enum LedgerLockMode { Shared, Exclusive }
  internal static class LedgerLock
  {
      public static async Task AcquireAsync(DatabaseFacade database, Guid tenantId, LedgerLockMode mode, CancellationToken ct)
  }
  ```
  Keep the same SQL (`sp_getapplock`, `@LockOwner = 'Transaction'`), the same `NVarChar(255)` parameter with value `$"ledger:{tenantId:D}"`, `@LockMode` from `mode` (`'Shared'`/`'Exclusive'`, sent as a parameter or a fixed literal per enum value, never string-built from input), and the same `InvalidOperationException` when the result is < 0.
- Doc comment: spec §6 step 3. Postings take it `Shared` and the rebuild `Exclusive`. It must be the first statement of a transaction (owner `Transaction`). Because every caller holds no other lock when it asks for this one, it can't be in a deadlock cycle, so `-3` (deadlock victim) is unreachable and isn't retried (plan D6, IMS-8 nit).
- `PostingEngine.TakeLedgerLockAsync` becomes `=> LedgerLock.AcquireAsync(db.Database, tenant.TenantId, LedgerLockMode.Shared, ct);`. Keep its name, keep its comment line "Step 3 …", and leave both call sites alone. Change nothing else in `PostingEngine`.

### 2. `Persistence/LedgerProjection.cs` (plan D2, D8)
```csharp
internal sealed record BalanceValues(decimal OnHand, decimal StockValue, decimal? LastUnitCost, long? LastMovementId);
internal sealed record ProjectedBalance(
    Guid ItemId, string Sku, Guid LocationId, string LocationCode, bool IsVirtual, bool AllowNegativeStock,
    BalanceValues? Stored,      // null: no StockBalances row for this key
    BalanceValues Ledger)
{
    public bool Differs => Stored != Ledger;
}
internal static class LedgerProjection
{
    public static async Task<List<ProjectedBalance>> ReadAsync(InventoryDbContext db, Guid tenantId, CancellationToken ct)
}
```
- **One statement** via `db.Database.SqlQuery<Row>($"…")` (interpolated, so `tenantId` is a parameter) and `.ToListAsync(ct)` with **no LINQ composed on top**, so EF sends it as written and a CTE is allowed. If EF still wraps it in a derived table, rewrite with derived tables and `OUTER APPLY`. Put `TenantId = {tenantId}` on **every** table reference (the S8/S10 convention; RLS also filters). Read `Features/Items/ListItemMovements/Handler.cs:32-60` for the In/Out-legs pattern and a private `Row` class with `init` properties.
- Keys: the full outer union of (a) every `StockBalances` row and (b) every `(ItemId, ToLocationId)` and `(ItemId, FromLocationId)` in `StockMovements`. Join `Items` (Sku) and `Locations` (`Code`, `Kind`, `AllowNegativeStock`). `IsVirtual` = `Locations.Kind <> 'Physical'` (the location, not the row's mirror).
- Stored: the row's `OnHand`, `StockValue`, `LastUnitCost`, `LastMovementId`, plus a `HasRow` flag.
- Ledger sums per key, over legs (an In leg `+Quantity, +TotalCost` on `ToLocationId` and an Out leg `−Quantity, −TotalCost` on `FromLocationId`): `LedgerOnHand = ISNULL(SUM(qty), 0)`, `LedgerValue = ISNULL(SUM(cost), 0)`, `LedgerLastMovementId = MAX(Id)` (null with no movements).
- LUC inputs, physical keys only. These must match `Domain/CostingPolicy.cs:85-137` rule for rule, so read it first:
  - *inflow* = the key's leg with the highest `Id` where it's an In leg, `Quantity > 0` (so a qty-0 revaluation is never the inflow), `ReversesMovementId IS NULL`, and no movement of the tenant has `ReversesMovementId = this Id`.
  - *end* = `MAX(Id)` over the key's legs whose `DocumentId` = the inflow's `DocumentId`. That includes the inflow's revaluation (`LineNo` 1000 + n, same document, qty 0, either direction).
  - Return `InflowQuantity`, `InflowCost` (the inflow's `TotalCost`), `OnHandAtInflow = SUM(qty)` and `ValueAtInflow = SUM(cost)` over the key's legs with `Id <= end`. All nullable when there's no inflow.
  - **Do not divide in SQL** (plan D2): T-SQL `decimal` division cuts the scale to 6, so it can't be proven equal to C#.
- In C#, build `Ledger`:
  - Virtual: `new(LedgerOnHand, 0m, null, LedgerLastMovementId)`.
  - Physical: `new(LedgerOnHand, LedgerValue, inflow is null ? null : CostingPolicy.LastUnitCostAfterInflow(OnHandAtInflow, ValueAtInflow, InflowQuantity, InflowCost), LedgerLastMovementId)`.
  - `Stored` = `HasRow ? new(...) : null`.
- Sort the result in C# by `new SqlGuid(ItemId)`, then `new SqlGuid(LocationId)` (clustered key order; the rebuild writes in this order).
- Doc comment: plan D2. SQL sums exactly; the one division is `CostingPolicy`'s, so INV-5 equals `CostingPolicy.LastUnitCost` by construction. A row with no movements projects to 0 / 0 / null / null (IMS-14). No lock: under RCSI the single statement is one snapshot.

### 3. `tests/Inventory.IntegrationTests/LedgerInvariants.cs`
Change only `private static async Task<List<LedgerMovement>> MovementsAsync` to `internal static`. Its body stays as is.

### 4. `tests/Inventory.IntegrationTests/LedgerProjectionTests.cs` (new, `public sealed class LedgerProjectionTests(DatabaseFixture db)`)
Each test creates its own tenant (spec §11). Helpers to reuse, not copy:
- `DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin)` → `(TenantId, Email)`; `DatabaseFixture.SignInAsync(app, email)`; `db.CreateApp()`.
- `PostingTests.{Receipts, Issues, PostDocumentAsync, Receipt, Line, Issue, IssueLine, NewKey, NewItemAsync, NewLocationAsync, ReceiveAsync}`.
- `TransferTests.{Transfers, Transfer, TransferLine}`; `AdjustmentTests.{Adjustments, Adjustment, Delta}`; `ReversalTests.ReverseAsync(client, documentId, key, ct)`.
- `LocationsTests.AllowNegativeAsync(client, id, true, ct)`.
- `LedgerRows.OpenAsync(db, tenant, ct)` (an `inventory_app` connection with the tenant's session context), `LedgerInvariants.MovementsAsync`.
- To call `LedgerProjection.ReadAsync`, copy the scope pattern in `TenantInterceptorTests.cs:20-40` (`ITenantScopeFactory.BeginScope(tenant)` → `scope.ServiceProvider.GetRequiredService<InventoryDbContext>()`).

Add one private helper, `AssertProjectionMatchesAsync(tenant, connection, ct)`:
- Read the projection and the movements.
- Assert every row has `Stored == Ledger`.
- For every physical row, assert `Ledger.LastUnitCost == CostingPolicy.LastUnitCost(new BalanceKey(item, location), movements touching it)`. Filter to the item's movements whose `From` or `To` is the location, as `LedgerInvariants.cs:35-37` does.
- Assert the projection's row count equals `SELECT COUNT(*) FROM inventory.StockBalances`.

Tests:
- `Projection_matches_stored_balances_and_CostingPolicy`: one tenant, physical locations A and B (toggle off) and K (toggle on), and items P, Q, R.
  - Item P: post spec §7 set V steps V1–V7 at A/B through the API (V7 reverses V6, so B's LUC falls back to the V5 transfer-in).
  - Item Q: set N steps N1–N9 at K (revaluations both settle out to ADJ).
  - Item R: set X steps X1–X3 at K (revaluation in from ADJ). Then an adjustment `+3` with no unit cost at A after a receipt, and a midpoint inflow at B (receive 2 @ 10.0001 then 2 @ 10.0000 → value 40.0002 / 4 = 10.00005 → `LastUnitCost` **10.0001**, which banker's rounding would make 10.0000).
  - Then call `AssertProjectionMatchesAsync`. Also assert that every `(item, ADJ)` row with no movements projects to `Stored == Ledger == (0, 0, null, null)`.
- `Projection_reports_a_drifted_balance`: receive 5 @ 10 at A. As `inventory_app` (`LedgerRows` connection), `UPDATE inventory.StockBalances SET OnHand = 6, StockValue = 61, LastUnitCost = 1.2345 WHERE TenantId = @t AND ItemId = @i AND LocationId = @a` (it satisfies the CHECKs). Then the row has `Differs`, `Stored` = (6, 61, 1.2345, id) and `Ledger` = (5, 50, 10.0000, id).
- `Projection_reports_a_missing_balance_row`: receive at A. Then, as the owner, open `new SqlConnection(db.AdminConnectionString)` with `Pooling = false` and set the tenant's session context exactly as `LedgerRows.OpenAsync` does (RLS applies to the owner too). `DELETE` the item's `SUP` balance row. Then the projection has that key with `Stored == null` and `Ledger.OnHand` = −5.
- Compare decimals by value (`ShouldBe(10.0001m)`), never as text (IMS-8).

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Change anything in `PostingEngine` beyond the `TakeLedgerLockAsync` body, or change spec §6, migrations, `StockMovements` or `StockDocuments`.
- Divide or `ROUND` in the projection SQL; use `IgnoreQueryFilters()`; open a connection outside EF in `src/`.
- Add dependencies, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerProjectionTests' --filter-class '*PostingTests' --filter-class '*PostingConcurrencyTests' --filter-class '*ReversalTests' --filter-class '*NegativeStockTests' --filter-class '*SeedTests'
dotnet test --project tests/Architecture.Tests
! grep -q 'ledger:' src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs   # the resource text lives only in LedgerLock
```
- Every posting, reversal, concurrency and seed test is unchanged and green, which proves the lock refactor changed no behaviour.
