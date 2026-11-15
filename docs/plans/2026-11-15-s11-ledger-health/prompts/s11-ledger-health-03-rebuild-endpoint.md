---
type: prompt
plan: "[[2026-11-15-s11-ledger-health]]"
id: 03
unit: IMS-15
status: todo
model: opus
effort: high
depends_on: [02]
parallel_group: C
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Ledger/LedgerRebuildResponse.cs
    - src/Modules/Inventory/Inventory/Features/Ledger/RebuildLedger/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Ledger/RebuildLedger/Handler.cs
    - tests/Inventory.IntegrationTests/LedgerRebuildTests.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Ledger/LedgerEndpoints.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 03 · Rebuild endpoint: `POST /api/ledger/rebuild`

## Task
Add the Admin-only `POST /api/ledger/rebuild`. It takes the tenant's ledger lock in `Exclusive` mode, compares every balance with the ledger through `LedgerProjection`, returns the differences, and overwrites them in the same transaction. A healthy ledger gives an empty diff and no writes. Register it, give it a T5 probe, and test it.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-15`. Stop if `git branch --show-current` says otherwise. .NET 10, `Nullable` on, `TreatWarningsAsErrors`; module types are `internal`. Raw SQL only through EF. `TenantId` comes from `ITenantContext`. **No UPDATE or DELETE on `StockDocuments`/`StockMovements`**: the rebuild writes only `StockBalances`.
- Spec §6 `:343`: *"`POST /api/ledger/rebuild` takes the tenant's ledger lock in `Exclusive` mode, so no posting runs while it reads and writes. It recomputes `OnHand`, `StockValue` and `LastMovementId` for every balance of the current tenant as a `GROUP BY` over `StockMovements` … `LastUnitCost` comes from the same windowed query that checks INV-5. It returns the differences it found, then overwrites them in the same transaction."* §9 `:550`: Admin, *"Rebuild and return the diff"*. IMS-14: *"rebuild must also reset empty `(item, ADJ)` balance rows (0 / 0 / null `LastMovementId`)"*.
- Available: `Persistence/LedgerLock.AcquireAsync(db.Database, tenantId, LedgerLockMode.Exclusive, ct)` and `Persistence/LedgerProjection.ReadAsync(db, tenantId, ct)` (prompt 01). `Features/Ledger/LedgerEndpoints.cs` and the health endpoint (prompt 02). Read all of them first.
- Pattern for a locking write: `Features/Locations/UpdateLocation/Handler.cs:32-92` (`CreateExecutionStrategy().ExecuteAsync(...)`, `db.ChangeTracker.Clear()` per attempt, `BeginTransactionAsync`, raw SQL with an explicit `[TenantId] = {tenant.TenantId}`, and `CommitAsync`). Exhausted retries: `Persistence/PostingEngine.cs:97-104` catches `RetryLimitExceededException`. Here, map it to `Problems.ConcurrencyConflict()` (`Features/Problems.cs:21-22`). The step-5 insert to copy for a missing row is `PostingEngine.cs:158-164`.

### 1. Response (`Features/Ledger/LedgerRebuildResponse.cs`), exactly the plan's contract
```csharp
internal sealed record LedgerRebuildResponse(DateTimeOffset RebuiltAt, int BalancesChecked, IReadOnlyList<BalanceDifference> Differences);
internal sealed record BalanceDifference(
    Guid ItemId, string Sku, Guid LocationId, string LocationCode, bool Inserted, BalanceSnapshot? Before, BalanceSnapshot After);
internal sealed record BalanceSnapshot(decimal OnHand, decimal StockValue, decimal? LastUnitCost, long? LastMovementId);
```
`BalancesChecked` = the number of projection rows. Order `Differences` by `Sku`, then `LocationCode` (`StringComparer.Ordinal`).

