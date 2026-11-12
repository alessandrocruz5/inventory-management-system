# Inventory System — Sprints
Status: [ ] planned · [~] in progress · [x] merged · [-] cancelled (excluded from changelog)

> Each unit's plan, prompts and acceptance criteria are in its `plan:` link. `/build-unit {KEY-id}` runs them.
> A unit with `spec:` instead of `plan:` isn't planned yet: `/plan-feature {KEY-id}` plans it from that spec slice and replaces `spec:` with `plan:` and `prompts:`. Models are provisional until then.

## Sprint 1 — Stock ledger core   (planned 2026-11-04)
Epic: IMS-1 · Branch base: `develop` · Plan: [[Inventory System — Sprint 1 Spec]]
- [x] IMS-2 — S1 Skeleton · Added · plan: [[2026-11-04-s1-skeleton]] prompts: 01, 02, 03, 04, 05, 06 · depends: — · model: sonnet (merged 2026-11-04) Skeleton: AppHost (sql 2025, api, web), ServiceDefaults, 5 module projects, Angular 22 app, T12 architecture tests (8), CI jobs `build` and `test`.
- [x] IMS-3 — S2 Platform · Added · plan: [[2026-11-04-s2-platform]] prompts: 01, 02, 03, 04, 05, 06, 07, 08, 09, 10, 11 · depends: IMS-2 · model: opus (merged 2026-11-04) Platform: Identity cookie auth (login/logout/me), tenants, `Api migrate` + `Api seed` modes, Inventory `Locations` + `ProvisionTenantAsync`, tenant middleware, AppHost `sql`→`migrate`→`seed`→`api`→`web`, Angular login/guard/shell, T12 `Migrate*` rule (10 arch tests), 19 integration tests.
- [x] IMS-4 — S3 Tenancy enforcement · Added · plan: [[2026-11-04-s3-tenancy-enforcement]] prompts: 01, 02, 03, 04, 05 · depends: IMS-3 · model: opus (merged 2026-11-05) Tenancy: `TenantId` default and save interceptor, named `Tenant` filter, RLS `security.TenantPolicy` with session-context interceptor, `Locations` tenant-owned, T5 route guard, T6 catalog, T11 isolation, T12 `IgnoreQueryFilters` rule, 62 tests.
- [x] IMS-5 — S4 Master data · Added · plan: [[2026-11-05-s4-master-data]] prompts: 01, 02, 03, 04, 05, 06, 07, 08, 09 · depends: IMS-4 · model: opus (merged 2026-11-05) Master data: `inventory.Items` with RLS, Items API (Admin writes, per-tenant SKU, `version` concurrency, deactivate idempotent under races), physical Locations API (system rows read-only), `RoleNames` and `SeedTenants` in Kernel, demo seed of QC, MKT and 20 items, `/items` (AG Grid 36.2.0) and `/locations` screens, 115 .NET and 58 web tests.
- [x] IMS-6 — S5a Ledger schema · Added · plan: [[2026-11-05-s5a-ledger-schema]] prompts: 01, 02, 03, 04 · depends: IMS-5 · model: opus (merged 2026-11-09) Ledger schema: `StockDocuments`, `StockMovements`, `StockBalances` and the `MovementIds` sequence with RLS predicates, append-only triggers (51000) and `DENY UPDATE, DELETE` for `inventory_app`; SKU immutability and `stock-not-zero` deactivation guards (item and location) under `HOLDLOCK, FORCESEEK` locks; T7 and schema tests; 152 .NET and 59 web tests.
- [x] IMS-7 — S5b Costing rules · Added · plan: [[2026-11-09-s5b-costing-rules]] prompts: 01, 02, 03 · depends: IMS-2 · model: opus (merged 2026-11-09) Costing rules: pure `Inventory.Domain` model (`CostingPolicy.Round4`/`OutflowCost`/`LastUnitCost` fold, `PostingRules.Post`/`.Reverse`, typed `PostingRejectedException`s), new `tests/Inventory.UnitTests` with the in-memory `LedgerHarness` and vector sets V, R, A, T12 domain rule live; 63 unit tests.
- [x] IMS-8 — S5c Posting engine · Added · plan: [[2026-11-09-s5c-posting-engine]] prompts: 01, 02, 03, 04, 05, 06 · depends: IMS-6, IMS-7 · model: opus (merged 2026-11-11)
  - Result: `POST /api/documents/receipts` and `/issues` on a shared `DocumentPoster` and `PostingEngine` (steps 2–10), with Idempotency-Key replay (D2 order), D1 overflow guards, retries set to 3, and T1/T2/T4/T5/T10 tests. 346 .NET tests.
  - Downstream for S6/S9/S10: `DocumentPoster.PostAsync` is the base for every posting route. Add its `counterpartIsSource` flag, and map `UnitCostRequired` and `ReversalNotLatest` in your own route. `PostingTests` exposes the shared helpers (`PostDocumentAsync`, `Receipt`, `Issue`, `ScalarAsync`).
  - Deferred nits from code-guardian: (S9) a 2601 on `UX_StockDocuments_Reverses` would look like a lost key, and needs a 409 `already-reversed`; (S11) the applock's deadlock return of -3 isn't retried; (S4 handlers) `UpdateItem`/`DeactivateItem`/`DeactivateLocation` return 500 instead of `409 concurrency-conflict` when retries run out, now that the budget is 3; (S7/T13) a 201 and a replay format the same decimal differently (`1` vs `1.0000`), so compare numbers, not text.
