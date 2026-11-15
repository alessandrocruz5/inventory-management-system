---
type: prompt
plan: "[[2026-11-15-s9-reversals]]"
id: 01
unit: IMS-13
status: todo
model: opus
effort: xhigh
depends_on: []
parallel_group: A
files:
  create: []
  modify:
    - src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs
    - src/Modules/Inventory/Inventory/Persistence/PostingCommand.cs
  delete: []
---
# 01 · Engine: shared steps and `ReverseAsync`

## Task
In `PostingEngine`, move the SQL of steps 3, 4, 6, 7 and 9 and the execution-strategy wrapper out of `AttemptAsync`/`PostAsync` into private helpers, **without changing any behaviour or SQL text**, then add `ReverseAsync(ReversalCommand, CancellationToken)`: the reversal write path (spec §6 steps 3, 4, 6–10) built on the same helpers and on `PostingRules.Reverse`. Add the command and two outcomes to `PostingCommand.cs`. No route yet (prompt 03); the existing posting suites are the regression net.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-13` (stop if `git branch --show-current` says otherwise). .NET 10, `Nullable` on, `TreatWarningsAsErrors`. Module types are `internal`. `decimal` only. Raw SQL only through EF (`Database.SqlQueryRaw`, `ExecuteSql*`, `FromSqlRaw`): a connection opened another way has no tenant session context.
- **Ledger rules you must keep** (spec §6, CLAUDE.md review constraints): documents and movements are insert-only (triggers + `DENY UPDATE, DELETE`); `PostedAt` = `clock.GetUtcNow()`; deadlocks retry inside `db.Database.CreateExecutionStrategy()` (budget 3); every attempt starts with `db.ChangeTracker.Clear()`; balance locks are taken by **one** statement seeking the clustered key `(TenantId, ItemId, LocationId)`; add no index; the step-7 reads keep `READCOMMITTEDLOCK, FORCESEEK`.
- Read `src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs` in full first. Today: `FindExistingAsync` `:28-59`; `PostAsync` `:65-89`; `AttemptAsync` `:92-300` (step 3 applock `:101-109`; step 4 insert + 2601/2627 → `return null` `:111-133`; step 5 `:135-158`; step 6 lock statement + count check `:160-185`; step 7 reads `:187-205` and inactive checks `:207-222`; balances `:224-235`; step 8 `:237-252`; step 9 `:254-295`; commit `:297-299`); helpers `:306-341`.

### Part 1 — extract helpers (pure move)
Create these private members and make `AttemptAsync`/`PostAsync` call them. Copy the SQL strings, hints, parameters, `#pragma warning disable/restore EF1003` and comments **verbatim**; only the surrounding C# changes.
1. `private async Task<PostingOutcome> RunAsync(Func<CancellationToken, Task<PostingOutcome?>> attempt, Func<CancellationToken, Task<PostingOutcome>> lostKey, CancellationToken ct)` — the body of `PostAsync` today: `outcome = await db.Database.CreateExecutionStrategy().ExecuteAsync(attempt, ct); outcome ??= await lostKey(ct);` inside `try`, `catch (RetryLimitExceededException) { outcome = new RetriesExhaustedOutcome(); }`, then the `ChangeTracker.Clear()` when `outcome is not PostedOutcome { Replayed: false }`. `PostAsync` becomes:
   ```csharp
   public Task<PostingOutcome> PostAsync(PostingCommand command, CancellationToken ct) => RunAsync(
       token => AttemptAsync(command, token),
       // Null: a parallel duplicate committed the key first, so a row must exist now. Answer as step 2 would.
       async token => await FindExistingAsync(command.IdempotencyKey, command.RequestHash, token)
           ?? throw new InvalidOperationException(
               $"Idempotency key '{command.IdempotencyKey}' hit a unique violation, but no document holds it."),
       ct);
   ```
   Keep `PostAsync`'s XML doc.