### 2. Handler (`RebuildLedgerHandler(InventoryDbContext db, ITenantContext tenant, ICurrentUser user, TimeProvider clock, ILogger<RebuildLedgerHandler> logger)`) (plan D4)
- `HandleAsync(CancellationToken ct)` returns `Results<Ok<LedgerRebuildResponse>, ProblemHttpResult>`. It wraps the attempt in `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` and catches `RetryLimitExceededException` → `Problems.ConcurrencyConflict()`.
- One attempt:
  1. `db.ChangeTracker.Clear();` then `await using var tx = await db.Database.BeginTransactionAsync(ct);`.
  2. `await LedgerLock.AcquireAsync(db.Database, tenant.TenantId, LedgerLockMode.Exclusive, ct);` must be the **first statement** (LedgerLock's doc: no other lock held, so no deadlock cycle).
  3. `var rows = await LedgerProjection.ReadAsync(db, tenant.TenantId, ct);` then `var drifted = rows.Where(r => r.Differs).ToList();` (already in clustered key order).
  4. For each drifted row, in that order:
     - `Stored is null` → `INSERT INTO [inventory].[StockBalances] ([ItemId],[LocationId],[IsVirtual],[AllowNegative],[OnHand],[StockValue],[LastUnitCost],[LastMovementId]) SELECT {item}, l.[Id], IIF(l.[Kind] <> 'Physical', 1, 0), l.[AllowNegativeStock], {onHand}, {value}, {luc}, {lastId} FROM [inventory].[Locations] AS l WHERE l.[TenantId] = {t} AND l.[Id] = {location}`. `TenantId` comes from the column default, as at step 5.
     - Otherwise `UPDATE [inventory].[StockBalances] SET [OnHand] = {..}, [StockValue] = {..}, [LastUnitCost] = {..}, [LastMovementId] = {..} WHERE [TenantId] = {t} AND [ItemId] = {i} AND [LocationId] = {l}`, through `db.Database.ExecuteSqlAsync($"…")` (parameterized). It writes those four columns only, with no rowversion check (plan D4: the applock excludes postings, and a toggle writes only `AllowNegative`). Assert the affected row count is 1 and throw `InvalidOperationException` otherwise.
     - Comment: no set-based `UPDATE … FROM` over the tenant. Key-targeted writes lock only drifted rows, in clustered order.
  5. `await tx.CommitAsync(ct);`. With no drift, nothing is written, and the commit just releases the lock.
  6. If `drifted.Count > 0`: `logger.LogWarning("Ledger rebuild for tenant {TenantId} by user {UserId} fixed {Count} balances", …)`. Log no values or bodies (§8). Read `ICurrentUser`'s member name in `src/Api/Tenancy/CurrentUser.cs` and Kernel first.
  7. Return `Ok(new LedgerRebuildResponse(clock.GetUtcNow(), rows.Count, differences))`, with `Before = Stored` mapped (null when inserted) and `After = Ledger`.
- Doc comment: spec §6 Rebuild; plan D4. A `547` here means a balance row was corrupted by hand into a state the ledger can't restore under the current toggle (plan Assumption); it rolls back.

### 3. Endpoint and wiring
- `RebuildLedger/Endpoint.cs`: `MapPost("/rebuild", (RebuildLedgerHandler handler, CancellationToken ct) => handler.HandleAsync(ct)).RequireAuthorization(InventoryPolicies.Admin);`. No body is bound and no `Idempotency-Key` is used (plan Assumption: not a document; idempotent by nature).
- `LedgerEndpoints`: add `ledger.MapRebuildLedger();`. `InventoryModuleExtensions`: `AddScoped<RebuildLedgerHandler>()`.

### 4. T5 (`TenantIsolationTests.cs`, plan D10)
Add `[Fact][ProbesRoute("POST /api/ledger/rebuild")] Each_tenant_rebuilds_only_its_own_ledger`:
- Two tenants with stock; drift B's physical balance with a raw `UPDATE` on B's `LedgerRows` connection.
- A's rebuild → `200`, `differences` empty, and B's row is still drifted (read on B's connection).
- B's rebuild → one difference on B's key, and the row is restored.
- B's health is healthy, and A's balances' `RowVersion` values are unchanged across both rebuilds.

### 5. `tests/Inventory.IntegrationTests/LedgerRebuildTests.cs` (new; own tenant per test; helpers as in prompts 01–02)
- `Healthy_ledger_rebuild_returns_an_empty_diff_and_writes_nothing`: a mixed ledger. Read every balance's `RowVersion` (as `binary(8)`) → rebuild → `200`, `differences` empty, `balancesChecked` = `COUNT(*)` of balances, and every `RowVersion` unchanged.
- `Rebuild_fixes_a_drifted_physical_balance`: drift `OnHand`, `StockValue`, `LastUnitCost` and `LastMovementId` → rebuild returns one difference with `inserted: false` and exact `before`/`after`. The row then equals the ledger, health is healthy, and a second rebuild has an empty diff.
- `Rebuild_resets_an_empty_ADJ_row`: after a receipt (step 5 creates an empty `(item, ADJ)` row), `UPDATE` that row to `OnHand = 5` (virtual, value 0) → the difference shows after (0, 0, null, null), and the row is reset.
- `Rebuild_inserts_a_missing_balance_row`: as the owner (`db.AdminConnectionString`, `Pooling = false`, the tenant's session context set as in `LedgerRows.OpenAsync`), `DELETE` the item's `SUP` row → rebuild returns `inserted: true`, `before: null`, `after.onHand` = −received. The row exists again with `IsVirtual` 1 and `AllowNegative` 0.
- `Rebuild_waits_for_a_posting_that_holds_the_ledger_lock`: on a `LedgerRows.OpenAsync` connection, begin a transaction and run `EXEC sp_getapplock @Resource = N'ledger:<tenant:D>', @LockMode = 'Shared', @LockOwner = 'Transaction'`. Start the rebuild request without awaiting it, then `LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx)` proves it's blocked. Commit the holder → the rebuild completes `200`.
- `Posting_waits_while_the_ledger_lock_is_exclusive`: hold the lock `Exclusive` the same way → start a receipt POST → `WaitUntilBlockedAsync` → commit → the receipt returns `201`. This proves the engine and `LedgerLock` agree on the resource name. Before writing it, grep `PostingConcurrencyTests.cs` for `sp_getapplock`; if an equivalent test exists, skip this one and say so.
- `Staff_gets_403`: Staff `POST /api/ledger/rebuild` → `403`, and nothing changed.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Write `IsVirtual`, `AllowNegative`, `StockDocuments` or `StockMovements`; write a set-based `UPDATE`; take any lock before the applock.
- Add a ProblemDetails slug, a migration, a column, or an `Idempotency-Key` requirement.
- Add dependencies, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerRebuildTests' --filter-class '*LedgerHealthTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingConcurrencyTests' --filter-class '*PostingClockTests'
dotnet test --project tests/Architecture.Tests
```
- `Every_api_route_is_probed_or_exempt` passes with both ledger routes probed.
