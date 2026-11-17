# Audit: 2026-11-04-s3-tenancy-enforcement

## Audit 2026-11-04: PASS-WITH-CHANGES
Graph built: none (`graphify-out/` absent; grep + file reads) · Symbols traversed: 14 · Missed blast-radius items: 1 (non-breaking)

**Independent blast radius.** These symbols and paths were traced by grep before reading the planner's section: `InventoryDbContext` (ctor, DI, design-time factory), `InventoryModule`/`IInventoryModule`, `TenantProvisioner.CreateAsync`, `PlatformSeeder`, `SeedCommand`, `MigrateCommand`, `InventoryMigrator`, `TenantContext`/`TenantScopeFactory`, `AddInventoryModule` + `EnrichSqlServerDbContext`, `SetupTests` (all 7 facts), `ModuleBoundaryTests`, `ProjectReferenceTests`, `MigrationCallTests`, `ci.yml`, `InventorySystem.slnx`, the three `InternalsVisibleTo` csproj lines, `AuthEndpoints` routes and `PlatformDbContext` `TenantId` columns.
- Every item the planner lists matches. Its "won't break" claims hold:
  - `Snapshot` is the only no-tenant `Locations` read (`SetupTests.cs:201–207`). The other reads use `BeginScope` (`:121`, `:189`).
  - The only `TenantId` column outside `inventory` is `identity.AspNetUsers` (`PlatformDbContext.cs:18,30`), which T6 excludes.
  - `platform.Tenants` has no `TenantId`.
  - The API has 10 architecture tests and 19 Platform integration tests.
- **Missed (non-breaking):** Aspire's `EnrichSqlServerDbContext` health check runs `CanConnectAsync` on `InventoryDbContext` in a no-tenant scope (`/health`, `src/ServiceDefaults/Extensions.cs`). The session interceptor does nothing there, and no filter is evaluated, so nothing breaks. It needs no change.
- **API assumptions verified** against the EF Core 10.0.14 XML docs in `~/.nuget/packages`:
  - `HasQueryFilter(string, Expression)` and `IgnoreQueryFilters(IQueryable, IReadOnlyCollection<string>)`.
  - `IReadOnlyEntityType.FindDeclaredQueryFilter(string)`.
  - `DbConnectionInterceptor.ConnectionOpened` and `ConnectionOpenedAsync(DbConnection, ConnectionEndEventData, …)`.
  - The `dotnet-ef` 10.0.14 tool, in `.config/dotnet-tools.json`.

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|
| FIX | Usability | **The Rollback runs in the wrong order.** Reverting the merge first leaves `security.TenantPolicy` ON with no session interceptor, so every `inventory_app` read returns empty and every insert hits the BLOCK predicate. The schema rollback then can't run: `dotnet ef database update Initial` needs `TenantPolicy.Down`, which lives only in the reverted migration file. | Plan `:311–313`: the revert is listed first, then `database update Initial`. `Down` exists only in prompt 01's new `<ts>_TenantPolicy.cs`. | Reorder the steps. (1) On the IMS-4 build, as `sa`, run `dotnet ef database update Initial …`, or drop the AppHost SQL volume. (2) Then revert the merge. Add one line on why: RLS without the interceptor blocks all inventory writes. |
| FIX | Requirements (R7) | **T5 test 5 can pass vacuously.** `Rls_alone_isolates_when_the_tenant_filter_is_ignored` asserts only that A sees its own 3 rows. The EF filter alone gives the same result, so the test passes even if `IgnoreQueryFilters([FilterName])` didn't remove the filter. It doesn't prove the "RLS alone" claim (spec §4 `:198`, §11 "each proves a claim a reviewer will probe"). | Prompt 04, test 5, compared with test 6 (`ToQueryString()` contains `[TenantId] = @`). | In test 5, also assert that the ignored query's `ToQueryString()` does **not** contain `[TenantId] = @`. Then the rows that come back can only have been filtered by RLS. |
| FIX | Usability | **The AppHost block in the end-to-end verification won't run as written.** `dotnet run --project src/AppHost &` is followed immediately by `curl`, with no readiness wait, which contradicts S2's decision to poll instead of using fixed sleeps. `$SEED_PASSWORD` has no stated source: S2 noted it came from the dashboard, and S3 dropped that note. | Plan `:298–304`. `2026-11-04-s2-platform.md:129` (polling decision) and `:327` (SEED_PASSWORD source). | Add a bounded poll before the login, until `GET /api/auth/me` through `:4200` returns `401`, with a timeout of about 180 s that fails. Add a comment that `SEED_PASSWORD` is the AppHost's `seed-password` parameter, read from the dashboard or user-secrets into the shell and never echoed. |
| NOTE | Usability (R10) | The test counts don't agree. The end-to-end verification says Inventory.IntegrationTests "≥ 20", and prompt 04 says 21/21. | Plan `:277`; prompt 04, Done when. | State exact counts: Architecture 11, Platform 19, Inventory 21. |
| NOTE | Requirements (R7) | T5 test 4, `Scopes_alternate_tenants_on_one_connection`, claims "one connection" but never asserts it. `MaxPoolSize = 1` only implies it. | Prompt 04, test 4. | Read `@@SPID` in each of the three scopes and assert they're equal, as T11 does. |

**Checks with no finding:**
- **Breakage:**
  - `SetupTests.Snapshot` is fixed in 01, before the filter that would make it throw.
  - The design-time factory gets `NoTenant`.
  - The migrate step as `sa` with no tenant reads no tenant rows.
  - The seed path goes through `BeginScope`.
  - The grants are unchanged.
- **Review constraints:**
  - No bare `IgnoreQueryFilters()`; tests use the named overload.
  - `TenantId` is set only from `ITenantContext`.
  - The API connects as `inventory_app`.
  - No packages are added.
  - Section 6 and the ledger tables are untouched.
- **Prompt independence:**
  - 01 and 02 share files but run in sequential groups (A, B).
  - 05 has its own group, E.
  - Every implied file touch is declared.
- **Unit shape:**
  - One unit, IMS-4, is required by "one slice per PR".
  - It's flagged high-stakes, which matches `Inventory/Persistence/**` and `**/Migrations/**`.
  - Each prompt leaves the repo green. 01 ships RLS together with the session interceptor; 02 adds the filter and stamping.
- **Optimization:** the extra `sp_set_session_context` round trip on each open is spec-mandated and already noted in the plan.

**Bloat estimate:** planned ~25 files / ~900 LOC (~230 src incl. migration, ~670 tests); minimal viable ~24 / ~860 (only the model-check test 12 overlaps T6's catalog, and it covers the EF side, so keep it). Proportionate.
**Model cost:** no change. 01/02 opus (high-stakes `Persistence/**` and `Migrations/**`, RLS and key stamping); 03/04 sonnet/high (exact cases given inline); 05 haiku (mechanical copy of `MigrationCallTests`).