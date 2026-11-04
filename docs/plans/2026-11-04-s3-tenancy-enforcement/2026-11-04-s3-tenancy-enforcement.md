---
type: plan
kind: feature
status: audited
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-04
risk: high
prompts: 5
graph_built: none
tags: [plan]
---
# S3 Tenancy enforcement (IMS-4)

## Request
> s3-tenancy-enforcement

This resolves to `TASKS.md:12`:
> - [ ] IMS-4 — S3 Tenancy enforcement · Added · spec: §12 S3 · depends: IMS-3 · model: opus

The source is spec §12, row S3 (`docs/Inventory System — Sprint 1 Spec.md:668`):
> Named query filters, `TenantId` interceptor and column defaults, RLS policy over the tables so far, session-context interceptor · Tests: T5, T6, T11 · code-guardian review

This is a re-plan of an existing ticket (workflow §8). It keeps key IMS-4, Sprint 1 and epic IMS-1. The base is `develop` (CLAUDE.md `Base branch:`), at `8c626cf9`, clean. No new sprint or tickets.

| # | Requirement |
|---|---|
| R1 | Layer 1, reads: every tenant-owned entity (only `Location` so far) has the named EF Core query filter `"Tenant"` (`e.TenantId == <the context's tenant>`). The filter reads the current scope's `ITenantContext`, so a query from a scope with no tenant throws instead of returning data. `IgnoreQueryFilters()` with no names is banned outside tests, and a T12 rule enforces it. (§8 `:487–490`; Review constraints) |
| R2 | Layer 1, writes: a `SaveChanges` interceptor sets `TenantId` on every added tenant-owned entity from `ITenantContext`. Handlers never set it. An added entity with a different non-empty `TenantId`, or one added in a scope with no tenant, throws before any SQL runs. (§8 `:491`) |
| R3 | `inventory.Locations.TenantId` defaults to `CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)`, so raw-SQL inserts get the tenant without passing it. (§5 `:283`) |
| R4 | Layer 2: SQL Server RLS. The `security` schema, `security.fn_tenant` (inline TVF, `SCHEMABINDING`) and `security.TenantPolicy` (`STATE = ON`) carry one FILTER, one BLOCK `AFTER INSERT` and one BLOCK `AFTER UPDATE` predicate on `inventory.Locations`, the only tenant table so far. They're created in one Inventory migration. (§8 `:494–515`; Hidden coupling) |
| R5 | A `DbConnectionInterceptor` runs `EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1` on every connection open of `InventoryDbContext` (sync and async) when the scope has a tenant. It does nothing when there is no tenant, which is the `migrate` step's case. No interceptor holds a tenant beyond one scope. (§8 `:492`, `:511–512`) |
| R6 | `InventoryModule.ProvisionTenantAsync` drops its explicit `TenantId` assignment and `Where(TenantId == …)` filter, and relies on R1/R2. (TASKS.md Downstream IMS-3) |
| R7 | **T5** (integration): requests alternate tenants A, B, A in one host, and each sees only its own data. Each tenant lists every collection and gets zero foreign rows; in S3 that's `Locations` through EF, with the filter on and with it off (RLS alone). A guard fails when an `/api` route is neither probed by T5 nor explicitly exempted. The `404`/`400` probes for route and body ids start in S4, with the first id-bearing route. (§11 `:629`, `:637`; spec audit `:21`, `:39`) |
| R8 | **T6** (integration): as `inventory_app` on a raw `SqlConnection` with tenant A's session context, reading or inserting tenant B's rows (and moving a row to B) gets nothing or is refused. With no session context, the same connection sees nothing and can't insert. A catalog check: every table with a `TenantId` column outside `identity` has one FILTER and two BLOCK predicates in the enabled, schema-bound `security.TenantPolicy`, a `SESSION_CONTEXT` default on `TenantId`, and `TenantId` as the first key column of its PK and of every unique index. (§11 `:638`; TASKS.md Downstream IMS-3; CLAUDE.md Conventions) |
| R9 | **T11** (integration): a pooled physical connection never carries the previous tenant. On one physical connection (`Max Pool Size=1`, same `@@SPID`), the session tenant runs A, then none (Platform's context), then B, and a second open in one scope re-sets the read-only key without error. (§8 `:511` "[Likely — prove it with a test]"; §11 `:643`) |
| R10 | Tests live in a new `tests/Inventory.IntegrationTests` (user pick, 2026-11-04). Every `## Commands` line passes, and code-guardian reviews the PR (CLAUDE.md Workflow; §12 `:668`). |

## Context used
- `TASKS.md:12`: IMS-4 is a `spec:` line; the ticket step rewrites it to `plan:`/`prompts:`.
- TASKS.md **Downstream (IMS-3)**:
  - S3 adds the column default, the interceptor, the named filters and RLS for `inventory.Locations`, then removes `ProvisionTenantAsync`'s explicit `TenantId`.
  - T6's catalog check scopes to tenant-owned tables in module schemas, and `identity.*` stays outside both the policy and the TenantId-first rule.
  - `EnrichSqlServerDbContext` turns on retries.
  - Integration tests copy `tests/Platform.IntegrationTests`' fixture.
  - `inventory_app` has schema-level `SELECT, INSERT, UPDATE` on `inventory`.
- TASKS.md **Downstream (IMS-2)**:
  - Test projects use `xunit.v3` only, with `OutputType Exe`, under MTP.
  - Kernel has no EF, ASP.NET Core or SqlClient dependency.
  - A module exposes only `<M>ModuleExtensions`; `.Migrations` is exempt.
  - Only `src/Api` and `tests/**` reference module implementations.
- CLAUDE.md:
  - Conventions: `TenantId` leads every PK and unique index; module internals are `internal`.
  - Review constraints: `TenantId` never comes from a request; no bare `IgnoreQueryFilters()` outside tests; the API connects as `inventory_app`.
  - High-stakes paths: `Inventory/Persistence/**`, `**/Migrations/**`.
  - Hidden coupling: every tenant table goes in `security.TenantPolicy` in the migration that creates it.
- Spec:
  - §4 `:192`: tenant via `ITenantContext`; seed and tests use `BeginScope`.
  - §4 `:196–198`: two tenant checks; a stray `IgnoreQueryFilters()` still hits RLS.
  - §5 `:221–230`, `:280–283`: Locations; composite FKs; the `TenantId` default.
  - §8 `:469–522`: layers 1 and 2, the policy SQL, the session-context interceptor, Identity outside the policy, raw SQL only through EF, predicates added in the creating migration, two principals.
  - §11 `:629`, `:637–643`: test isolation, T5, T6, T11.
  - §12 `:662`, `:668`: never cut S3's tests.
- Spec audit:
  - `:20`: predicates per migration, plus the catalog test.
  - `:21`: no pooled contexts; interceptors resolve the tenant per open; T5 symmetric and alternating.
  - `:22`: column default plus BLOCK predicate; raw SQL only on EF's connection.
  - `:39`: foreign body ids are `400`.
- S2 plan `2026-11-04-s2-platform.md`:
  - Decisions `:92–93`: S3 owns the default, the interceptors, the filters and RLS.
  - Log: `DOTNET_SYSTEM_NET_DISABLEIPV6=1`; `SCRATCH=$(mktemp -d)` in Done-when blocks; the `dotnet ef … --startup-project src/Api --context InventoryDbContext --output-dir Persistence/Migrations` invocation (prompt 03 `:84`); `.editorconfig` `insert_final_newline = false`.
  - Outcome: `Program` is visible through Api's `InternalsVisibleTo`.
- Code:
  - `src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs:5–31`: primary-ctor context, `Location` config, `TenantId` with no default.
  - `Location.cs:1–28`.
  - `InventoryModule.cs:10–45`: explicit `TenantId` at `:21`, `:25`, `:33`, and the S3 comment at `:23`.
  - `InventoryModuleExtensions.cs:16–26`: `AddDbContext` + `EnrichSqlServerDbContext`, no interceptors.
  - `InventoryDesignTimeFactory.cs:7–13`: `new(options)`.
  - `InventoryMigrator.cs:8–10`.
  - `Migrations/20261104110425_Initial.cs:17–55`.
  - `Migrations/InventoryDbContextModelSnapshot.cs:28`: `TenantId` without a default.
  - `src/Api/Program.cs:21–28`: `TenantContext` scoped as `ITenantContext`; `TenantScopeFactory` singleton.
  - `src/Api/Tenancy/TenantContext.cs:12`: `TenantId` throws when unset.
  - `src/Api/Setup/MigrateCommand.cs:22–31`: migrators in a no-tenant scope as `sa`.
  - `DatabasePrincipals.cs:18–31`: grants; S3 adds none.
  - `src/Modules/Platform/Platform/Tenants/TenantProvisioner.cs:22–33`: `ProvisionTenantAsync` inside `BeginScope`, the only production caller.
  - `src/Modules/Platform/Platform/Identity/AuthEndpoints.cs:24–27`: the only `/api` routes (`login` anonymous, `logout`, `me`).
- Tests:
  - `tests/Platform.IntegrationTests/DatabaseFixture.cs:19–76` and `ApiFactory.cs:7–27`: the fixture to copy.
  - `SetupTests.cs:199–208`: `Snapshot` counts `Locations` from a **no-tenant** scope, which breaks under R1 and R4.
  - `SetupTests.cs:121–126`, `:189–195`: tenant-scoped reads that stay valid.
  - `tests/Architecture.Tests/MigrationCallTests.cs:7–66`: the source-scan rule pattern to copy.
  - `InventorySystem.slnx:20–23`.
  - `.github/workflows/ci.yml`: `dotnet test` picks up every project in the slnx.
- EF Core 10.0.14 XML docs (`~/.nuget/packages/microsoft.entityframeworkcore{,.relational}/10.0.14`):
  - `EntityTypeBuilder<T>.HasQueryFilter(string, Expression)`.
  - `IgnoreQueryFilters<T>(IQueryable<T>)` and `(…, IReadOnlyCollection<string>)`.
  - `IReadOnlyEntityType.FindDeclaredQueryFilter(string)` and `GetDeclaredQueryFilters()`.
  - `DbConnectionInterceptor.ConnectionOpened` and `ConnectionOpenedAsync(DbConnection, ConnectionEndEventData, …)`.
  - `ConnectionEventData` carries the `DbContext`.
  - `SaveChangesInterceptor.SavingChanges` and `SavingChangesAsync(DbContextEventData, …)`.
- Grep (no graph):
  - `InventoryDbContext` is constructed only by DI and `InventoryDesignTimeFactory`.
  - `ProvisionTenantAsync` is called from `TenantProvisioner.cs:32` and `SetupTests.cs:118`.
  - No `IgnoreQueryFilters`, `SESSION_CONTEXT` or `AddInterceptors` exists in `src` or `tests`.
- Graph: none (`graphify-out/` is absent). The blast radius comes from grep and reading the files above.

## Decisions
- **Tests go in a new `tests/Inventory.IntegrationTests`** (user pick, 2026-11-04). It matches spec §4, and S5a–S11's integration tests (T2–T4, T7–T10) land there too. It copies the Platform fixture, as the Downstream note says, and pays for a second SQL container in CI.
- **T5 carries a route guard** (user pick, 2026-11-04). It lists every `/api` endpoint and fails unless each is probed by T5 or exempted with a reason. From S4 on, a new route without a tenant probe fails CI.
- **The interceptors are stateless singletons that read the tenant from the context they intercept.** `InventoryDbContext` takes `ITenantContext` in its constructor and exposes it as `internal ITenantContext Tenant`. Each interceptor reads `eventData.Context` as `InventoryDbContext`, so it uses the current scope's tenant every time it runs, which is the spec §8 `:492` option.
  - No per-scope interceptor instances are added to options, so EF's internal service provider doesn't churn and the options can't capture a tenant.
  - They're registered as `static readonly` instances through `o.AddInterceptors(...)` in `AddInventoryModule`.
- **The query filter fails loudly with no tenant.** The filter is `e.TenantId == CurrentTenantId`, where `CurrentTenantId => Tenant.TenantId`, and `TenantContext.TenantId` throws when the scope has no tenant (`src/Api/Tenancy/TenantContext.cs:12`).
  - A tenant query from a no-tenant scope throws `InvalidOperationException` rather than silently returning empty.
  - RLS is the backstop: with no session context it returns nothing.
- **The `"Tenant"` filter and the `TenantId` default come from one helper.** `InventoryDbContext` gets `ConfigureTenantOwned<T>(EntityTypeBuilder<T>) where T : class, ITenantOwned`, which applies `HasQueryFilter(TenantSchema.FilterName, …)` and `Property(TenantId).HasDefaultValueSql(TenantSchema.ColumnDefaultSql).ValueGeneratedNever()`.
  - `ValueGeneratedNever` keeps EF always sending the interceptor's value. The default serves raw SQL only.
  - `ITenantOwned { Guid TenantId { get; set; } }` is `internal` in `Inventory.Persistence`, not Kernel. Only Inventory has tenant tables, and a Kernel type would widen shared code for no current caller.
- **The interceptor stamps and validates, but never overwrites.** For each `Added` `ITenantOwned` entry:
  - With no tenant, it throws.
  - With `TenantId == Guid.Empty`, it sets the current tenant.
  - With any other value that isn't the current tenant, it throws.
  - `Modified` entries need no check, because `TenantId` is part of the PK and EF refuses key changes on tracked entities.
- **The session-context interceptor is registered on `InventoryDbContext` only.** `PlatformDbContext` reads `platform` and `identity`, which are outside the policy (§8 `:513`). It shares the connection pool, so T11 opens Platform's context in the middle of the pooled sequence to prove the reset.
- **One migration, `TenantPolicy`, holds the default and RLS**, because RLS without the session interceptor breaks every insert. Prompt 01 therefore ships the default, RLS and the session interceptor together, and prompt 02 adds the EF-side filter and stamping.
  - The `security` schema and the policy are created in Inventory's migrations. Later modules' tenant tables add their predicates with `ALTER SECURITY POLICY` and depend on Inventory migrating first. `MigrateCommand` runs migrators in registration order.
  - The policy is `SCHEMABINDING = ON` (the default), so a later migration that alters a `TenantId` column must drop and re-add that table's predicates.
- **The migration's default stays EF-generated if EF emits it without `ALTER COLUMN`.** `ALTER COLUMN` on a PK column fails. If `dotnet ef migrations script` shows `ALTER COLUMN [TenantId]`, prompt 01 replaces that op with a named `ALTER TABLE … ADD CONSTRAINT [DF_Locations_TenantId] DEFAULT (…) FOR [TenantId]` and keeps the generated snapshot.
- **The SetupTests change is minimal.** `SetupTests.Snapshot` counts `Locations` per seed tenant through `BeginScope` and sums them. Nothing else in Platform's tests changes.
- **No grants change.** RLS needs no permission on the predicate function for querying users (same owner, ownership chaining), and `inventory_app` keeps exactly the S2 grants (`SetupTests.Inventory_app_has_exactly_the_s2_grants` still passes).
- **The T12 rule is a source scan.** It matches `.IgnoreQueryFilters()` with an empty argument list in `src/**/*.cs` (bin, obj and Migrations excluded), copying `MigrationCallTests`' shape. The named overload `IgnoreQueryFilters([...])` stays allowed.
- **Audit 2026-11-04 (accepted):**
  - The Rollback rolls the schema back on the IMS-4 build before reverting the code, because `TenantPolicy.Down` leaves with the revert, and RLS without the interceptor blocks all inventory writes.
  - T5 test 5 also asserts that the ignored query's SQL has no `[TenantId] = @`, so only RLS can have filtered the rows.
  - T5 test 4 asserts that the three scopes share one `@@SPID`.
  - The end-to-end AppHost block polls the proxy until it's ready, and names the source of `SEED_PASSWORD`. The test counts are exact.

**Downstream (for TASKS.md at the ticket step):**
- Every new tenant table (S4 `Items`; S5a `StockDocuments`, `StockMovements`, `StockBalances`):
  - Its entity implements `ITenantOwned` and is configured through `ConfigureTenantOwned`.
  - Its creating migration runs `ALTER SECURITY POLICY security.TenantPolicy ADD FILTER PREDICATE security.fn_tenant(TenantId) ON <t>, ADD BLOCK PREDICATE … AFTER INSERT, ADD BLOCK PREDICATE … AFTER UPDATE`.
  - T6's catalog test fails otherwise.
- Every new `/api` route gets a T5 probe or a reasoned exemption, or T5's route guard fails:
  - A route id from tenant B gets `404 not-found`.
  - A body id from tenant B gets `400 validation`.
  - S4 adds the `not-found` slug with its first `404` (§9, Kernel `ProblemTypes`, Angular `PROBLEM_MESSAGES`).
- Raw SQL runs only through EF: `Database.SqlQuery`, `ExecuteSql`, or a command on EF's connection after `Database.OpenConnectionAsync()`. A connection opened any other way has no session context and sees nothing.
- Querying a tenant table from a scope with no tenant throws (EF) or returns nothing (RLS). Seeders and tools use `ITenantScopeFactory.BeginScope`.
- `security.TenantPolicy` is schema-bound: altering a `TenantId` column means dropping its predicates in the same migration.
- `tests/Inventory.IntegrationTests` exists with the copied fixture. S5a+ integration tests go there.

**Assumptions** (for `/audit-plan` to challenge):
- **EF lets the `SavingChanges` interceptor change `TenantId` on an `Added` entity**, even though it's part of the composite key. EF refuses key changes only on tracked non-`Added` entries **\[Likely\]**. Test 04 `Insert_takes_the_scope_tenant` proves it; if EF refuses, prompt 02 stops and reports.
- **`EnrichSqlServerDbContext` keeps interceptors added in the `AddDbContext` options action** **\[Likely\]**. It's self-verifying: without the session interceptor, every S2 provisioning test fails under RLS.
- **SQL Server's pool reset (`sp_reset_connection`) clears session context, including read-only keys** **\[Likely; spec §8 `:511`\]**. T11 proves it, and so does every second EF query in one scope, which would otherwise fail on the read-only key.
- **The SQL error numbers:**
  - A BLOCK predicate violation raises **33504**.
  - Re-setting a read-only session key raises **15664**.
  - SQL Server stores the default as `(CONVERT([uniqueidentifier],session_context(N'TenantId')))`.

  The prompts assert `SqlException`, and they check the number after confirming the message text. They match the default case-insensitively on `session_context(N'TenantId')`.
- **`EndpointDataSource` resolves from `WebApplicationFactory.Services`** and lists the grouped routes with `RoutePattern.RawText` = `/api/auth/login` etc. If `RawText` lacks the group prefix, prompt 04 builds the key from the pattern's segments instead.
- **`HasDefaultValueSql` together with `ValueGeneratedNever()` on a key part raises no EF model-validation error at runtime** (TreatWarningsAsErrors is compile-time only).

**Non-goals:**
- RLS or a filter on `identity.*` and `platform.*`: login runs before the tenant is known (§8 `:513`).
- New routes, ProblemDetails slugs or Angular changes. The `404`/`400` foreign-id probes and the `not-found` slug start in S4.
- `DENY UPDATE` on ledger tables, triggers and T7 (S5a).
- A soft-delete filter. Named filters only make room for one.
- Making `ITenantOwned` or the interceptors shared for other modules (no second tenant module exists).
- A shared test-support project. The fixture is copied, as the Downstream note says.
- Changing `TenantContext`, `TenantMiddleware` or `TenantScopeFactory`.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Persistence/TenantSchema.cs` | C | Constants `SessionKey = "TenantId"`, `ColumnDefaultSql`; 02 adds `FilterName = "Tenant"` | R1, R3, R5 | 01, 02 |
| `src/Modules/Inventory/Inventory/Persistence/TenantSessionInterceptor.cs` | C | `sp_set_session_context` on every open (sync + async) when the scope has a tenant | R5 | 01 |
| `src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs` | M | ctor takes `ITenantContext`; `Tenant`/`CurrentTenantId`; `TenantId` default + `ValueGeneratedNever` (01) → `ConfigureTenantOwned` with the named filter (02) | R1, R3, R5 | 01, 02 |
| `src/Modules/Inventory/Inventory/Persistence/InventoryDesignTimeFactory.cs` | M | Pass a no-tenant `ITenantContext` to the new ctor | R5 | 01 |
| `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs` | M | `AddInterceptors(TenantSessionInterceptor.Instance)` (01), then the SaveChanges interceptor (02) | R2, R5 | 01, 02 |
| `src/Modules/Inventory/Inventory/Persistence/Migrations/<ts>_TenantPolicy.cs` + `.Designer.cs` | C | `TenantId` default; `security` schema, `fn_tenant`, `TenantPolicy` with 3 predicates on Locations; `Down` reverses | R3, R4 | 01 |
| `src/Modules/Inventory/Inventory/Persistence/Migrations/InventoryDbContextModelSnapshot.cs` | M | Generated: `TenantId` default | R3 | 01 |
| `tests/Platform.IntegrationTests/SetupTests.cs` | M | `Snapshot` counts Locations per tenant scope (a no-tenant read sees nothing under RLS) | R4 (keeps S2 green) | 01 |
| `src/Modules/Inventory/Inventory/Persistence/ITenantOwned.cs` | C | Marker with `Guid TenantId { get; set; }` | R1, R2 | 02 |
| `src/Modules/Inventory/Inventory/Persistence/Location.cs` | M | `: ITenantOwned` | R1, R2 | 02 |
| `src/Modules/Inventory/Inventory/Persistence/TenantSaveChangesInterceptor.cs` | C | Stamps/validates `TenantId` on `Added` entries (sync + async) | R2 | 02 |
| `src/Modules/Inventory/Inventory/InventoryModule.cs` | M | Drop explicit `TenantId`, its `Where` and the S3 comment; drop the unused `ITenantContext` ctor param | R6 | 02 |
| `tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj` | C | xunit.v3 MTP test project (copy of Platform's) | R10 | 03 |
| `tests/Inventory.IntegrationTests/DatabaseFixture.cs`, `ApiFactory.cs` | C | Copied fixture (namespace `Inventory.IntegrationTests`) | R10 | 03 |
| `tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs` | C | T6 | R3, R4, R8 | 03 |
| `InventorySystem.slnx` | M | Add the project under `/tests/` | R10 | 03 |
| `src/Api/Api.csproj`, `src/Modules/Platform/Platform/Platform.csproj`, `src/Modules/Inventory/Inventory/Inventory.csproj` | M | `<InternalsVisibleTo Include="Inventory.IntegrationTests" />` | R10 | 03 |
| `tests/Inventory.IntegrationTests/SessionContextTests.cs` | C | T11 | R5, R9 | 04 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | C | T5 (alternation, listing with and without the EF filter, route guard) | R1, R7 | 04 |
| `tests/Inventory.IntegrationTests/TenantInterceptorTests.cs` | C | R2 behaviour + model check (every `TenantId` entity has the filter and default) | R1, R2, R3 | 04 |
| `tests/Architecture.Tests/QueryFilterCallTests.cs` | C | T12: no bare `IgnoreQueryFilters()` in `src` | R1 | 05 |

**Review constraints check:**
- `decimal` money: n/a.
- No `UPDATE`/`DELETE` on ledger tables: n/a.
- `TenantId` never from a request: holds; the tenant comes from `ITenantContext` only.
- No bare `IgnoreQueryFilters()`: tests use the named overload, and prompt 05 enforces it.
- `PostedAt` from `TimeProvider`: n/a.
- `inventory_app`, not admin: holds; the tests run as `inventory_app` and use admin only for the catalog.
- No new packages: none added. The test project reuses Platform tests' four packages.
- Section 6 and ledger columns: untouched.

## Blast radius
**Risk:** high. Prompts 01 and 02 touch `Inventory/Persistence/**` and `**/Migrations/**`. RLS changes what every `inventory_app` read and write of `inventory.Locations` sees, and a mistake either leaks tenants or blocks all inventory writes.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `InventoryDbContext` ctor callers | Direct | New `ITenantContext` param | DI resolves it (Api registers `ITenantContext`); `InventoryDesignTimeFactory.cs:9` doesn't compile | 01 passes a private no-tenant `ITenantContext` |
| `InventoryMigrator` / `MigrateCommand` (no-tenant scope, `sa`) | Direct | Session interceptor fires on the migrate connection | No: the interceptor no-ops without a tenant, migrations don't query `Locations`, and `__EFMigrationsHistory` has no `TenantId` | Test `Migrate_is_idempotent` (S2) stays green |
| `InventoryModule.ProvisionTenantAsync` | Direct | Filter + stamping replace explicit tenant code | No, if R1/R2 work | `SetupTests.ProvisionTenant_is_idempotent` and `Seed_twice_changes_nothing` |
| `TenantProvisioner.CreateAsync` (`TenantProvisioner.cs:31–32`) | Transitive | Calls provisioning inside `BeginScope` → session context set | No | All S2 tests create tenants through it |
| `SetupTests.Snapshot` (`SetupTests.cs:199–208`) | Direct (test) | Counts `Locations` from a no-tenant scope | **Yes**: the filter throws (02); RLS returns 0 (01) | 01 rewrites it to count per tenant through `BeginScope` |
| `SetupTests.cs:122`, `:190` | Direct (test) | Tenant-scoped `Locations` reads with a redundant `Where(TenantId)` | No | Left as is (no adjacent refactor) |
| `SetupTests.Inventory_app_has_exactly_the_s2_grants` | Non-graph | Grants | No: S3 adds no grant | — |
| `ModuleBoundaryTests` public-surface rule | Direct (arch) | New types | No: all new types are `internal`; the migration class is in `.Migrations` (exempt) | — |
| `PlatformDbContext` connections | Non-graph | Same connection string → same pool as Inventory | Would leak a tenant if the reset didn't clear the session | T11 opens Platform's context between two tenants on one SPID |
| `EnrichSqlServerDbContext` retry strategy | Non-graph | Reopened connections | No: every open re-runs the interceptor | — |
| `EnrichSqlServerDbContext` health check (`/health`) | Non-graph (audit) | `CanConnectAsync` on `InventoryDbContext` in a no-tenant scope | No: the interceptor does nothing, and no filter is evaluated | — |
| Every EF query on `InventoryDbContext` | Non-graph | One extra round trip per connection open (`sp_set_session_context`) | No; a perf cost the spec accepts | Noted; S5c's posting runs on one opened connection |
| Dev SQL volume with S2 data | Non-graph | The migration adds the default and policy over existing rows | No: rows already hold correct `TenantId`; the policy doesn't revalidate them | End-to-end runs the AppHost on the existing volume (migrate + seed exit 0) |
| `security.TenantPolicy` `SCHEMABINDING` | Non-graph | Locks `Locations.TenantId` against `ALTER` | Not in S3; later migrations must drop and re-add predicates | Downstream note |
| Hidden coupling: every tenant table in `TenantPolicy` in its creating migration | Hidden coupling | `Locations` added retroactively | Deviates as the spec sanctions ("over the tables so far") | T6 catalog test enforces it from now on |
| Hidden coupling: `HasTrigger()` ↔ trigger SQL | Hidden coupling | — | n/a in S3 (no triggers) | — |
| `ci.yml` `test` job | Non-graph | New project in the slnx → second SQL container | No; ~15 s and ~2 GB more on the runner | — |
| `ProjectReferenceTests` (T12) | Direct (arch) | New test project references implementations | No: `tests/**` may reference them | — |

**Known risks in the touched area:**
- Pooled contexts leak tenants (spec audit `:21`). This plan keeps `AddDbContext` + Enrich, and its interceptors are stateless.
- A `DbConnectionInterceptor` sees only connections EF opens (spec audit `:22`). The column default and the BLOCK predicate cover raw inserts, and connections opened outside EF see nothing (T6's no-context case).
- `sa` bypasses RLS by being able to turn it off (spec audit `:19`). The app runs as `inventory_app`, and T7 (S5a) proves `inventory_app` can't `ALTER SECURITY POLICY`.
- Machine prerequisites (S1/S2 Log): restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1`, and the Aspire run needs a trusted dev cert, `SSL_CERT_DIR` and a pre-pulled sql image.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-4 | S3 Tenancy enforcement | Added | 01–05 | IMS-3 | yes: `Inventory/Persistence/**`, `**/Migrations/**` (code-guardian) | See below |

Acceptance criteria for IMS-4:
- **R1:** `TenantInterceptorTests.Every_tenant_entity_has_the_tenant_filter_and_default` passes. A no-tenant query throws (`TenantIsolationTests.Query_without_a_tenant_throws`). `QueryFilterCallTests` passes, and fails on an injected bare `IgnoreQueryFilters()`.
- **R2:** in a tenant scope, an added `Location` without `TenantId` saves with the scope's tenant. A foreign `TenantId`, or no tenant at all, throws and inserts nothing.
- **R3:** a raw insert that omits `TenantId` gets the session tenant, and the catalog shows the default on every tenant table.
- **R4:** the catalog shows 1 FILTER, 1 BLOCK `AFTER INSERT` and 1 BLOCK `AFTER UPDATE` on every tenant table, in an enabled, schema-bound `security.TenantPolicy`.
- **R5/R9:** on one SPID the session tenant runs A → null (Platform context) → B, and two queries in one scope both succeed.
- **R6:** `InventoryModule.cs` has no `TenantId` reference, and the S2 provisioning and seed tests pass.
- **R7:** A, B, A requests each see their own tenant. A, B, A scopes on one SPID each list only their own 3 `Locations`. With the filter ignored, the SQL has no `TenantId` predicate and RLS still returns only the tenant's 3 rows. The route guard passes, and fails on an unlisted route.
- **R8:** with A's session context, a raw connection reads 0 of B's rows, can't insert or move a row to B (BLOCK error), and with no context reads 0 and can't insert.
- **R10:** every `## Commands` line is green, CI `build` and `test` are green, and code-guardian has reviewed.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Column default, RLS migration, session-context interceptor | IMS-4 | opus | high | — | A | `TenantSchema.cs`, `TenantSessionInterceptor.cs`, `InventoryDbContext.cs`, `InventoryDesignTimeFactory.cs`, `InventoryModuleExtensions.cs`, `Migrations/*TenantPolicy*` (2 + snapshot), `tests/Platform.IntegrationTests/SetupTests.cs` |
| 02 | Named filter, `TenantId` interceptor, provisioning cleanup | IMS-4 | opus | high | 01 | B | `ITenantOwned.cs`, `Location.cs`, `TenantSchema.cs`, `InventoryDbContext.cs`, `TenantSaveChangesInterceptor.cs`, `InventoryModuleExtensions.cs`, `InventoryModule.cs` |
| 03 | Inventory.IntegrationTests and T6 | IMS-4 | sonnet | high | 02 | C | `tests/Inventory.IntegrationTests/*` (4), `InventorySystem.slnx`, `Api.csproj`, `Platform.csproj`, `Inventory.csproj` |
| 04 | T11, T5 and interceptor tests | IMS-4 | sonnet | high | 03 | D | `SessionContextTests.cs`, `TenantIsolationTests.cs`, `TenantInterceptorTests.cs` |
| 05 | T12 rule: no bare `IgnoreQueryFilters()` | IMS-4 | haiku | low | — | E | `tests/Architecture.Tests/QueryFilterCallTests.cs` |

- 01 opus: it's in `**/Migrations/**` and `Inventory/Persistence/**`. It creates the RLS policy and the connection interceptor, and either error leaks tenants or blocks every inventory write.
- 02 opus: it's in `Inventory/Persistence/**`. It changes EF key handling (it stamps a composite-key part in `SavingChanges`), and the fail-closed filter guards tenant isolation for every later slice.
- 03 and 04 sonnet/high: they're new tests with exact cases and SQL given inline, under `tests/**`, which isn't a high-stakes glob. They're the evidence that code-guardian checks.
- 05 haiku: a mechanical copy of `MigrationCallTests` with a different regex.
- 05 has no code dependency, but it runs last in its own group: it shares the build and test tree with 01–04 (workflow §5).

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false
cd ..

# R10: counts (Architecture.Tests 11; Platform.IntegrationTests 19; Inventory.IntegrationTests 21), 0 failed
dotnet test 2>&1 | grep -Ei 'total|passed|failed'

# R3/R4: the migration's SQL
SCRATCH=$(mktemp -d)
dotnet ef migrations script Initial TenantPolicy --project src/Modules/Inventory/Inventory --startup-project src/Api \
  --context InventoryDbContext -o "$SCRATCH/s3.sql"
grep -n "CREATE SECURITY POLICY \[security\].\[TenantPolicy\]\|CREATE SECURITY POLICY security.TenantPolicy" "$SCRATCH/s3.sql"
grep -c 'PREDICATE' "$SCRATCH/s3.sql"                       # 3
grep -n "SESSION_CONTEXT(N'TenantId')" "$SCRATCH/s3.sql"
! grep -n 'ALTER COLUMN \[TenantId\]' "$SCRATCH/s3.sql"
dotnet ef migrations has-pending-model-changes --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext   # none

# R1/R6/review constraints
! grep -rnE --include='*.cs' '\.IgnoreQueryFilters\s*\(\s*\)' src
! grep -n 'TenantId' src/Modules/Inventory/Inventory/InventoryModule.cs
! grep -rn --include='*.cs' 'AddSqlServerDbContext\|AddDbContextPool' src

# R2/R5/R7/R8/R9: named tests in tests/Inventory.IntegrationTests (prompts 03, 04)
dotnet test --project tests/Inventory.IntegrationTests

# Full stack on the existing dev volume (S2 data): migrate applies TenantPolicy, seed stays idempotent under RLS
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"
dotnet run --project src/AppHost &      # dashboard: migrate Finished(0), seed Finished(0), api Healthy, web Running
# Wait up to 180 s until the proxy reaches the api: an anonymous /me returns 401.
for i in $(seq 90); do [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:4200/api/auth/me)" = 401 ] && break; sleep 2; done
[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:4200/api/auth/me)" = 401 ] || { echo 'api not ready'; kill %1; exit 1; }
# $SEED_PASSWORD: the AppHost's generated `seed-password` parameter (dashboard or AppHost user-secrets), read into the shell and never echoed
curl -s -c "$SCRATCH/jar" -H 'Content-Type: application/json' \
  -d "{\"email\":\"staff@demo.test\",\"password\":\"$SEED_PASSWORD\"}" http://localhost:4200/api/auth/login -o /dev/null -w '%{http_code}\n'  # 200
curl -s -b "$SCRATCH/jar" http://localhost:4200/api/auth/me    # tenantName "Demo Retail"
kill %1

# R10: after build-unit opens the PR
gh pr checks IMS-4                       # build ✓  test ✓
```

## Rollback
Order matters. RLS without the session interceptor blocks every inventory insert and empties every read, and `TenantPolicy.Down` exists only in the IMS-4 build.
1. Roll back the dev database first, on the IMS-4 build. As `sa`, run `dotnet ef database update Initial --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext --connection "<sa connection string>"`. `Down` drops the policy, the function, the `security` schema and the default. Alternatively, drop the AppHost's SQL volume.
2. Then revert the IMS-4 merge commit on `develop`.
- No data migration to undo: existing `Locations` rows are unchanged.

## Open questions
- None blocking. The Assumptions above are for the audit.

## Log
- 2026-11-04: planned (re-plan of IMS-4 from spec §12 S3; base `develop` @ `8c626cf9`, clean). User picks: new `tests/Inventory.IntegrationTests`; T5 route guard.
- 2026-11-04: audited, 5 fixes applied (PASS-WITH-CHANGES: 3 FIX, 2 NOTE). Ticket step: existing key IMS-4, TASKS.md line rewritten in place.
