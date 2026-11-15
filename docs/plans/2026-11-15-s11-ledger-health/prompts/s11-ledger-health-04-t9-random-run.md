---
type: prompt
plan: "[[2026-11-15-s11-ledger-health]]"
id: 04
unit: IMS-15
status: todo
model: opus
effort: high
depends_on: [03]
parallel_group: D
files:
  create:
    - tests/Inventory.IntegrationTests/LedgerRandomRunTests.cs
  modify:
    - tests/Inventory.IntegrationTests/SeedTests.cs
  delete: []
---
# 04 · T9 random run and demo-tenant health

## Task
Write spec T9: a seeded, reproducible random run of 500 API operations over 3 items × 3 physical locations, valid and invalid. INV-1 to INV-5 must hold every 50 operations and after a rebuild that runs while postings continue, and the rebuild diff must be empty. Then add the demo tenant's health check to `SeedTests` (IMS-9 downstream). Change only tests.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-15`. Stop if `git branch --show-current` says otherwise. xUnit v3 + Shouldly; `TestContext.Current.CancellationToken`. Each integration test creates its own tenant (spec §11), except `SeedTests`, which owns `SeedTenants.Demo`.
- Spec §11 T9 `:642`: *"Seeded random run: 500 operations over 3 items × 3 locations, valid and invalid; one rebuild runs while postings continue → INV-1 to INV-5 hold every 50 operations and after the concurrent rebuild; rebuild diff is empty"*. IMS-7: *"T9 can compare"* INV-5's SQL with `CostingPolicy.LastUnitCost`.
- The three views of the invariants to check at each checkpoint (read each first):
  1. The oracle: `LedgerInvariants.AssertHoldAsync(connection, ct)` (`tests/Inventory.IntegrationTests/LedgerInvariants.cs`) on a `LedgerRows.OpenAsync(db, tenant, ct)` connection.
  2. The product: `GET /api/ledger/health` → `healthy: true`, five invariants all `passed` (prompt 02).
  3. The projection: `LedgerProjection.ReadAsync` gives `Stored == Ledger` on every row, and every physical row's `Ledger.LastUnitCost == CostingPolicy.LastUnitCost(key, touching movements)`. Reuse or mirror `LedgerProjectionTests`' private `AssertProjectionMatchesAsync` (prompt 01). If it's private, copy its few lines rather than editing that file.
- Helpers: `DatabaseFixture.{CreateTenantWithUserAsync, SignInAsync}`, `db.CreateApp()`. `PostingTests.{Receipts, Issues, PostDocumentAsync, Receipt, Line, Issue, IssueLine, NewKey, NewItemAsync, NewLocationAsync}`; `TransferTests.{Transfers, Transfer, TransferLine}`; `AdjustmentTests.{Adjustments, Adjustment, Delta}` (reason codes: `Damaged`, `Lost`, `Found`, `CountCorrection`, `Other`); `ReversalTests.ReverseAsync(client, documentId, key, ct)`; `LocationsTests.AllowNegativeAsync(client, id, true, ct)`. A `201` body has `id`.

### 1. `LedgerRandomRunTests.cs` (new, `public sealed class LedgerRandomRunTests(DatabaseFixture db)`), with one `[Fact] Random_run_keeps_every_invariant_and_rebuild_finds_nothing`
- **Determinism.** `const int Seed = 20261115;` and `var random = new Random(Seed);`. Every assertion message includes `$"seed {Seed}, op {i}, {description}"`, so a failure names the operation to replay. Draw quantities from a small set that includes 4-dp values (e.g. `1, 2, 3, 5, 0.5m, 1.2345m`) and unit costs such as `10m, 12.5m, 10.0001m, 99.9999m`.
- **Setup.** One Admin tenant. Items I1–I3. Locations L1, L2 (toggle off) and L3 (toggle on via `AllowNegativeAsync`). An opening receipt for each item at L1 so the early operations have stock.
- **500 operations**, chosen with weights:
  - receipt (any location, 1–3 lines with distinct items);
  - issue (sometimes more than on hand: `422 insufficient-stock` at L1/L2, an estimate `201` at L3);
  - transfer (random pair of distinct locations);
  - adjustment (`+` with a unit cost, `+` without one, which is `400` when on hand ≤ 0, and `−`);
  - reversal of a random earlier `201` document: `201`, `409 reversal-not-latest`, `409 already-reversed`, `422 cannot-reverse-reversal` or `422 insufficient-stock`;
  - replay of a random earlier successful request with the same key and body → `200` with `Idempotent-Replayed: true`;
  - a deterministic invalid request (quantity 0 → `400`, or a `Guid.NewGuid()` item → `400`).
