---
type: prompt
plan: "[[2026-11-11-s6-transfers-adjustments]]"
id: 05
unit: IMS-10
status: done
model: opus
effort: high
depends_on: [04]
parallel_group: E
files:
  create:
    - tests/Inventory.IntegrationTests/RetryLog.cs
    - tests/Inventory.IntegrationTests/LedgerInvariants.cs
  modify:
    - tests/Inventory.IntegrationTests/ApiFactory.cs
    - tests/Inventory.IntegrationTests/PostingConcurrencyTests.cs
  delete: []
---
# 05 · T3: the clustered-key lock path, retry counter, ledger invariants

## Task
Add T3 (spec §11): 50 parallel transfers A→B and B→A. They must all succeed **without a single deadlock retry**, and the ledger invariants must hold afterwards. Build the two test helpers T3 needs: a per-app counter of EF execution-strategy retries, and an INV-1/2/3/5 checker. Prove the counter isn't vacuous by asserting it fires in the existing deadlock-retry test. Tests only: no production code changes.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-10`. Don't switch it. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any `dotnet` command.
- **Why zero retries (plan D8):** spec §6 step 6 claims, marked **[Likely]**, that locking every touched balance in one `UPDLOCK, ROWLOCK` statement seeking the clustered key `(TenantId, ItemId, LocationId)` makes every transaction lock in the same order, so A→B and B→A transfers never deadlock. The engine's statement is `PostingEngine.cs:163-176` (`… WITH (UPDLOCK, ROWLOCK, FORCESEEK, INDEX(PK_StockBalances)) WHERE [LocationId] IN (…) AND [ItemId] IN (…)`). The engine runs inside `SqlServerRetryingExecutionStrategy` with `MaxRetryCount` 3 (`InventoryModuleExtensions.cs:33-37`), so a deadlock (1205) is retried silently. "All 201" alone would hide one; counting retries is the proof. **If T3 sees any retry, stop and report it** (the count, and which statements deadlocked if you can capture them) as a finding against spec §6 step 6. Don't loosen the assertion: CLAUDE.md says ask before disabling a test.
- **Stack:** xUnit v3, Shouldly, `WebApplicationFactory<Program>` (`ApiFactory`), Testcontainers SQL Server 2025. Every test creates its own tenant.

### 1. `RetryLog.cs` (create)
- An `internal sealed class RetryLog : ILoggerProvider` with a thread-safe `int Count` (via `Interlocked`). Its loggers increment it when `eventId.Id == CoreEventId.ExecutionStrategyRetrying.Id` (`Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId`).
- Every other `Log` call is a no-op. `IsEnabled` returns true. `BeginScope` returns null. `Dispose` is a no-op.
- `Microsoft.EntityFrameworkCore` and `Microsoft.Extensions.Logging` are already available to the test project (it references the API and uses EF types in `PostingConcurrencyTests.cs:10-11`). Add no package.

### 2. `ApiFactory.cs` (modify)
- Mirror the `Clock` hook (`:13-14`, `:25-32`): add `internal RetryLog? Retries { get; init; }`.
- When it's set, register it in `ConfigureWebHost` (for example `builder.ConfigureLogging(l => l.AddProvider(retries))`). Add a **provider-specific** filter so configuration can't suppress it: `l.AddFilter<RetryLog>(category: null, LogLevel.Trace)`. Provider-specific rules win over the category rules in `appsettings*.json`.
- Null keeps today's behaviour.
- Use: `await using var app = new ApiFactory(db.AppConnectionString) { Retries = retries };`. `PostingClockTests.cs:28` does the same with `Clock`.

### 3. `LedgerInvariants.cs` (create, plan D9)
- `internal static class LedgerInvariants` with `public static async Task AssertHoldAsync(SqlConnection tenantConnection, CancellationToken ct)`. The connection comes from `LedgerRows.OpenAsync(db, tenant, ct)`, so RLS limits it to one tenant.
- Read all `inventory.StockMovements` (`Id, DocumentId, [LineNo], ItemId, FromLocationId, ToLocationId, Quantity, TotalCost, ReversesMovementId`; `LineNo` is reserved, so bracket it). Map them to `Inventory.Domain.LedgerMovement` (internal; the test project has `InternalsVisibleTo`).
- Read all `inventory.StockBalances` (`ItemId, LocationId, IsVirtual, AllowNegative, OnHand, StockValue, LastUnitCost, LastMovementId`).
- **Mirror the existing in-memory oracle rule for rule:** `LedgerHarness.AssertInvariants` (`tests/Inventory.UnitTests/LedgerHarness.cs:85-125`). The integration project can't reference the unit-test project, so port its folds and read the rows from SQL instead of memory. Include its check that a virtual balance's `LastUnitCost` is null.
- Before calling `CostingPolicy.LastUnitCost`, filter the movements by **item and location**. It throws on any movement that is for another item or doesn't touch the location (`src/Modules/Inventory/Inventory/Domain/CostingPolicy.cs:64-73`).
- If T3 ever counts a retry, `RetryLog` can't tell which DbContext retried: it counts Platform's too. Capture the retried message and name the context in your report before attributing it to spec §6 step 6.
- Collect failures as readable strings, then `failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures))`:
  - **INV-1 projection.** For every balance: `OnHand` = Σ qty in − Σ qty out over the movements touching it (`ToLocationId` = in, `FromLocationId` = out). Physical: `StockValue` = Σ cost in − Σ cost out. Virtual: `StockValue` = 0. `LastMovementId` = the max id touching it. Every (item, location) that appears in a movement has a balance row.
  - **INV-2 conservation.** Per item, Σ `OnHand` across all its balances = 0.
  - **INV-3 physical sanity.** Physical: `OnHand >= 0` ⇒ `StockValue >= 0`; `OnHand == 0` ⇒ `StockValue == 0`; `!AllowNegative` ⇒ `OnHand >= 0`.
  - **INV-5 last unit cost.** Physical: `LastUnitCost` equals `CostingPolicy.LastUnitCost(new BalanceKey(item, location), movementsTouchingIt)`.