- [x] IMS-9 — S5d Opening stock · Added · plan: [[2026-11-11-s5d-opening-stock]] prompts: 01, 02 · depends: IMS-8 · model: sonnet (merged 2026-11-11)
  - Result: `Api seed` posts 31 opening-stock receipts (QC 19 items, MKT 12; `CANDLE-WHT` none) through `PostingEngine` under `seed:<tenant>:<sku>:<location>` keys, `PostedBy` `Guid.Empty`. A held key means already seeded (renamed branch is skipped, not failed). `Seed_twice_changes_nothing` covers the ledger; `SeedTests` added. 347 .NET tests.
  - Downstream: (S7) show `PostedBy` `Guid.Empty` as "System". (S12/T14) use `CANDLE-WHT` or an own item, since other seeded items carry stock. (S11) check ledger health on the demo tenant inside `SeedTests` or `Platform.IntegrationTests` only; `SeedTests` owns `SeedTenants.Demo` and leaves QC renamed. Compare keys with `StringComparer.Ordinal`, never `ORDER BY IdempotencyKey`. Editing an `OpeningStock` row does not repost; use an adjustment (S6).
- [x] IMS-10 — S6 Transfers and adjustments · Added · plan: [[2026-11-11-s6-transfers-adjustments]] prompts: 01, 02, 03, 04, 05 · depends: IMS-8 · model: opus (merged 2026-11-13)
- [ ] IMS-11 — S7 Stock screens · Added · spec: §12 S7 · depends: IMS-10 · model: sonnet
- [ ] IMS-12 — S8 Item history · Added · spec: §12 S8 · depends: IMS-10 · model: sonnet
- [ ] IMS-13 — S9 Reversals · Added · spec: §12 S9 · depends: IMS-10, IMS-11 · model: opus
- [ ] IMS-14 — S10 Negative stock · Added · spec: §12 S10 · depends: IMS-8, IMS-11 · model: opus
- [ ] IMS-15 — S11 Ledger health · Added · spec: §12 S11 · depends: IMS-13 · model: opus
- [ ] IMS-16 — S12 End to end · Added · spec: §12 S12 · depends: IMS-13 · model: sonnet
- [ ] IMS-17 — S13 Compose output (stretch) · Added · spec: §12 S13 · depends: IMS-16 · model: haiku
- [ ] IMS-18 — S14 Azure (stretch) · Added · spec: §12 S14 · depends: IMS-17 · model: sonnet

