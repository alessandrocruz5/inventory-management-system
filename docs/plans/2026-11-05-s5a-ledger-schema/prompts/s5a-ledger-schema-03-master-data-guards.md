---
type: prompt
plan: "[[2026-11-05-s5a-ledger-schema]]"
id: 03
unit: IMS-6
status: todo
model: opus
effort: high
depends_on: [01, 02]
parallel_group: C
files:
  create: []
  modify:
    - src/Shared/Kernel/Problems/ProblemTypes.cs
    - src/Modules/Inventory/Inventory/Features/Problems.cs
    - src/Modules/Inventory/Inventory/Features/Items/UpdateItem/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Items/DeactivateItem/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Locations/DeactivateLocation/Handler.cs
    - tests/Inventory.IntegrationTests/ItemsTests.cs
    - tests/Inventory.IntegrationTests/LocationsTests.cs
  delete: []
---
# 03 · Master-data guards and `stock-not-zero`

## Task
Finish the S4 rules that waited for the ledger (TASKS.md, IMS-5 Downstream):
- `PUT /items/{id}` refuses a SKU change once the item has a movement.
- Both deactivate routes refuse while a physical balance has quantity or value, returning `422 stock-not-zero` with the blocking balances.
- Both rules are race-safe: they lock before they check, per spec §6.

Add the slug to Kernel, and test everything, including deterministic lock tests.

## Context
- **Repo and branch:** the repo root is `/home/apa/Code/portfolio/inventory-management-system`, and you are on branch `IMS-6`. Don't switch it.
- **Stack:** .NET 10, EF Core 10, SQL Server 2025 with **RCSI on**, so a plain read never waits for a writer.
- **Prerequisite:** run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` first.
- **What prompts 01–02 created:**
  - `InventoryDbContext.StockMovements` and `StockBalances`, with the entity `StockBalance { ItemId, LocationId, IsVirtual, AllowNegative, OnHand, StockValue, … }`.
  - Indexes: the clustered PK `(TenantId, ItemId, LocationId)` on balances, `IX_Balances_Location (TenantId, LocationId)` and `IX_Movements_Item (TenantId, ItemId, Id)`.
  - `inventory_app` is **denied `UPDATE`** on `StockMovements`, so don't put `UPDLOCK` on that table. It may `UPDATE` balances.
  - The test helper `tests/Inventory.IntegrationTests/LedgerRows.cs`: `OpenAsync(db, tenant, ct)`, `LocationIdAsync`, `AddLocationAsync(c, code, ct, allowNegative)`, `AddDocumentAsync`, `AddMovementAsync`, `AddBalanceAsync` and `WaitUntilBlockedAsync(db, holder, ct, tx)`. Each takes an optional `SqlTransaction tx`.
- **The retrying execution strategy** (TASKS.md IMS-3: `EnrichSqlServerDbContext` turns it on). `BeginTransactionAsync` outside `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` throws `InvalidOperationException`. These are the repo's first user transactions, so the pattern below is the one S5c will copy.

**Spec rules:**
- §5 `:214`: the SKU is immutable once the item has a movement.
- §6 `:324`: deactivation requires zero on-hand and zero value in every physical location.
- §6 `:326`: deactivation first locks the affected balance rows `WITH (UPDLOCK, HOLDLOCK)` (through `IX_Balances_Location` for a location), then checks them, then updates the row. A posting that started earlier finishes first, and one that starts later sees the change at step 7.
- §9 `:589`: `stock-not-zero` is 422, with the balances that block deactivation.
- Keep the S4 behaviour: a racing deactivate that lost on the rowversion reloads and returns `200` if the row is already inactive (`DeactivateItem/Handler.cs:32-40`).

**1. `src/Shared/Kernel/Problems/ProblemTypes.cs`.** After `ConcurrencyConflict` (line 19), add:
```csharp
/// <summary>Deactivation is blocked: a physical balance still has quantity or value (spec §6).</summary>
public const string StockNotZero = "stock-not-zero";
```

**2. `src/Modules/Inventory/Inventory/Features/Problems.cs`.** Add `using Inventory.Persistence;`. After `ConcurrencyConflict` (line 20), add:
```csharp
public static ProblemHttpResult StockNotZero(IEnumerable<StockBalance> balances) => TypedResults.Problem(
    statusCode: StatusCodes.Status422UnprocessableEntity,
    title: "Stock is not zero",
    type: ProblemTypes.Uri(ProblemTypes.StockNotZero),
    extensions: new Dictionary<string, object?>
    {
        ["balances"] = balances.Select(b => new BlockingBalance(b.ItemId, b.LocationId, b.OnHand, b.StockValue)).ToList(),
    });