- XML doc: INV-4 (reversal mirror) is added with reversals in S9, and S11's health endpoint is the product check; this is the test oracle.

### 4. `PostingConcurrencyTests.cs` (modify)
- **Positive control:** in `Deadlocked_posting_is_retried_and_posts_once` (`:367-404+`), replace `await using var app = db.CreateApp();` with `var retries = new RetryLog(); await using var app = new ApiFactory(db.AppConnectionString) { Retries = retries };`. Add `retries.Count.ShouldBeGreaterThanOrEqualTo(1);` after the `201` assertion. Change nothing else in that test. If the control fails, the hook is wrong: fix `RetryLog`/`ApiFactory`, never the control.
- **T3:** add `[Fact] public async Task Fifty_parallel_transfers_both_ways_never_deadlock()`. Update the class summary to mention T3.
  - **Setup:**
    - `var retries = new RetryLog();` and the app with `Retries = retries`; one tenant with an Admin; physical locations A (`"WH-A"`) and B (`"WH-B"`); 3 items.
    - Receive 100 of each item into A and into B, at different unit costs per item and location, for example A @ 10.00/20.00/30.00 and B @ 12.50/21.75/33.10. **Every touched balance must exist before the race**, so step 5 inserts nothing and only the step-6 lock path is under test. Use `PostingTests.ReceiveAsync`.
    - Record each item's initial A + B `StockValue` (`PostingTests.BalanceAsync`).
    - Then set `var retriesBefore = retries.Count;`, since the setup receipts shouldn't retry, but measure only the race.
  - **Race:**
    - Use the existing `InParallelAsync` (`:411`) pattern so all 50 sends start behind one gate.
    - Send 25 A→B transfers with lines `[item1, item2, item3]` and 25 B→A transfers with lines `[item3, item2, item1]` (the opposite order, so the request's line order can't be what keeps it safe), each line quantity 1, interleaved by index. Each send uses a fresh `PostingTests.NewKey()`.
    - Use the builders from prompt 04: `TransferTests.Transfers`, `TransferTests.Transfer(from, to, lines…)`, `TransferTests.TransferLine(item, qty)`.
    - `InParallelAsync` takes one `Func<Task<HttpResponseMessage>>`. Either add an index-aware overload as a private helper in this file, or build the 50 tasks behind your own gate in the test.
  - **Assert:**
    - All 50 are `201`, using `Count(...)` and `Statuses(...)` (`:498-500`) for the message. So no `409 concurrency-conflict` and no `500`.
    - `(retries.Count - retriesBefore).ShouldBe(0, "a deadlock was retried: the step-6 lock order didn't hold")`.
    - Each item is 100 at A and 100 at B (25 out, 25 in each way).
    - Each item's A + B `StockValue` equals its initial A + B value exactly: transfers conserve value.
    - `await LedgerInvariants.AssertHoldAsync(connection, ct)`.
    - The tenant has exactly 50 `Transfer` documents (`DocumentsOfTypeAsync(connection, "Transfer", ct)`) with 150 movements.

### Review constraints that apply
- Tests only read the ledger: no `UPDATE`/`DELETE` on `StockDocuments`/`StockMovements`.
- Decimals only. Compare decimals as numbers, never as text (IMS-8 Downstream: a 201 and a replay format differently).

## Do not
- Touch files outside the `files` list. If you must, stop and report why. Don't edit anything in `src/`.
- Weaken T3's zero-retry assertion, raise the retry budget, or add delays or jitter to make it pass. Report instead.
- Change any other existing test in `PostingConcurrencyTests.cs`.
- Add packages.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingConcurrencyTests'
for i in 1 2 3; do dotnet test --project tests/Inventory.IntegrationTests --filter-method '*Fifty_parallel_transfers_both_ways_never_deadlock' || exit 1; done   # stable, not lucky
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
git diff --stat develop -- src   # unchanged since prompt 03
```
- `Deadlocked_posting_is_retried_and_posts_once` passes with `retries.Count >= 1`, which proves the counter works.
- T3 passes three runs in a row with 0 retries.