2. `private async Task TakeLedgerLockAsync(CancellationToken ct)` — step 3 (`sp_getapplock … 'Shared'`, throw when `< 0`).
3. `private async Task<bool> TryInsertDocumentAsync(StockDocument document, CancellationToken ct)` — `db.StockDocuments.Add(document); SaveChangesAsync`; `false` on `DbUpdateException` with inner `SqlException { Number: 2601 or 2627 }`, else `true`. `AttemptAsync` builds the `StockDocument` as today and does `if (!await TryInsertDocumentAsync(document, ct)) return null;`.
4. `private async Task<List<StockBalance>> LockBalancesAsync(IReadOnlyList<Guid> itemIds, IReadOnlyList<Guid> locationIds, CancellationToken ct)` — the step-6 `FromSqlRaw` statement with `UPDLOCK, ROWLOCK, FORCESEEK, INDEX(PK_StockBalances)`, `.AsTracking()`. The caller passes ids already sorted by `new SqlGuid(id)`. The items × locations count check **stays in `AttemptAsync`**.
5. `private async Task<(Dictionary<Guid, Item> Items, Dictionary<Guid, Location> Locations)> ReadMasterDataAsync(IReadOnlyList<Guid> itemIds, IReadOnlyList<Guid> locationIds, CancellationToken ct)` — the two step-7 `FromSqlRaw` reads (`READCOMMITTEDLOCK, FORCESEEK`), `.AsNoTracking()`, keyed by id. The inactive checks stay in each attempt.
6. `private async Task<List<StockMovement>> WriteAsync(StockDocument document, PostingResult result, IReadOnlyDictionary<BalanceKey, StockBalance> tracked, IReadOnlyDictionary<Guid, Location> locations, Guid postedBy, DateTimeOffset now, CancellationToken ct)` — step 9 exactly as today: `TakeMovementIdsAsync(result.Movements.Count)`, map `PlannedMovement`s in order to `StockMovement` (`Id = firstId + i`, `ReversesMovementId = m.ReversesMovementId`, `PostedAt = now`, `PostedBy = postedBy`), `AddRange`, compute each touched key's last new movement id (virtual keys included), set each `result.Balances` row's `OnHand`, `StockValue`, `LastUnitCost`, `AllowNegative = locations[row.LocationId].AllowNegativeStock`, `LastMovementId`, `SaveChangesAsync`. Returns the movements. The commit stays in the attempt.
- After Part 1, `AttemptAsync` reads: clear tracker, `now`, `BeginTransactionAsync`, `TakeLedgerLockAsync`, build document, `TryInsertDocumentAsync`, step 5 loop (unchanged, stays inline), compute sorted `itemIds`/`locationIds`, `LockBalancesAsync`, count check, `ReadMasterDataAsync`, inactive checks, balances dictionary, `PostingRules.Post` in try/catch, D1 range check, `WriteAsync`, `CommitAsync`, `return new PostedOutcome(document, movements, PhysicalInKeyOrder(rows), Replayed: false, BalancesAsOf: now)`.
- Build and run the Part 1 regression commands (Done when, first block) **before** Part 2. If anything fails, fix the move; never change a test.

### Part 2 — `ReverseAsync`
- **`PostingCommand.cs`** — append, matching the file's XML-doc style:
  ```csharp
  /// <summary>
  /// A reversal that passed the route's checks (plan D3): the original document is not a reversal and had no reversal
  /// when checked. <see cref="Original"/> is every movement of it, revaluations included, in any order.
  /// </summary>
  internal sealed record ReversalCommand(
      Guid OriginalDocumentId, IReadOnlyList<LedgerMovement> Original, string IdempotencyKey, byte[] RequestHash, Guid PostedBy);

  /// <summary>The original already has a reversal, found after a race on <c>UX_StockDocuments_Reverses</c> (plan D5).</summary>
  internal sealed record AlreadyReversedOutcome(Guid ReversalDocumentId) : PostingOutcome;

  /// <summary>A later movement touched a physical balance of the original (spec §6): 409 reversal-not-latest.</summary>
  internal sealed record ReversalNotLatestOutcome(Guid ItemId, Guid LocationId, Guid BlockingDocumentId) : PostingOutcome;
  ```
  Also update the `PostingOutcome` summary only if it names "posting" in a way that excludes reversals (one phrase, no other edits).
- **`PostingEngine.ReverseAsync`** (public, beside `PostAsync`, with an XML doc citing spec §6 reversal rules and plan D5–D9):
  ```csharp
  public Task<PostingOutcome> ReverseAsync(ReversalCommand command, CancellationToken ct) => RunAsync(
      token => AttemptReversalAsync(command, token),
      async token => await FindExistingAsync(command.IdempotencyKey, command.RequestHash, token)
          ?? await AlreadyReversedAsync(command.OriginalDocumentId, token)
          ?? throw new InvalidOperationException(
              $"Reversal of {command.OriginalDocumentId} hit a unique violation, but neither its key nor a reversal exists."),
      ct);
  ```
  `AlreadyReversedAsync`: `await db.StockDocuments.AsNoTracking().Where(d => d.ReversesDocumentId == originalId).Select(d => (Guid?)d.Id).SingleOrDefaultAsync(ct)` → `id is { } r ? new AlreadyReversedOutcome(r) : null` (return type `Task<PostingOutcome?>`).
