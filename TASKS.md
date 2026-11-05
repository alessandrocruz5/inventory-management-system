# Inventory System — Sprints
Status: [ ] planned · [~] in progress · [x] merged · [-] cancelled (excluded from changelog)

> Each unit's plan, prompts and acceptance criteria are in its `plan:` link. `/build-unit {KEY-id}` runs them.
> A unit with `spec:` instead of `plan:` isn't planned yet: `/plan-feature {KEY-id}` plans it from that spec slice and replaces `spec:` with `plan:` and `prompts:`. Models are provisional until then.

## Sprint 1 — Stock ledger core   (planned 2026-11-04)
Epic: IMS-1 · Branch base: `develop` · Plan: [[Inventory System — Sprint 1 Spec]]
- [x] IMS-2 — S1 Skeleton · Added · plan: [[2026-11-04-s1-skeleton]] prompts: 01, 02, 03, 04, 05, 06 · depends: — · model: sonnet (merged 2026-11-04) Skeleton: AppHost (sql 2025, api, web), ServiceDefaults, 5 module projects, Angular 22 app, T12 architecture tests (8), CI jobs `build` and `test`.
- [x] IMS-3 — S2 Platform · Added · plan: [[2026-11-04-s2-platform]] prompts: 01, 02, 03, 04, 05, 06, 07, 08, 09, 10, 11 · depends: IMS-2 · model: opus (merged 2026-11-04) Platform: Identity cookie auth (login/logout/me), tenants, `Api migrate` + `Api seed` modes, Inventory `Locations` + `ProvisionTenantAsync`, tenant middleware, AppHost `sql`→`migrate`→`seed`→`api`→`web`, Angular login/guard/shell, T12 `Migrate*` rule (10 arch tests), 19 integration tests.
- [x] IMS-4 — S3 Tenancy enforcement · Added · plan: [[2026-11-04-s3-tenancy-enforcement]] prompts: 01, 02, 03, 04, 05 · depends: IMS-3 · model: opus (merged 2026-11-05) Tenancy: `TenantId` default and save interceptor, named `Tenant` filter, RLS `security.TenantPolicy` with session-context interceptor, `Locations` tenant-owned, T5 route guard, T6 catalog, T11 isolation, T12 `IgnoreQueryFilters` rule, 62 tests.
- [ ] IMS-5 — S4 Master data · Added · plan: [[2026-11-05-s4-master-data]] prompts: 01, 02, 03, 04, 05, 06, 07, 08, 09 · depends: IMS-4 · model: opus
- [ ] IMS-6 — S5a Ledger schema · Added · spec: §12 S5a · depends: IMS-5 · model: opus
- [ ] IMS-7 — S5b Costing rules · Added · spec: §12 S5b · depends: IMS-2 · model: opus
- [ ] IMS-8 — S5c Posting engine · Added · spec: §12 S5c · depends: IMS-6, IMS-7 · model: opus
- [ ] IMS-9 — S5d Opening stock · Added · spec: §12 S5d · depends: IMS-8 · model: opus
- [ ] IMS-10 — S6 Transfers and adjustments · Added · spec: §12 S6 · depends: IMS-8 · model: opus
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