**Downstream (locked by IMS-2; plan [[2026-11-04-s1-skeleton]]):**
- Every test project references `xunit.v3` only (no `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`), has `<OutputType>Exe</OutputType>`, and runs under MTP through `global.json`. Create each test project together with its first test.
- `Inventory.Domain` depends only on `System.*` (not `System.Data`), itself and `Kernel`. `Kernel` never depends on EF Core, ASP.NET Core, SqlClient or `System.Data`, so its "ProblemDetails types" are slug constants or exceptions that Api maps.
- A module implementation exposes exactly one public type, `<M>ModuleExtensions`; the exception is namespaces ending in `.Migrations`, because EF generates migrations as public. Everything else is `internal`, and test projects get `InternalsVisibleTo`. Widening the surface means editing T12 in the same PR, with the reason.
- In Platform, only the `Platform.Identity` namespace touches `System.Security.Claims`.
- Only `src/Api` and `tests/**` reference a module implementation. A module may reference another module's `.Contracts`.
- Aspire resources are `sql`, `api` and `web`, and the database is `inventory` (`ConnectionStrings:inventory`). S2 wires the dev proxy `/api` → `services__api__http__0`, adds the `inventory_app` password parameter, and adds a T12 rule against `Database.Migrate*` outside the migrate step. *(Amended by IMS-3 below: resources become `sql`, `migrate`, `seed`, `api`, `web`.)*
- npm's allowed list includes `eslint` and `typescript-eslint`, as angular-eslint's peers.
- The CI job ids `build` and `test` are the ruleset's required check contexts. S12 adds `e2e`.

**Downstream (locked by IMS-3; plan [[2026-11-04-s2-platform]]):**
- Aspire resources are `sql`, `migrate`, `seed`, `api` and `web`. Every process reads `ConnectionStrings:inventory`, and only `migrate` gets the server admin. Later seed extensions (S4 items, S5d opening stock) go in `PlatformSeeder` or another `IDataSeeder`, and they run as `inventory_app`.
- `inventory_app` has schema-level `SELECT, INSERT, UPDATE` on `inventory`. **S5a must `DENY UPDATE` on `inventory.StockDocuments` and `inventory.StockMovements`** (and grant the sequence), or the schema grant covers them.
- S3:
  - Add the `TenantId` column default, the interceptor, the named filters and RLS for `inventory.Locations`, then remove `ProvisionTenantAsync`'s explicit `TenantId` assignment and filter.
  - T6's catalog check scopes to tenant-owned tables in `inventory` (and later module schemas). `identity.*` stays outside the policy and the TenantId-first index rule by design (§8).