- **`private async Task<PostingOutcome?> AttemptReversalAsync(ReversalCommand command, CancellationToken ct)`**, in this order:
  1. `db.ChangeTracker.Clear(); var now = clock.GetUtcNow(); await using var tx = await db.Database.BeginTransactionAsync(ct); await TakeLedgerLockAsync(ct);`
  2. Step 4: `new StockDocument { Type = StockDocumentType.Reversal, ReversesDocumentId = command.OriginalDocumentId, ReasonCode = null, Reference = null, Notes = null, IdempotencyKey = command.IdempotencyKey, RequestHash = command.RequestHash, PostedAt = now, PostedBy = command.PostedBy }`; `if (!await TryInsertDocumentAsync(document, ct)) return null;` (comment: a 2601/2627 here is either the key or `UX_StockDocuments_Reverses`; `ReverseAsync`'s fallback tells them apart, D5). `ReasonCode` null even for an adjustment's reversal (`CK_StockDocuments_ReasonCode`).
  3. No step 5 (comment: the original's posting created every balance it touched).
  4. Step 6: `touched` = for each original movement, `(ItemId, FromLocationId)` and `(ItemId, ToLocationId)`, distinct. `itemIds`/`locationIds` distinct and sorted by `new SqlGuid(id)`. `var rows = await LockBalancesAsync(itemIds, locationIds, ct);` Build `lockedByKey = rows.ToDictionary(r => new BalanceKey(r.ItemId, r.LocationId))`. If any touched key is missing → `throw new InvalidOperationException($"No balance row for {key} at step 6; the original's posting created it.")`. Comment (D7): the cross product may lock rows no movement touches; they're locked in key order and never changed.
  5. Step 7: `var (items, locations) = await ReadMasterDataAsync(itemIds, locationIds, ct);` Inactive item check over the original's movements ordered by `Id` (`Found(items, …).IsActive`, return `new InactiveItemOutcome(itemId)` for the first inactive); then inactive location over each movement ordered by `Id`, checking `ToLocationId` then `FromLocationId` (the reversal's From then To) → `new InactiveLocationOutcome(locationId)`.
  6. Balances for the touched keys only, built exactly like `AttemptAsync`'s dictionary (`IsVirtual` = `locations[...].Kind != LocationKind.Physical`, `AllowNegative` = `locations[...].AllowNegativeStock`, `OnHand`, `StockValue`, `LastUnitCost`, `LastMovementId` from the locked row).
  7. Histories (D8): for each touched key whose balance is physical, `await db.StockMovements.AsNoTracking().Where(m => m.ItemId == key.ItemId && (m.FromLocationId == key.LocationId || m.ToLocationId == key.LocationId)).Select(m => new LedgerMovement(m.Id, m.DocumentId, m.LineNo, m.ItemId, m.FromLocationId, m.ToLocationId, m.Quantity, m.TotalCost, m.ReversesMovementId)).ToListAsync(ct)`, into `Dictionary<BalanceKey, IReadOnlyList<LedgerMovement>>`. Comment: read after the step-6 lock, so this snapshot sees every commit to the balance; no lock hints (IMS-6: don't U-lock ledger reads).
  8. Step 8: `PostingRules.Reverse(command.Original, balances, histories)` in `try`. `catch (ReversalNotLatestException e)`: `var blocking = await db.StockMovements.AsNoTracking().Where(m => m.Id == e.BlockingMovementId).Select(m => m.DocumentId).SingleAsync(ct); return new ReversalNotLatestOutcome(e.ItemId, e.LocationId, blocking);` (an outcome returned without commit rolls back, as in `AttemptAsync`). `catch (PostingRejectedException rejection) { return new RejectedOutcome(rejection); }`. No D1 range check (D9: a reversal restores values the table held).
  9. Step 9: `var touchedRows = lockedByKey.Where(p => touched.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value); var movements = await WriteAsync(document, result, touchedRows, locations, command.PostedBy, now, ct);`
  10. `await tx.CommitAsync(ct); return new PostedOutcome(document, movements, PhysicalInKeyOrder(touchedRows.Values), Replayed: false, BalancesAsOf: now);`
- `PostingRules.Reverse` (`Domain/PostingRules.cs:149-278`) is final and must not change. It checks latest-only per physical key against `Balance.LastMovementId`, throws `InsufficientStockException` unless `AllowNegative`, mirrors movements in id order with `LineNo` copied and `ReversesMovementId` set, and recomputes LUC with `CostingPolicy.LastUnitCost` over the history minus the original.
- Update the class summary of `PostingEngine` to say it is the ledger's write path for postings and reversals; nothing else in comments changes.

## Do not
- Touch files outside the `files` list (no `Domain/**`, no `Features/**`, no tests). If you must, stop and report why.
- Change any SQL string, hint, parameter type or order, the step-5 loop, the count check in `AttemptAsync`, or `FindExistingAsync`.
- Add an index, migration, column, package, interface or config.
- Use `UPDATE`/`DELETE` on `StockDocuments` or `StockMovements`, `IgnoreQueryFilters()`, or `DateTime*.Now`.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
# Part 1 and final regression net (the whole posting path, the seed, the races and the deadlock retry):
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingTests' --filter-class '*TransferTests' --filter-class '*AdjustmentTests' --filter-class '*PostingConcurrencyTests' --filter-class '*PostingVectorTests' --filter-class '*PostingClockTests' --filter-class '*SeedTests' --filter-class '*DocumentQueryTests' --filter-class '*ItemHistoryTests'
dotnet test            # full suite; on the Testcontainers start-up flake: dotnet test -- --max-parallel-test-modules 1
git diff --stat        # only the two declared files
```
- `git diff src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs` shows every SQL string unchanged (moved, not edited).
- `ReverseAsync` exists, compiles, and is unused until prompt 03.