```
At the end of the file, add:
```csharp
/// <summary>A balance that blocks deactivation, as the <c>stock-not-zero</c> problem lists it.</summary>
internal sealed record BlockingBalance(Guid ItemId, Guid LocationId, decimal OnHand, decimal StockValue);
```

**3. `Features/Items/DeactivateItem/Handler.cs`.** Rewrite `HandleAsync` to keep its signature and result type, and run it in the strategy:
```csharp
public Task<Results<Ok<ItemResponse>, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
    // The lock below lasts until commit, so this runs in a transaction inside the retrying strategy (TASKS.md, IMS-3).
    db.Database.CreateExecutionStrategy().ExecuteAsync(ct => DeactivateAsync(id, ct), cancellationToken);

private async Task<Results<Ok<ItemResponse>, ProblemHttpResult>> DeactivateAsync(Guid id, CancellationToken cancellationToken)
{
    // A retry starts clean: a row tracked by the failed attempt would hide the fresh read.
    db.ChangeTracker.Clear();
    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

    var item = await db.Items.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
    if (item is null)
    {
        return Problems.NotFound();
    }

    // Already inactive: nothing to change, so the call is idempotent.
    if (!item.IsActive)
    {
        return TypedResults.Ok(ItemResponse.From(item));
    }

    // Spec §6: lock the item's balances, then check them, then update the item. HOLDLOCK also locks the empty
    // key range, so a posting can't add a balance row for this item before we commit.
    var blocking = await db.StockBalances
        .FromSql($"SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, HOLDLOCK) WHERE [ItemId] = {id}")
        .AsNoTracking()
        .Where(b => !b.IsVirtual && (b.OnHand != 0 || b.StockValue != 0))
        .OrderBy(b => b.LocationId)
        .ToListAsync(cancellationToken);
    if (blocking.Count > 0)
    {
        return Problems.StockNotZero(blocking);
    }

    item.IsActive = false;
    item.UpdatedAt = clock.GetUtcNow();
    item.UpdatedBy = user.UserId;
    try
    {
        await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        // No version is sent, so a racing deactivate that won first still counts as success.
        await db.Entry(item).ReloadAsync(cancellationToken);
        if (item.IsActive)
        {
            return Problems.ConcurrencyConflict();
        }
    }

    await transaction.CommitAsync(cancellationToken);
    return TypedResults.Ok(ItemResponse.From(item));
}
```
- The item's balances seek the clustered PK prefix `(TenantId, ItemId)`. The EF `Tenant` filter and RLS supply `TenantId`; never write `TenantId` in Features (a review grep enforces this).
- Virtual balances never block. The Supplier balance ends at minus everything received.
- If the `Results<…>` union doesn't infer across the `return`s, add explicit casts. Don't change the endpoint or the result type.

**4. `Features/Locations/DeactivateLocation/Handler.cs`.**
- Apply the same shape. Keep the system-location `400` (lines 21-24) *before* the lock, after the 404.
- The locked read goes through the index the spec names:
  ```csharp
  .FromSql($"SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, HOLDLOCK, INDEX([IX_Balances_Location])) WHERE [LocationId] = {id}")
  ```
  Filter with `b.OnHand != 0 || b.StockValue != 0` and `OrderBy(b => b.ItemId)`.
- The `INDEX` hint keeps the range lock to this location's slice. Without it, the optimizer can scan the tenant's clustered range and block every posting in the tenant.
- Keep the reload-on-conflict path and the existing result type `Results<Ok<LocationResponse>, ValidationProblem, ProblemHttpResult>`.

**5. `Features/Items/UpdateItem/Handler.cs`.**
- Keep the field validation and `version` check (lines 20-30) outside the strategy. Move the rest into `UpdateAsync(Guid id, string sku, string name, string baseUnit, byte[] version, CancellationToken)`, run through `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` with `ChangeTracker.Clear()` and `BeginTransactionAsync`, as in step 3.
- Inside, in order:
  1. Load the item (404).
  2. Run the existing duplicate-SKU pre-check (lines 39-43).
  3. Add the new check:
     ```csharp
     // Spec §5: the SKU is immutable once the item has a movement. HOLDLOCK range-locks the item's slice of
     // IX_Movements_Item until commit, so a first posting can't land between this check and the update.
     // No UPDLOCK: inventory_app is denied UPDATE on StockMovements, and a shared range lock already blocks inserts.
     if (!string.Equals(item.Sku, sku, StringComparison.Ordinal)
         && await db.StockMovements
             .FromSql($"SELECT * FROM [inventory].[StockMovements] WITH (HOLDLOCK, INDEX([IX_Movements_Item])) WHERE [ItemId] = {id}")
             .AnyAsync(cancellationToken))
     {
         errors["sku"] = SkuInUse;
         return Problems.Validation(errors);
     }
     ```
     with `private static readonly string[] SkuInUse = ["The SKU can't change once the item has stock movements."];` beside `DuplicateSku`.
  4. Set the original `RowVersion` and the fields.
  5. `SaveChangesAsync`, keeping both existing `catch`es (409, and the `400` for a unique violation).
  6. `CommitAsync`, then return `Ok`.
- Remove the `// … lands with the ledger tables (S5a).` comment (line 38).
- An unchanged SKU takes no lock, so a name or unit edit after a movement still works.
- Remove the matching `S5a` placeholder comments in both deactivate handlers.