- Keep a list of `(key, route, body)` for the replays and of successful document ids and types for the reversals.
- **Per operation:** assert the status is in that operation's allowed set and is **never** `500`. Count outcomes by `(kind, status)`.
- **Every 50th operation** (i = 50, 100, …, 500), run all three invariant views above.
- **Concurrent rebuild, once at i = 250.** Build 20 operations up front from the seeded `random` and send them with `Task.WhenAll`. **Only operations whose status doesn't depend on order go in the burst** (plan D13): receipts (1–3 lines, any location) and `+` adjustments **with** a unit cost. Both are `201` whatever order the server runs them in. No issues, transfers, `−` adjustments, `+` adjustments without a cost, reversals or invalid requests: their status, or the state they leave, depends on scheduling, and a fixed seed can't control that. When the first is in flight, also send `POST /api/ledger/rebuild` and three `GET /api/ledger/health` calls; use a `TaskCompletionSource` or start them right after the first `SendAsync` begins. Then:
  - every burst posting is `201` (never `409` or `500`; a `409 concurrency-conflict` here is a finding to report, not a status to allow);
  - add the burst's requests to the replay pool (a replay is always `200`) but **not** its documents to the reversal pool: whether a burst document is "latest" on a key depends on the order they posted in;
  - count burst outcomes under their own kinds (`burst-receipt`, `burst-adjustment`), apart from the sequential counts;
  - the rebuild is `200` with `differences` empty;
  - every concurrent health response is `healthy: true` (each check is one snapshot, plan D3);
  - all three invariant views pass afterwards.
- **After the run:** assert that the outcome counts cover at least one `201` for receipt, issue, transfer, adjustment and reversal, plus at least one `422`, one `409`, one replay `200` and one `400`. A run that never exercises an outcome isn't evidence. If the seed misses one, adjust the weights, not the assertion. Burst outcomes don't count toward this coverage.
- **Why counts repeat.** After the burst, every status depends only on quantities. `OnHand` doesn't depend on the order the burst posted in, and burst documents are never reversed, so the random sequence and every status match between runs. Only costs after the burst can differ (`LastUnitCost` takes the latest inflow), and each run's oracle checks those. If two runs give different counts, look for a status that depends on order. Fix the operation mix, not the comparison.
- Keep the runtime reasonable (one `HttpClient`, sequential except for the burst). Record the elapsed time in the final message.

### 2. `SeedTests.cs` (IMS-9: demo tenant health, inside the existing single ordered `[Fact]`)
Read the whole file first and keep the step order. Add a local helper `AssertLedgerHealthyAsync(HttpClient admin, CancellationToken ct)` (`GET /api/ledger/health` → `200`, `healthy: true`) and call it:
- in step 3, with the `admin` client already signed in as `admin@demo.test`, after the replay assertions;
- after step 4 (rename and reseed), with a new `db.CreateApp()` and `SignInAsync(app, "admin@demo.test")`. The QC rename must not break health.

Don't rebuild the demo tenant; health is read-only.

## Do not
- Touch files outside `files`, including `src/**`. If a view disagrees, stop and report the seed, the operation and the three views' outputs; don't patch production code here.
- Use `Thread.Sleep`/fixed delays for ordering (use `LedgerRows.WaitUntilBlockedAsync` or task signals if needed), or an unseeded `Random`.
- Add dependencies.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerRandomRunTests' --filter-class '*SeedTests'
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerRandomRunTests'   # run twice: same seed, same result
dotnet test   # the declared command; plan D14
```
- If plain `dotnet test` fails only with the Testcontainers start-up crash (`handle-crash.sh`, fixture failures before any test body runs), rerun `dotnet test --max-parallel-test-modules 1`. Report both results; never report the serial run as the plain one. Any other failure is real.
- The run passes twice in a row with identical outcome counts over all 500 operations, burst kinds included, which you report in the summary.