- `EnrichSqlServerDbContext` turns on EF's retrying execution strategy. S5c's posting transactions run inside `Database.CreateExecutionStrategy()` or set `DisableRetry`.
- The `tenant_id` claim name is `Kernel.Tenancy.TenantClaims.TenantId`. Each new ProblemDetails slug lands in spec §9, Kernel `ProblemTypes` and the Angular `PROBLEM_MESSAGES` map in the same PR.
- Integration tests copy `tests/Platform.IntegrationTests`' fixture: a Testcontainers 2025 image, `MigrateCommand` as `sa`, then the app as `inventory_app`.
- S4 asserts that Staff gets `403` with no `Location` header on an Admin route (R1's `403` half is untested in S2).
- Bookkeeping (`TASKS.md`, `CHANGELOG.md`) lands on the base branch, `develop`.

**Downstream (locked by IMS-4; plan [[2026-11-04-s3-tenancy-enforcement]]):**
- Every new tenant table (S4 `Items`; S5a `StockDocuments`, `StockMovements`, `StockBalances`):
  - Its entity implements `ITenantOwned` and is configured through `InventoryDbContext.ConfigureTenantOwned`.
  - Its creating migration runs `ALTER SECURITY POLICY security.TenantPolicy ADD FILTER PREDICATE security.fn_tenant(TenantId) ON <t>, ADD BLOCK PREDICATE … AFTER INSERT, ADD BLOCK PREDICATE … AFTER UPDATE`.
  - T6's catalog test fails otherwise.
- Every new `/api` route gets a T5 probe or a reasoned exemption, or T5's route guard fails:
  - A route id from tenant B gets `404 not-found`.
  - A body id from tenant B gets `400 validation`.
  - S4 adds the `not-found` slug with its first `404` (§9, Kernel `ProblemTypes`, Angular `PROBLEM_MESSAGES`).
- Raw SQL runs only through EF: `Database.SqlQuery`, `ExecuteSql`, or a command on EF's connection after `Database.OpenConnectionAsync()`. A connection opened any other way has no session context and sees nothing.
- Querying a tenant table from a scope with no tenant throws (EF) or returns nothing (RLS). Seeders and tools use `ITenantScopeFactory.BeginScope`.
- `security.TenantPolicy` is schema-bound: altering a `TenantId` column means dropping its predicates in the same migration.
- Rolling back a migration that adds RLS: roll the schema back on that build before reverting the code.
- `tests/Inventory.IntegrationTests` exists with the copied fixture. S5a+ integration tests go there.

**Downstream (locked by IMS-5; plan [[2026-11-05-s4-master-data]]):**
- **S5a** (the ledger tables exist; tests insert movements and balances directly as `inventory_app`):
  - `PUT /items/{id}` rejects a SKU change once the item has a `StockMovements` row: `400 validation` with `errors.sku`.
  - `POST /items/{id}/deactivate` and `POST /locations/{id}/deactivate` require `OnHand = 0 AND StockValue = 0` on every physical balance of that item or location. Otherwise they return `422 stock-not-zero` with the blocking balances. Location deactivation locks its balance rows `WITH (UPDLOCK, HOLDLOCK)` through `IX_Balances_Location` (spec audit `:18,:23`).
  - S5a adds `stock-not-zero` to `ProblemTypes` and `PROBLEM_MESSAGES`. The Items and Locations screens already show `problemMessage()` for any error.
  - Deactivate sends no version, so a racing deactivate that lost on the rowversion reloads and returns `200` when the row is already inactive. Keep that when the stock check is added.
- **S5d** (opening stock): goes in `Inventory.Seeding.InventorySeeder`, keyed `seed:<tenant>:<sku>:<location>`. Both `Sku` and `Code` are editable, so S5d either keys on the seeded `Sku`/`Code` and skips a pair whose item or location no longer matches by natural key, or switches the seeder to a stored marker. Decide this in S5d's plan.
- **S7**: AG Grid 36.2.0 is installed and exact-pinned. Reuse `web/src/app/core/api/paged.ts`, `problemFieldErrors` and `shared/confirm-dialog.ts`.
- **S8**: `GET /items/{id}` gains the balance per physical location.
- **S10**: `PUT /locations/{id}` gains `allowNegativeStock`, with the §6 lock order and the `StockBalances.AllowNegative` update. The Locations screen gains the switch. S4 deliberately ignores that field in the body.
- **S5c and later**: endpoints follow `Features/<Area>/<Endpoint>/{Endpoint,Handler,Request,Response}.cs` with plain scoped handlers, sharing `Features/Problems.cs`, `Features/Paging.cs` and `Features/InventoryPolicies.cs`.
- **T5 probes**: a route mapped as `""` on a group has a trailing slash in `RoutePattern.RawText` (`GET /api/items/`). Probe with the exact raw text.

**Downstream (locked by IMS-6; plan [[2026-11-05-s5a-ledger-schema]]):**
- **S5c (posting engine):**
  - The tables, the sequence and `HasTrigger` exist. Every document and movement column is final at insert: `inventory_app` is denied `UPDATE`/`DELETE` on both tables, and the triggers throw for the owner.
  - Take movement ids with `NEXT VALUE FOR [inventory].[MovementIds]`; `StockMovement.Id` is `ValueGeneratedNever`.
  - A raw-SQL document insert must supply `Id`, or insert the document through EF inside the strategy's transaction. EF inserts work because the triggers are declared.
  - Lock hints need no `UPDATE` permission: `UPDLOCK`/`XLOCK` on `StockDocuments`/`StockMovements` work for `inventory_app` despite the deny (code-guardian, verified). Read the ledger with shared locks anyway: U locks would serialize readers for no gain.
  - `NEXT VALUE FOR` through `SqlQuery<long>(…).SingleAsync()` fails with 11719 (EF wraps it in a derived table); use `ToListAsync()` then `.Single()`. `LineNo` is reserved: write `[LineNo]` in raw SQL.
- **S5c (lock order):**
  - Deactivation takes the balance locks, then the item or location row.
  - A SKU edit takes a `HOLDLOCK` range on `IX_Movements_Item`, then the item row.
  - The three master-data reads carry `FORCESEEK`. Add no index or statistic that would make them unseekable (8622).
  - A posting's FK check on `Items` takes a shared lock during the movement insert. A SKU edit racing a first posting can therefore deadlock (1205), and the execution strategy retries it. Both sides are already under the strategy. Cover this in S5c's audit.
  - Deactivation vs a posting that inserts a new balance can deadlock (1205): the posting inserts `(I, L2)` at step 5; the deactivation U-locks `(I, L1)` and waits on `(I, L2)`; the posting's step 6 needs `(I, L1)`. The same holds for a location through `IX_Balances_Location`. Both sides retry under the strategy, so it isn't a correctness bug; cover it in S5c's audit and parallel tests (spec audit NOTE 1).
  - `HOLDLOCK` also locks the next key past the range, which can be another item's balance, or another tenant's when the item has none. A deactivation can briefly block an unrelated posting at step 6.
- **S6 (IMS-10, adjustments):** validate `reasonCode` at the request: required and in the `AdjustmentReason` enum on adjustments, absent on every other type, returning `400 validation` with `errors.reasonCode`. Reversals of adjustments store NULL. Otherwise the stricter `CK_StockDocuments_ReasonCode` surfaces as an unmapped 547 → `500`.
- **S7:** decide on `IX StockDocuments (TenantId, PostedAt)` for the `GET /documents` date filter (spec audit NOTE `:35`).
- **Tests:** `tests/Inventory.IntegrationTests/LedgerRows.cs` inserts documents, movements and balances as `inventory_app` on a raw connection. It also has `WaitUntilBlockedAsync` for lock tests. S5c+ tests post through the API instead, but can reuse the lock helper.
- **Rollback:** the object `DENY`s live in `DatabasePrincipals`, not the migration. After a `dotnet ef database update` re-up, run `migrate` to restore them; the triggers still block `UPDATE`/`DELETE` meanwhile.
- **Tests:** a plain `dotnet test` occasionally hits a Testcontainers SQL Server start-up crash (`handle-crash.sh`) when the two assemblies start containers at once; `--max-parallel-test-modules 1` avoids it.

**Downstream (locked by IMS-7; plan [[2026-11-09-s5b-costing-rules]]):**
- **S5c/S6/S9:**
  - Call `PostingRules.Post`/`.Reverse` at step 8, with balances built from the step 6 lock read and the step 7 `IsVirtual`/`AllowNegative`.
  - Insert `PlannedMovement`s with ids from `MovementIds` in `LineNo` order, then set each balance's `LastMovementId` to its last new movement.
  - Catch `PostingRejectedException` subtypes and map them: `InsufficientStock` → `422 insufficient-stock`; `UnitCostRequired` → `400 validation` with `errors["lines[i].unitCost"]` (S6); `ReversalNotLatest` → `409 reversal-not-latest` with `blockingDocumentId` (S9).
  - Every other exception from Domain is a bug.
- **S10:** replaces the `qty > OnHand` rejection in `OutflowCost` (when `AllowNegative`) with the estimate branch. Replaces the negative-inflow `InvalidOperationException` in `Post` with `Revaluation`, which emits a movement at `LineNo + 1000`. Adds sets N and X to `CostingVectorTests`. The LUC fold already groups by document. `Reverse` skips the short check when `AllowNegative` (as specified) while `Post` still rejects.
- **S11:** INV-5's windowed SQL must return what `CostingPolicy.LastUnitCost` returns. T9 can compare the two.
- **Build:** restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1` on this machine.

## Hotfixes
- [~] IMS-19 — Bump Aspire packages to 13.6.5 (GHSA-f7vh-h5g3-7jrh) · Fixed · files: src/AppHost/AppHost.csproj, src/Modules/Inventory/Inventory/Inventory.csproj, src/Modules/Platform/Platform/Platform.csproj · depends: — · model: haiku