**6. `tests/Inventory.IntegrationTests/ItemsTests.cs`.** Add the tests below.
- Use the existing helpers: `CreateAsync(client, sku, name, unit, ct)` (`:405`), `GetJsonAsync`, `AssertValidationAsync` and `DatabaseFixture.CreateTenantWithUserAsync`/`SignInAsync`.
- Add ledger rows with `LedgerRows` on `await LedgerRows.OpenAsync(db, tenant, ct)`. Use `LocationIdAsync(c, "SUP", ct)` for the Supplier and `AddLocationAsync` for a physical location.
- Check every 422 body: `type == "/problems/stock-not-zero"`, `status == 422`, and a `balances` array of `{ itemId, locationId, onHand, stockValue }` in camelCase.

Tests:
- **`Update_rejects_a_sku_change_once_the_item_has_a_movement`:**
  - Setup: a receipt and a movement SUP → physical (10 / 1000).
  - A PUT with a new SKU (and a valid `version`) → `400` with `errors.sku`.
  - `GET` still shows the old SKU.
- **`Update_allows_name_edits_after_a_movement`:** the same setup, then a PUT with the same SKU and a new name → `200` with the new name.
- **`Sku_change_waits_for_an_uncommitted_first_movement`:**
  1. Open `holder = LedgerRows.OpenAsync(…)` and `tx = holder.BeginTransaction()`, then insert a document and a movement in `tx` (uncommitted).
  2. Start the PUT with a new SKU **without awaiting**.
  3. `await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx)`, then `tx.Commit()`.
  4. The PUT → `400 errors.sku`.

  Without the `HOLDLOCK`, RCSI lets the PUT finish with `200` and the wait times out.
- **`Deactivate_is_422_while_a_physical_balance_is_not_zero`:**
  - Balances: physical 5 / 500.0000, SUP −5 / 0 (virtual).
  - → `422`. `balances` has exactly one entry: the physical location, `onHand` 5, `stockValue` 500.
  - A `GET` shows the item still `isActive: true`.
- **`Deactivate_ignores_virtual_and_zero_physical_balances`:** balances physical 0 / 0 and SUP −5 / 0 → `200`, inactive.
- **`Deactivate_is_422_for_a_negative_physical_balance`:** a physical location with `allowNegative: true`, balance −2 / 0 with `AllowNegative = true` → `422`.
- **`Deactivate_waits_for_an_uncommitted_balance`:** the same pattern as the SKU lock test. The holder inserts a physical balance 5 / 500 in `tx`, the deactivate is started, the test waits until it's blocked, then commits → `422`.
- Keep `Concurrent_deactivations_all_succeed` (`:358-375`) and every other test unchanged. They must still pass: 8 parallel deactivates serialize on the locks, then the losers reload and get `200`.

**7. `tests/Inventory.IntegrationTests/LocationsTests.cs`.** Use `CreateAsync(client, code, name, ct)` (`:307`) for the physical location, and `LedgerRows.AddItemAsync` for the items.
- **`Deactivate_is_422_while_a_balance_is_not_zero`:** two items at the location, balances 3 / 30 and 0 / 0 → `422`, and `balances` lists only the first. The location stays active.
- **`Deactivate_succeeds_when_every_balance_is_zero`:** balances 0 / 0 → `200`.
- **`Deactivate_waits_for_an_uncommitted_balance`:** the lock pattern above, then → `422`.
- Keep `Concurrent_deactivations_all_succeed` (`:250-266`) unchanged.

**Conventions:**
- Every type is `internal`. Handlers never mention `TenantId`.
- Money and quantities are `decimal`. Assert JSON numbers with `GetValue<decimal>()`.
- No bare `IgnoreQueryFilters()`. Raw SQL goes only through EF (`FromSql`), so the session-context interceptor and RLS apply (TASKS.md IMS-4).

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change routes, endpoint files or result types, or add a version to deactivate.
- Put `UPDLOCK` or `XLOCK` on `StockMovements` or `StockDocuments`.
- Write to the ledger tables from a handler.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
! grep -rn --include='*.cs' 'TenantId' src/Modules/Inventory/Inventory/Features
! grep -rn --include='*.cs' 'S5a' src/Modules/Inventory/Inventory/Features
```
- The 7 new `ItemsTests` and 3 new `LocationsTests` pass. Both `Concurrent_deactivations_all_succeed` tests and the T5 probes in `TenantIsolationTests` still pass.
- Each lock test fails if its lock hint is removed. Check this once by deleting `HOLDLOCK` locally, running the test and restoring it; don't commit that.
