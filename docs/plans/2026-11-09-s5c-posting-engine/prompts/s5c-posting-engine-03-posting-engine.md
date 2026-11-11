---
type: prompt
plan: "[[2026-11-09-s5c-posting-engine]]"
id: 03
unit: IMS-8
status: done
model: opus
effort: xhigh
depends_on: [01]
parallel_group: B
files:
  create:
    - src/Modules/Inventory/Inventory/Persistence/PostingCommand.cs
    - src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs
  modify:
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - src/Shared/Kernel/Problems/ProblemTypes.cs
    - src/Modules/Inventory/Inventory/Features/Problems.cs
  delete: []
---
# 03 · PostingEngine, slugs, retry config

## Task
Write `Persistence/PostingEngine`, the transaction runner for spec §6 steps 2–10, together with its input and outcome types. Then:
- Set the `InventoryDbContext` retry count to 3.
- Register the engine.
- Add the five slugs to Kernel, and their factories to `Features/Problems.cs`.

No endpoint calls the engine yet; prompt 04 does that. This is the ledger's write path, so read before you write and follow the steps exactly.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-8`. Don't switch it.
- **Prerequisite:** run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` first.
- **Stack:** .NET 10, EF Core 10, SQL Server 2025 with **RCSI on**. The API connects as `inventory_app`. Its rights on `inventory` are `SELECT, INSERT, UPDATE` at schema level, `DENY UPDATE, DELETE` on `StockDocuments` and `StockMovements`, and `UPDATE` on the sequence `inventory.MovementIds` (`src/Api/Setup/DatabasePrincipals.cs:26-34`). Its RLS session context is the tenant, set by `TenantSessionInterceptor` on every connection EF opens.

**Read these first:**
- `src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs:78-144`: the mappings.
  - Documents: `Id` is `ValueGeneratedOnAdd`, and the triggers are declared.
  - Movements: `Id` is `ValueGeneratedNever`.
  - Balances: PK `(TenantId, ItemId, LocationId)`, and `RowVersion` is a concurrency token.
- `Persistence/StockDocument.cs`, `StockMovement.cs`, `StockBalance.cs`, `Location.cs`, `Item.cs`, `LocationKind.cs`, `StockDocumentType.cs`.
- `src/Modules/Inventory/Inventory/Domain/PostingRules.cs:22-129` `Post(kind, lines, balances)`, and `Domain/Balance.cs`, `Domain/LedgerMovement.cs` (`PlannedMovement`), `Domain/PostingRejections.cs`.
- `Features/Items/DeactivateItem/Handler.cs:13-68`, the pattern to copy:
  - `CreateExecutionStrategy().ExecuteAsync`, then `ChangeTracker.Clear()` at the start of each attempt.
  - `await using var transaction = await db.Database.BeginTransactionAsync(...)`.
  - `FromSql` with lock hints and `FORCESEEK` (RLS supplies the `TenantId` seek).
- `Features/Problems.cs`: the existing factories and `IsUniqueViolation`.

**Locked rules (TASKS.md Downstream):**
- Raw SQL only through EF (`Database.SqlQuery*`, `ExecuteSql*`, `FromSql*`), never a connection you open yourself.
- `NEXT VALUE FOR` composed by EF (`SingleAsync`) fails with 11719: use `ToListAsync()` then `.Single()`.
- Write `[LineNo]` in brackets in raw SQL, because it's a reserved word.
- Lock hints on the ledger tables work despite the deny, but read the ledger with shared locks. Never `UPDLOCK` `StockDocuments` or `StockMovements`.
- Never `UPDATE` or `DELETE` `StockDocuments`/`StockMovements` (review constraint). `IgnoreQueryFilters()` without names is banned.
- `PostedAt` comes only from `TimeProvider`.

### 1. `src/Shared/Kernel/Problems/ProblemTypes.cs`
After `StockNotZero`, add five `public const string` members with `<summary>` docs in the existing style:

| Constant | Slug | Status (spec §9) |
|---|---|---|
| `IdempotencyKeyMissing` | `"idempotency-key-missing"` | 400 |
| `IdempotencyKeyReused` | `"idempotency-key-reused"` | 409 |
| `InsufficientStock` | `"insufficient-stock"` | 422 |
| `InactiveItem` | `"inactive-item"` | 422 |
| `InactiveLocation` | `"inactive-location"` | 422 |

### 2. `src/Modules/Inventory/Inventory/Features/Problems.cs`
Add these factories, following the `StockNotZero` pattern, with camelCase extension keys per spec §9:
- `IdempotencyKeyMissing()`: 400.
- `IdempotencyKeyReused(Guid originalDocumentId)`: 409, extension `originalDocumentId`.
- `InsufficientStock(Guid itemId, Guid locationId, decimal available, decimal requested)`: 422, extensions `itemId`, `locationId`, `available`, `requested`.
- `InactiveItem(Guid itemId)`: 422, extension `itemId`.
- `InactiveLocation(Guid locationId)`: 422, extension `locationId`.

Persistence never references `Inventory.Features`. The engine below checks 2601/2627 inline and has its own column bound.

### 3. `Persistence/PostingCommand.cs` (namespace `Inventory.Persistence`)
```csharp
internal sealed record PostingCommand(
    PostingKind Kind,                    // Inventory.Domain.PostingKind: Receipt or Issue in S5c
    StockDocumentType DocumentType,      // Receipt or Issue
    IReadOnlyList<PostingLine> Lines,    // Inventory.Domain.PostingLine with resolved From/To location ids (LineNo 1..n)
    string IdempotencyKey,
    byte[] RequestHash,
    string? Reference,
    string? Notes,
    Guid PostedBy);

internal abstract record PostingOutcome;
internal sealed record PostedOutcome(StockDocument Document, IReadOnlyList<StockMovement> Movements, IReadOnlyList<StockBalance> Balances, bool Replayed, DateTimeOffset BalancesAsOf) : PostingOutcome;
internal sealed record KeyReusedOutcome(Guid OriginalDocumentId) : PostingOutcome;
internal sealed record InactiveItemOutcome(Guid ItemId) : PostingOutcome;
internal sealed record InactiveLocationOutcome(Guid LocationId) : PostingOutcome;
internal sealed record RejectedOutcome(PostingRejectedException Rejection) : PostingOutcome;     // Domain rejections
internal sealed record BalanceOutOfRangeOutcome(Guid ItemId, Guid LocationId) : PostingOutcome;  // plan D1
internal sealed record RetriesExhaustedOutcome : PostingOutcome;                                  // → 409 concurrency-conflict
```
`PostedOutcome.Balances` holds the **physical** balances of the touched pairs only (plan D3), ordered by `ItemId` then `LocationId`.

### 4. `Persistence/PostingEngine.cs`
`internal sealed class PostingEngine(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)`. Register it as scoped.

**`Task<PostingOutcome?> FindExistingAsync(string key, byte[] hash, CancellationToken ct)`** is step 2 (plan D2: the caller runs it before the database checks).
1. Read `db.StockDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.IdempotencyKey == key)`. If there's no row, return `null`.
2. If `!document.RequestHash.AsSpan().SequenceEqual(hash)`, return `KeyReusedOutcome(document.Id)`.
3. Otherwise return `PostedOutcome(document, movements, balances, Replayed: true, BalancesAsOf: clock.GetUtcNow())`:
   - `movements` is the document's movements (`AsNoTracking`, ordered by `LineNo`).
   - `balances` is the current physical balances for the (ItemId, From/To) pairs of those movements. Join to `Locations` with `Kind == Physical`, or filter `!IsVirtual`.

**`Task<PostingOutcome> PostAsync(PostingCommand command, CancellationToken ct)`** runs steps 3–10.
- Wrap everything in `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`.
- Catch `RetryLimitExceededException` (`Microsoft.EntityFrameworkCore.Storage`) around it and return `RetriesExhaustedOutcome`.
- Inside each attempt:
  1. `db.ChangeTracker.Clear()`. Set `var now = clock.GetUtcNow()` once, and use it for the document and every movement.
  2. `await using var tx = await db.Database.BeginTransactionAsync(ct)`. That's READ COMMITTED with snapshot reads on.
  3. **Step 3, the ledger lock.** Execute `DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Shared', @LockOwner = 'Transaction'; SELECT @r AS [Value];` via `db.Database.SqlQueryRaw<int>(sql, new SqlParameter("@res", SqlDbType.NVarChar, 255) { Value = $"ledger:{tenant.TenantId:D}" }).ToListAsync()`, then `.Single()`. Throw `InvalidOperationException` if the result is `< 0`. You may use `ExecuteSqlRawAsync` with an output parameter instead, if that's cleaner.
  4. **Step 4, the document.** `db.StockDocuments.Add(new StockDocument { Type = command.DocumentType, ReasonCode = null, ReversesDocumentId = null, Reference, Notes, IdempotencyKey, RequestHash, PostedAt = now, PostedBy = command.PostedBy })`, then `await db.SaveChangesAsync(ct)`.
     - Catch `DbUpdateException ex when ex.InnerException is SqlException { Number: 2601 or 2627 }`. That's a parallel duplicate that committed first. Set a local `duplicate = true` and **return from the attempt without committing**. The transaction disposes and rolls back.
     - Every later step depends on this row existing first: it claims the key (spec §6 step 4).
  5. **Step 5, ensure balances.** Build the touched keys: for each line, `(ItemId, FromLocationId)` and `(ItemId, ToLocationId)`. Sort them by `(new SqlGuid(ItemId), new SqlGuid(LocationId))`, which is SQL Server `uniqueidentifier` order (`System.Data.SqlTypes.SqlGuid`). For each key, in that order:
     ```sql
     INSERT INTO [inventory].[StockBalances] ([ItemId], [LocationId], [IsVirtual], [AllowNegative], [OnHand], [StockValue])
     SELECT {item}, l.[Id], IIF(l.[Kind] <> 'Physical', 1, 0), l.[AllowNegativeStock], 0, 0
     FROM [inventory].[Locations] AS l
     WHERE l.[Id] = {location}
       AND NOT EXISTS (SELECT 1 FROM [inventory].[StockBalances] AS b WHERE b.[ItemId] = {item} AND b.[LocationId] = {location});
     ```
     - Run it with `db.Database.ExecuteSqlAsync($"…")`. `TenantId` comes from the column default (session context), and the BLOCK predicate checks it.
     - Wrap it in `try { … } catch (SqlException e) when (e.Number is 2601 or 2627) { }`. A concurrent insert won, and the transaction stays usable, because SQL Server treats 2627 as a statement-level error.
  6. **Step 6, lock.** One statement through `db.StockBalances.FromSqlRaw(sql, parameters)`, **tracked**:
     ```sql
     SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, ROWLOCK, FORCESEEK, INDEX(PK_StockBalances))
     WHERE [LocationId] IN (@l0, @l1, …) AND [ItemId] IN (@i0, @i1, …)
     ```
     - Build one `SqlParameter` (`SqlDbType.UniqueIdentifier`) per distinct location and item, each list in `SqlGuid` order.
     - The cross product equals the touched set for receipts and issues: every line moves between the same two locations. Assert `rows.Count == items.Count * locations.Count`, and throw `InvalidOperationException` otherwise.
     - The parameterized `IN` seek on the clustered key is what orders the locks (spec §6 step 6, spec audit `:31`). `INDEX(PK_StockBalances)` pins it: `FORCESEEK` alone could pick `IX_Balances_Location`, which also seeks this predicate and would lock in (LocationId, ItemId) order. Add no index, and no `ORDER BY` that would change the access path.
  7. **Step 7, re-read master data.** Use `db.Items.FromSqlRaw("SELECT * FROM [inventory].[Items] WITH (READCOMMITTEDLOCK, FORCESEEK) WHERE [Id] IN (@i0, @i1, …)", parameters).AsNoTracking().ToListAsync()`, with one `SqlParameter` (`SqlDbType.UniqueIdentifier`) per item id, and the same for `Locations` with the location ids. Keep the filter inside the raw SQL and keep `FORCESEEK`: a scan under `READCOMMITTEDLOCK` would S-lock every row of the table, other tenants' included (TASKS.md IMS-6: master-data reads carry `FORCESEEK`). RLS supplies the `TenantId` seek, as in `DeactivateItem/Handler.cs:38-43`. Prompt 06 tests this step with a deactivation held open mid-posting.
     - The first item with `!IsActive` returns `InactiveItemOutcome`; then the first location with `!IsActive` returns `InactiveLocationOutcome`. Check in `LineNo` order, then the location ids in line order. Return without committing.
     - Build a `Dictionary<BalanceKey, Balance>` from the locked rows. Take `IsVirtual = location.Kind != LocationKind.Physical` and `AllowNegative = location.AllowNegativeStock` from **this read**, not from the row (spec §6 step 7). Take `OnHand`, `StockValue`, `LastUnitCost` and `LastMovementId` from the row.
  8. **Step 8, compute.** `var result = PostingRules.Post(command.Kind, command.Lines, balances)`.
     - Catch `PostingRejectedException rejection` and return `RejectedOutcome(rejection)`.
     - Then **D1**: if any `result.Balances` has `Math.Abs(OnHand) > MaxColumn` or `Math.Abs(StockValue) > MaxColumn`, return `BalanceOutOfRangeOutcome(itemId, locationId)` for the first one. `MaxColumn` is `private const decimal MaxColumn = 999_999_999_999_999.9999m;` in the engine: the `decimal(19,4)` max, with a comment that it equals `PostingValidation.MaxColumn` (prompt 01). Don't reference Features from Persistence.
  9. **Step 9, ids and writes.**
     - Take `n = result.Movements.Count` contiguous ids: `EXEC sys.sp_sequence_get_range @sequence_name = N'inventory.MovementIds', @range_size = @n, @range_first_value = @first OUTPUT;` via `ExecuteSqlRawAsync`. `@first` is `new SqlParameter("@first", SqlDbType.Variant) { Direction = ParameterDirection.Output }`, and you read it with `Convert.ToInt64(first.Value)`.
     - **If `inventory_app` is refused, or the output can't be read,** fall back to `n` calls of `db.Database.SqlQueryRaw<long>("SELECT NEXT VALUE FOR [inventory].[MovementIds] AS [Value]").ToListAsync()` then `.Single()`. Say which one you used in your report.
     - Assign ids in `result.Movements` order (that's `LineNo` order). Add `StockMovement { Id, DocumentId = document.Id, LineNo, ItemId, FromLocationId, ToLocationId, Quantity, TotalCost, ReversesMovementId, PostedAt = now, PostedBy }` for each.
     - For each tracked `StockBalance` row, apply the matching `result.Balances` entry: `OnHand`, `StockValue`, `LastUnitCost`, and `AllowNegative` (from the step 7 read). Also set `LastMovementId` to the highest new movement id whose From or To is that balance's location, for that item. That covers virtual balances too.
     - Call `await db.SaveChangesAsync(ct)` once. Movements are inserted, which works because the trigger is declared, and the balances are updated.
  10. **Step 10.** `await tx.CommitAsync(ct)`. Return `PostedOutcome(document, movements, physicalBalances, Replayed: false, BalancesAsOf: now)`. `physicalBalances` are the updated tracked rows with `!IsVirtual`, ordered by `ItemId`, `LocationId`.
- After `ExecuteAsync` returns, if `duplicate` was set, call `FindExistingAsync(command.IdempotencyKey, command.RequestHash, ct)`. A row must exist, because a 2627 means the other transaction committed. If it returns null, throw `InvalidOperationException`. Otherwise return its outcome: a replay, or a reused key.
- If a commit succeeds but its acknowledgement is lost, the strategy retries. The retry then hits 2627 at step 4 and replays its own document. That's correct, so don't add a "verify succeeded" callback.
- Each attempt must start clean, because a retried attempt must not see entities from the failed one. That's why `ChangeTracker.Clear()` comes first. Don't hold state across attempts, except the `duplicate` flag, which you reset at the start of each attempt.

### 5. `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs`
- Change the `UseSqlServer` options lambda (line 33) to `sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory").EnableRetryOnFailure(maxRetryCount: 3)`.
- Change line 35 to `builder.EnrichSqlServerDbContext<InventoryDbContext>(settings => settings.DisableRetry = true);`. Aspire's default is `EnableRetryOnFailure()` with 6 retries, and Aspire requires `DisableRetry` when a custom strategy is configured. Add a one-line comment: spec §6 says 1205 retries up to 3 times.
- Add `builder.Services.AddScoped<PostingEngine>();` after the `InventoryModule` registration.

## Do not
- Touch files outside the `files` list. Specifically, don't change `Domain/*`, `InventoryDbContext.cs`, migrations or `DatabasePrincipals.cs`. If a grant is missing, stop and report it, because adding one needs approval.
- Add a column, index, type or migration, or an `UPDATE`/`DELETE` statement on the documents or movements tables.
- Add a package.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
! grep -nE 'UPDATE\s+\[?inventory\]?\.\[?Stock(Documents|Movements)|DELETE\s+(FROM\s+)?\[?inventory\]?\.\[?Stock' src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs
grep -n 'INDEX(PK_StockBalances)' src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs
grep -c 'READCOMMITTEDLOCK, FORCESEEK' src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs   # 2
grep -n 'EnableRetryOnFailure(maxRetryCount: 3)' src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
```
- All existing tests still pass (Items, Locations, ledger schema, T5, T6, T7, T11, T12), now with 3 retries.
- In your report, list which id-taking path you used, and anything in the steps you couldn't follow as written.
