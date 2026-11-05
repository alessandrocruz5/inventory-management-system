---
type: plan
kind: feature
status: audited
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-05
risk: medium
prompts: 9
graph_built: none
tags: [plan]
---
# S4 Master data (IMS-5)

## Request
> IMS-5

This re-plans the existing unit `IMS-5 — S4 Master data · Added · spec: §12 S4 · depends: IMS-4 · model: sonnet` (TASKS.md, Sprint 1). Spec §12 S4: *"Items table (with its RLS predicates) and physical locations: API and screens. Seed adds two branches and about 20 retail items to the demo tenant. Tests: Validation, SKU immutability, deactivation rules · code-guardian review."* The key, the sprint (1), the epic (IMS-1) and the base (`develop`) are kept (workflow §8, "Re-planning legacy units").

| # | Requirement |
|---|---|
| R1 | `inventory.Items` exactly as in spec §5: `TenantId`, `Id`, `Sku nvarchar(64)`, `Name nvarchar(200)`, `BaseUnit nvarchar(16)`, `IsActive` default 1, audit columns, `RowVersion`, PK `(TenantId, Id)`, unique `(TenantId, Sku)`, and a `TenantId` session default. Its creating migration adds the FILTER predicate and both BLOCK predicates to `security.TenantPolicy`. The entity implements `ITenantOwned` and is configured through `ConfigureTenantOwned` (spec §5, §8; IMS-4 Downstream). |
| R2 | Items API (spec §9): `GET /items` (search by SKU or name, `active` filter, `page`/`pageSize` 25/100), `POST /items` (Admin), `GET /items/{id}`, `PUT /items/{id}` (Admin; name, unit and SKU; body carries `version`), `POST /items/{id}/deactivate` (Admin). |
| R3 | Physical locations API (spec §9): `GET /locations` (physical and virtual, virtual flagged `isSystem`), `POST /locations`, `PUT /locations/{id}` and `POST /locations/{id}/deactivate` (Admin; physical only). |
| R4 | Errors are ProblemDetails with stable slugs: `validation` (400, `errors` per field), `not-found` (404, also for another tenant's id) and `concurrency-conflict` (409, a stale `version`). Each new slug is in spec §9 (already), Kernel `ProblemTypes` and the Angular `PROBLEM_MESSAGES` map in this PR (IMS-3 Downstream). |
| R5 | Roles: any signed-in user reads. Staff gets `403` on every Admin route, with no `Location` header and nothing written (spec §3, §13; IMS-3 Downstream). |
| R6 | Tenancy: every new `/api` route has a T5 `[ProbesRoute]` probe. A tenant B route id gets `404`, each tenant lists zero foreign rows, and SKU and location-code uniqueness is per tenant (IMS-4 Downstream). |
| R7 | SKU immutability and the deactivation rules: S4 delivers and tests the no-ledger half. The SKU is editable, deactivation succeeds and is idempotent, and inactive rows are filtered. The blocking half (a moved item, non-zero stock) is locked as a Downstream note for S5a (user decision, below). |
| R8 | Seed: the demo tenant gets two physical branches and 20 retail items. Running the seed twice changes nothing (spec §12 S4, §13). |
| R9 | Screens (spec §10): `/items` has an AG Grid with search, a create/edit dialog and deactivate. `/locations` lists physical locations with create/edit/deactivate and shows virtual ones read-only with a "System" chip. Admin-only buttons are hidden from Staff, and both screens are usable at 375 px. |
| R10 | Done: every `## Commands` line is green, CI `build` and `test` are green, and code-guardian has reviewed (it touches high-stakes paths). |

## Context used
- Spec `docs/Inventory System — Sprint 1 Spec.md`: §5 (`:212-219` the Items DDL; `:280-288` rules: composite keys, session default, reserved virtual codes), §8 (`:487-515` named filter, interceptor, RLS, raw SQL through EF), §9 (`:526-551` routes and roles; `:578-590` slugs), §10 (`:596-623` the Items and Locations screens, conventions, "hide buttons"), §11 (`:629` test isolation: T5 grows per route), §12 (`:669` S4 row).
- Spec audit `docs/Inventory System — Sprint 1 Spec.audit.md:18,23`: toggle-off and deactivation lock balance rows `WITH (UPDLOCK, HOLDLOCK)`, and deactivation requires `OnHand = 0 AND StockValue = 0`. Both land with the ledger and go into the S5a Downstream note.
- TASKS.md Downstream, IMS-3: seed extensions go in another `IDataSeeder` and run as `inventory_app`. Each new slug goes to §9, `ProblemTypes` and `PROBLEM_MESSAGES` in the same PR. "S4 asserts that Staff gets `403` with no `Location` header on an Admin route."
- TASKS.md Downstream, IMS-4: a new tenant table implements `ITenantOwned`, goes through `ConfigureTenantOwned`, and gets `ALTER SECURITY POLICY … ADD` in its creating migration. Each new route needs a T5 probe, and S4 adds `not-found`. The policy is schema-bound. Seeders use `ITenantScopeFactory.BeginScope`.
- TASKS.md Downstream, IMS-2: a module exposes exactly one public type, everything else is `internal`, and modules don't read claims.
- Code: `src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs:22-48` (the `Location` config and `ConfigureTenantOwned` to copy); `Persistence/Location.cs:3-29` (the entity shape); `Persistence/Migrations/20261104152736_TenantPolicy.cs:36-42` (the predicate SQL style); `InventoryModuleExtensions.cs:16-29` (registration; `MapInventoryEndpoints` is a no-op today); `InventoryModule.cs:17-39` (a tenant-scope insert with no `TenantId`).
- Code: `src/Modules/Platform/Platform/Identity/AuthEndpoints.cs:31-96` (the hand-written endpoint, ValidationProblem and Problem pattern); `Identity/Roles.cs:4-9` (internal role names, used at `Tenants/TenantProvisioner.cs:52` and `Seeding/PlatformSeeder.cs:30-33`); `Identity/IdentityRegistration.cs:43-47` (access denied → bare `403`, no redirect); `Seeding/PlatformSeeder.cs:13-15` (fixed tenant ids).
- Code: `src/Api/Program.cs:60-62` (`/api` group with `RequireAuthorization`; modules mapped under it); `src/Api/Setup/SeedCommand.cs:11-14` (seeders run in registration order); `src/Shared/Kernel/Problems/ProblemTypes.cs:4-17`.
- Tests: `tests/Inventory.IntegrationTests/TenantIsolationTests.cs:25-29,123-147` (the T5 route guard: every `/api` route needs a `[ProbesRoute]` or an exemption); `RowLevelSecurityTests.cs:130-221` (the T6 catalog tests pick up any table with a `TenantId` column); `MigrationRollbackTests.cs:33-55` (rolls back to `Initial`, so it runs the new migration's `Down`); `TenantInterceptorTests.cs:90-95` (every entity with `TenantId` implements `ITenantOwned`); `tests/Platform.IntegrationTests/SetupTests.cs:149-197` (seed-twice snapshot: asserts 6 locations and no physical kinds).
- Web: `web/package.json` (Material 22.2.5 is present; no AG Grid); `web/src/app/core/problems/problem-messages.ts:2-6`; `app.routes.ts:4-12`; `app.html:1-10`; `core/auth/auth.service.ts:20` (`isAdmin`); `login/login.ts` and `login/login.spec.ts` (the typed-form and Vitest patterns).
- npm: `ag-grid-community` / `ag-grid-angular` latest is 36.3.0 (published 2026-11-04). 36.2.0 dates from 2026-09-16. Peers: `@angular/core >= 20`.
- Graph: none (`graphify-out/` absent). Blast radius by grep.

## Options
For the guard rules in R7, which need ledger tables that only arrive in S5a:

| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| A. Defer the blocking half to S5a, with a locked Downstream note | none now | — | low: S4 can't violate a rule that has no data to violate it | S5a adds two checks and their tests against real rows | — |
| B. Add an `IStockUsage` seam now: a stub returning "none", plus NSubstitute tests | ~4 files | +½ prompt | low | duplicate tests once S5a has real rows; a speculative abstraction | — |

**Recommendation:** A. **Chosen:** A (user, 2026-11-05).

## Decisions
- **R7 blocking half deferred** (user pick, Option A). See Downstream below.
- **Features layout** (user pick): one folder per endpoint, `Features/<Area>/<Endpoint>/{Endpoint,Handler,Request,Response}.cs`, with only the files the endpoint needs. A response shared by an area's endpoints lives once at `Features/<Area>/<Area>Response.cs`. Classes are prefixed (`CreateItemEndpoint`, `CreateItemHandler`, `CreateItemRequest`), because a class named `Endpoint` collides with `Microsoft.AspNetCore.Http.Endpoint`. Handlers are plain scoped classes registered in `AddInventoryModule`. This sets the pattern for S5c+.
- **AG Grid now** (user pick): `ag-grid-community` and `ag-grid-angular` pinned exactly to `36.2.0` with `--save-exact`. 36.3.0 is a day old, and both packages must match. Both are on CLAUDE.md's allowed list. Items uses the grid. Locations uses a Material list, since it is short and needs chips and per-row actions that work at 375 px. AG Grid is imported only from the lazy `/items` route, so it stays out of the initial bundle (budget 500 kB warn / 1 MB error).
- **One source for role names:** a new `Kernel.Tenancy.RoleNames` (`Admin`, `Staff`), with `Platform.Identity.Roles`' constants aliased to it. The values don't change. The class is named `RoleNames` because `Roles` would be ambiguous in Platform files that import both `Kernel.Tenancy` and `Platform.Identity` (e.g. `TenantProvisioner.cs:1,9`). Inventory authorizes with an `AuthorizationPolicy` that requires `RoleNames.Admin`. That reads roles through ASP.NET's authorization, not claims, so T12 `Modules_do_not_read_claims` holds.
- **One source for seed tenant ids:** a new `Kernel.Hosting.SeedTenants` (`Demo`, `Isolation`, same Guids), with `PlatformSeeder.DemoTenantId`/`IsolationTenantId` aliased to it. Inventory gets its own `InventorySeeder : IDataSeeder`, which seeds branches and items in `BeginScope(SeedTenants.Demo)`. It doesn't depend on Platform: `inventory.*` has no FK to `platform.Tenants`, so seeder order doesn't matter. Platform still registers first. S5d's opening stock goes in this seeder too, because `PostingEngine` is Inventory-internal.
- **`PUT /locations/{id}` doesn't take `allowNegativeStock` in S4.** The toggle needs the §6 lock order and the `StockBalances.AllowNegative` mirror (Hidden coupling). If S4 allowed it, toggles made after S5c would desync the mirror until S10. An unknown JSON property is ignored, and a test proves the flag stays `false`.
- **Validation is hand-written** and returns `TypedResults.ValidationProblem(errors, type: ProblemTypes.Uri(ProblemTypes.Validation))`, copied from `AuthEndpoints.cs:38-52`. .NET 10's built-in `AddValidation()` doesn't set the `type` slug, and nothing else uses it.
- **Duplicate SKU or code:** pre-check with `AnyAsync`, and also catch `DbUpdateException` with an inner `SqlException` 2601/2627 for the race. Both return `400 validation` with `errors.sku` / `errors.code`. A physical location can't take `SUP`/`CUS`/`ADJ`, because the same unique index already holds them.
- **Concurrency:** responses carry `version` (rowversion, base64). `PUT` requires it and sets it as the `RowVersion` original value, so a stale version raises `DbUpdateConcurrencyException` → `409 concurrency-conflict`. Deactivate takes an empty body and no version, because it only sets a flag, idempotently.
- **`GET /items/{id}` returns the item only.** The per-location balances arrive with S8 (spec audit fix (b)).
- **Status codes:** create → `201` with `Location: /api/items/{id}` and the body. Update and deactivate → `200` with the updated body (fresh `version`). Route ids use `{id:guid}`.
- **Paging:** `page` ≥ 1 and `pageSize` 1–100 (default 1/25). Anything out of range → `400 validation`. The response is `{ items, page, pageSize, total }`. Items are ordered by `Sku`. Locations put physical first, then order by `Code`.

**Assumptions** (for `/audit-plan` to challenge):
- SKU and location code are stored trimmed, with their case preserved. Uniqueness is case-insensitive through the database's default `SQL_Latin1_General_CP1_CI_AS` collation (Testcontainers' and Aspire's default). `BaseUnit` is trimmed and lowercased (§5 "free text, lowercase").
- A location's `Code` is editable. The spec has no immutability rule for it, and ledger rows reference `Id`.
- `PUT` and deactivate on a virtual location return `400 validation` with `errors.location = ["System locations can't be changed."]`. They aren't `404`, because the row is visible in `GET /locations`. No new slug is needed.
- `Item.CreatedBy` is `Guid?`, null for seeded rows, mirroring `Location.CreatedBy` (`Location.cs:21-22`).
- There's no reactivate route: spec §9 lists none. A mistaken deactivation is fixed in the database until a later sprint.
- `GET /locations` has no `active` filter and returns inactive rows flagged `isActive: false`. S7's post-stock form can add a filter if it needs one.
- Seed data: branches `QC` "Quezon City branch" and `MKT` "Makati branch", and the 20 generic retail items in prompt 06. The isolation tenant gets none.

**Non-goals:** the `stock-not-zero` slug and its checks (S5a), the negative-stock toggle (S10), balances in `GET /items/{id}` and the item detail screen (S8), reactivation, bulk import, a location hierarchy, `FakeTimeProvider` for audit columns (T10 is S5c), the CI banned-package check (S12), Taglish UI text.

## Downstream (to add to TASKS.md under "locked by IMS-5" when it merges)
- **S5a** (the ledger tables exist; tests insert movements and balances directly as `inventory_app`):
  - `PUT /items/{id}` rejects a SKU change once the item has a `StockMovements` row: `400 validation` with `errors.sku`.
  - `POST /items/{id}/deactivate` and `POST /locations/{id}/deactivate` require `OnHand = 0 AND StockValue = 0` on every physical balance of that item or location. Otherwise they return `422 stock-not-zero` with the blocking balances. Location deactivation locks its balance rows `WITH (UPDLOCK, HOLDLOCK)` through `IX_Balances_Location` (spec audit `:18,:23`).
  - S5a adds `stock-not-zero` to `ProblemTypes` and `PROBLEM_MESSAGES`. The Items and Locations screens already show `problemMessage()` for any error.
- **S8:** `GET /items/{id}` gains the balance per physical location.
- **S10:** `PUT /locations/{id}` gains `allowNegativeStock`, with the §6 lock order and the `StockBalances.AllowNegative` update. The Locations screen gains the switch.
- **S5d:** opening stock goes in `Inventory.Seeding.InventorySeeder`, keyed `seed:<tenant>:<sku>:<location>`.
  - Opening-stock keys use the seed's natural keys, and both `Sku` and `Code` are editable. S5d either keys on the seeded `Sku`/`Code` and skips a pair whose item or location no longer matches by natural key, or switches the seeder to look rows up by a stored marker. Decide this in S5d's plan (audit 2026-11-05).
- **S5c+:** endpoints follow `Features/<Area>/<Endpoint>/{Endpoint,Handler,Request,Response}.cs`, with plain scoped handlers and `Features/Problems.cs` / `Features/Paging.cs` / `Features/InventoryPolicies.cs`.
- **S7:** AG Grid 36.2.0 is installed. Reuse `web/src/app/core/api/paged.ts`, `problemFieldErrors` and `shared/confirm-dialog.ts`.

## Change list
All paths are relative to the repo root. `Inv` = `src/Modules/Inventory/Inventory`.

| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `Inv/Persistence/Item.cs` | C | Item entity, `ITenantOwned` | R1 | 01 |
| `Inv/Persistence/InventoryDbContext.cs` | M | `Items` set and config through `ConfigureTenantOwned` | R1 | 01 |
| `Inv/Persistence/Migrations/<ts>_Items.cs` (+ `.Designer.cs`) | C | Create the table; ALTER the policy in Up, DROP the predicates in Down | R1 | 01 |
| `Inv/Persistence/Migrations/InventoryDbContextModelSnapshot.cs` | M | generated | R1 | 01 |
| `tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs` | M | Catalog tests name `inventory.Items` explicitly | R1 | 01 |
| `tests/Inventory.IntegrationTests/MigrationRollbackTests.cs` | M | Assert `Items` is gone after the rollback and back after re-up | R1 | 01 |
| `src/Shared/Kernel/Tenancy/RoleNames.cs` | C | One source of role names | R5 | 02 |
| `src/Shared/Kernel/Hosting/SeedTenants.cs` | C | One source of the seed tenant ids | R8 | 02 |
| `src/Shared/Kernel/Problems/ProblemTypes.cs` | M | `not-found`, `concurrency-conflict` | R4 | 02 |
| `src/Modules/Platform/Platform/Identity/Roles.cs` | M | Alias `RoleNames` | R5 | 02 |
| `src/Modules/Platform/Platform/Seeding/PlatformSeeder.cs` | M | Alias `SeedTenants` | R8 | 02 |
| `web/src/app/core/problems/problem-messages.ts` | M | The two new slugs (02); `problemFieldErrors` (07) | R4, R9 | 02, 07 |
| `Inv/Features/Paging.cs` | C | Page validation and `PagedResponse<T>` | R2, R3 | 03 |
| `Inv/Features/Problems.cs` | C | `NotFound`, `ConcurrencyConflict`, `Validation`, `IsUniqueViolation` | R4 | 03, 04 |
| `Inv/Features/Items/ItemResponse.cs`, `ItemEndpoints.cs` | C (M in 04) | Shared response and the group mapping | R2 | 03, 04 |
| `Inv/Features/Items/ListItems/{Endpoint,Handler,Request}.cs` | C | `GET /items` | R2 | 03 |
| `Inv/Features/Items/GetItem/{Endpoint,Handler}.cs` | C | `GET /items/{id}` | R2 | 03 |
| `Inv/InventoryModuleExtensions.cs` | M | Handler registrations; `MapInventoryEndpoints` maps the groups; seeder registration | R2, R3, R8 | 03, 04, 05, 06 |
| `tests/Inventory.IntegrationTests/DatabaseFixture.cs` | M | `SignInAsync` helper | R2 | 03 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | M | T5 probes for 9 routes | R6 | 03, 04, 05 |
| `tests/Inventory.IntegrationTests/ItemsTests.cs` | C (M in 04) | Items API tests | R2, R4, R5, R7 | 03, 04 |
| `Inv/Features/InventoryPolicies.cs` | C | Admin policy | R5 | 04 |
| `Inv/Features/Items/ItemRules.cs` | C | Normalize and validate (create and update) | R2, R4 | 04 |
| `Inv/Features/Items/{CreateItem,UpdateItem}/{Endpoint,Handler,Request}.cs`, `DeactivateItem/{Endpoint,Handler}.cs` | C | Write routes | R2, R5, R7 | 04 |
| `Inv/Features/Locations/{LocationResponse,LocationRules,LocationEndpoints}.cs` | C | Shared response, rules and mapping | R3 | 05 |
| `Inv/Features/Locations/{ListLocations,CreateLocation,UpdateLocation}/{Endpoint,Handler,Request}.cs`, `DeactivateLocation/{Endpoint,Handler}.cs` | C | Location routes | R3, R5, R7 | 05 |
| `tests/Inventory.IntegrationTests/LocationsTests.cs` | C | Locations API tests | R3, R4, R5, R7 | 05 |
| `Inv/Seeding/InventorySeeder.cs` | C | Branches and 20 items for the demo tenant | R8 | 06 |
| `tests/Platform.IntegrationTests/SetupTests.cs` | M | Seed-twice counts: 8 locations, 20 items, physical only in demo | R8 | 06 |
| `web/src/app/core/api/paged.ts` | C | `Paged<T>` | R9 | 07 |
| `web/src/app/core/problems/problem-messages.spec.ts` | C | `problemFieldErrors` and the new slugs | R4, R9 | 07 |
| `web/src/app/items/items.service.ts` (+ `.spec.ts`) | C | HTTP for items | R9 | 07 |
| `web/src/app/items/item-dialog.{ts,html}` (+ `.spec.ts`) | C | Create and edit form | R9 | 07 |
| `web/package.json`, `web/package-lock.json` | M | AG Grid 36.2.0 | R9 | 08 |
| `web/src/app/shared/confirm-dialog.ts` | C | Confirm before deactivating (reused by S9/S11) | R9 | 08 |
| `web/src/app/items/items.{ts,html,scss}` (+ `items.spec.ts`) | C | Items page | R9 | 08 |
| `web/src/app/app.routes.ts`, `app.html`, `app.ts`, `app.spec.ts` | M | Routes and nav links (anchors, so `app.spec.ts:42`'s first `button` stays Sign out); the toolbar already wraps at 375 px | R9 | 08, 09 |
| `web/src/app/locations/locations.service.ts` (+ `.spec.ts`) | C | HTTP for locations | R9 | 09 |
| `web/src/app/locations/locations.{ts,html,scss}` (+ `.spec.ts`) | C | Locations page | R9 | 09 |
| `web/src/app/locations/location-dialog.{ts,html}` (+ `.spec.ts`) | C | Create and edit form | R9 | 09 |

Review constraints checked: decimal, n/a (no money or quantity columns). No `UPDATE`/`DELETE` on the ledger, n/a, and no hard deletes: deactivation only, and the migration's `Down` drops only the table it creates. `TenantId` never comes from a request: no request type has it, and the interceptor stamps it. No bare `IgnoreQueryFilters()`. No request carries a date: `CreatedAt`/`UpdatedAt` come from `TimeProvider`. The API runs as `inventory_app`; the existing schema grant covers `Items`, with no new grant. No banned packages; AG Grid is on the allowed list. No §6 change and no `StockMovements`/`StockDocuments` column.

## Blast radius
**Risk:** medium. It's additive, but it touches `**/Migrations/**`, `Inventory/Persistence/**`, `Platform/**/Identity/**` and `Shared/Kernel/**`, and it extends the RLS policy.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `security.TenantPolicy` | Non-graph | `ALTER SECURITY POLICY … ADD` on `inventory.Items` | No, if Up runs after `CreateTable` and Down drops the predicates before `DropTable` (schema-bound: the drop fails otherwise) | Prompt 01 orders it; `MigrationRollbackTests` runs this `Down` |
| `MigrationRollbackTests.TenantPolicy_down_…` | Test | Rolls back to `Initial`, through the new `Items.Down` | Fails if `Down` is wrong | Prompt 01 extends it to assert the table is dropped and re-created |
| T6 `RowLevelSecurityTests` catalog (3 tests) | Test | `TenantTables` now returns `inventory.Items` | Fails without predicates, the default or a `TenantId`-first `UX_Items_Sku` | Prompt 01 adds all three and names Items in the tests |
| `TenantInterceptorTests` entity check | Test | Every entity with `TenantId` implements `ITenantOwned` | No: `Item` implements it | — |
| `InventoryDbContext` | Direct | New entity set and model snapshot | No | `has-pending-model-changes` in the verification |
| `TenantSaveChangesInterceptor` | Direct | Stamps `Item` on add | No: generic over `ITenantOwned` | Tests insert items without `TenantId` |
| T5 route guard `Every_api_route_is_probed_or_exempt` | Test | 9 new `/api` routes | Fails per unprobed route | Each API prompt adds its probes in the same prompt |
| `Platform.Identity.Roles` → `TenantProvisioner.cs:52`, `PlatformSeeder.cs:30-33`, `AuthTests`, `TenantIsolationTests`, `RowLevelSecurityTests` | Direct | Constants aliased, values unchanged | No | The name `RoleNames` avoids an ambiguous `Roles` |
| `PlatformSeeder.DemoTenantId`/`IsolationTenantId` → `SetupTests.cs:154,169` | Direct | Aliased, same Guids | No | — |
| `SetupTests.Seed_twice_changes_nothing` | Test | `InventorySeeder` adds 2 physical locations and 20 items to demo | **Yes**: `Locations.ShouldBe(6)` and `ShouldNotContain(Physical)` fail | Prompt 06 updates the snapshot and assertions in the same prompt |
| `SetupTests.Inventory_app_has_exactly_the_s2_grants` | Test | No grant change | No | — |
| `SeedCommand` | Transitive | Runs the new seeder after `PlatformSeeder` (registration order) | No: no cross-schema FK, and order doesn't matter | — |
| `ProblemTypes` (Kernel) | Shared | Additive constants | No | — |
| Angular `PROBLEM_MESSAGES` | Hidden coupling | Must match the new Kernel slugs | No, if both land together | Prompt 02 changes both |
| T12 `Module_implementations_expose_only_their_registration_class` | Test | Every new Inventory type must be `internal` | Fails on a public type | Every prompt says `internal sealed` |
| T12 `Modules_do_not_read_claims` | Test | The Admin policy uses `RequireRole`, not `System.Security.Claims` types | No | Prompt 04 forbids `ClaimsPrincipal`/`ClaimTypes` |
| T12 `Kernel_has_no_infrastructure_dependencies` | Test | `RoleNames` and `SeedTenants` are plain constants | No | — |
| `Program.cs` `/api` group | Direct | `MapInventoryEndpoints` now maps groups under the authorized `/api` | No | — |
| OpenAPI/Scalar (dev) | Non-graph | 9 new operations | No | — |
| Web initial bundle budget (500 kB warn) | Non-graph | AG Grid | No, while it is imported only by the lazy `/items` route | Prompt 08 forbids importing it from `app.*` |
| `web/package-lock.json` / CI `npm ci` | Non-graph | 2 new exact pins | No | — |
| Angular `problemInterceptor` | Direct | Unchanged; 403s pass through to callers | No | — |

**Known risks in the touched area:**
- A schema-bound policy: changing a `TenantId` column later means dropping its predicates in the same migration (IMS-4 Downstream).
- Rollback order: roll the schema back on the IMS-5 build before reverting the code (IMS-4 Downstream).
- `IsActive.HasDefaultValue(true)` on a `bool` makes `false` EF's sentinel, so an insert with `IsActive = false` would get the database default `true`. Handlers and the seeder always insert `true`, and the existing `Location` config has the same shape.
- Renaming a seeded branch code or SKU makes the next seed re-create the original, because the seeder matches by natural key and the AppHost reruns `seed` on every start. This affects demo data only, and S4 accepts it (audit 2026-11-05).
- Machine prerequisites (S1–S3 Log): `DOTNET_SYSTEM_NET_DISABLEIPV6=1` for restore, and the trusted dev cert plus `SSL_CERT_DIR` for the AppHost run.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-5 | S4 Master data | Added | 01–09 | IMS-4 | yes: `Inventory/Persistence/**`, `**/Migrations/**`, `Platform/**/Identity/**` (code-guardian) | See below |

Acceptance criteria for IMS-5:
- **R1:** the T6 catalog tests list `inventory.Items` with 1 FILTER and 2 BLOCK predicates, a session default and `TenantId`-first keys. `has-pending-model-changes` reports none. `MigrationRollbackTests` shows `Items` dropped on rollback and back on re-up.
- **R2:** `ItemsTests` covers list (search, `active`, paging and its `400`s), get, create (`201` + `Location`, normalized values), update (new `version`, SKU change allowed) and deactivate (`200`, idempotent, hidden by `active=true`).
- **R3:** `LocationsTests` covers: the list holds the 3 virtual locations `isSystem: true` plus created physical ones; create/update/deactivate of a physical location; `400` on a virtual one; `allowNegativeStock` in a PUT body is ignored; `SUP` is refused as a code.
- **R4:** a missing or foreign id → `404 /problems/not-found`; a stale version → `409 /problems/concurrency-conflict`; bad fields → `400 /problems/validation` with `errors` keys; Kernel and Angular both hold the slugs.
- **R5:** Staff gets `403` with no `Location` header, and no row is written, on each of the 6 Admin routes. Staff gets `200` on the 3 read routes.
- **R6:** the T5 route guard passes, with a `[ProbesRoute]` on each of the 9 routes.
- **R7:** the tests show the S4 half of R7. The S5a Downstream note is in the plan (copied to TASKS.md on merge).
- **R8:** `Seed_twice_changes_nothing` passes with demo = 3 virtual + `QC`, `MKT` and 20 items, and isolation = 3 virtual, 0 items.
- **R9:** the Vitest specs pass. On the running stack, `/items` and `/locations` work for Admin and are read-only for Staff, at 375 px.
- **R10:** every `## Commands` line is green, CI `build` and `test` are green, and code-guardian has reviewed.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Items table, RLS predicates, catalog tests | IMS-5 | opus | high | — | A | `Item.cs`, `InventoryDbContext.cs`, `Migrations/*Items*` (2 + snapshot), `RowLevelSecurityTests.cs`, `MigrationRollbackTests.cs` |
| 02 | Shared names: roles, seed tenants, problem slugs | IMS-5 | opus | medium | — | B | `RoleNames.cs`, `SeedTenants.cs`, `ProblemTypes.cs`, `Roles.cs`, `PlatformSeeder.cs`, `problem-messages.ts` |
| 03 | Items read API and HTTP plumbing | IMS-5 | sonnet | high | 01, 02 | C | `Features/{Paging,Problems}.cs`, `Features/Items/{ItemResponse,ItemEndpoints}.cs`, `ListItems/*`, `GetItem/*`, `InventoryModuleExtensions.cs`, `DatabaseFixture.cs`, `TenantIsolationTests.cs`, `ItemsTests.cs` |
| 04 | Items write API and Admin policy | IMS-5 | sonnet | high | 03 | D | `Features/{InventoryPolicies,Problems}.cs`, `Features/Items/ItemRules.cs`, `CreateItem/*`, `UpdateItem/*`, `DeactivateItem/*`, `ItemEndpoints.cs`, `InventoryModuleExtensions.cs`, `TenantIsolationTests.cs`, `ItemsTests.cs` |
| 05 | Locations API | IMS-5 | sonnet | high | 04 | E | `Features/Locations/**`, `InventoryModuleExtensions.cs`, `TenantIsolationTests.cs`, `LocationsTests.cs` |
| 06 | Demo seed: branches and items | IMS-5 | sonnet | medium | 01, 02, 05 | F | `Seeding/InventorySeeder.cs`, `InventoryModuleExtensions.cs`, `tests/Platform.IntegrationTests/SetupTests.cs` |
| 07 | Web: paged type, field errors, items service and dialog | IMS-5 | sonnet | medium | 02, 04 | G | `core/api/paged.ts`, `problem-messages.ts` (+ spec), `items/items.service.ts` (+ spec), `items/item-dialog.*` (+ spec) |
| 08 | Web: AG Grid, Items page, routes and nav | IMS-5 | sonnet | high | 07 | H | `package.json`, `package-lock.json`, `shared/confirm-dialog.ts`, `items/items.*` (+ spec), `app.routes.ts`, `app.html`, `app.ts`, `app.spec.ts` |
| 09 | Web: Locations page | IMS-5 | sonnet | medium | 05, 08 | I | `locations/*` (service, page, dialog, 3 specs), `app.routes.ts`, `app.html`, `app.spec.ts` |

- 01 opus: it's in `**/Migrations/**` and `Inventory/Persistence/**`. It extends a schema-bound RLS policy, and a wrong `Down` order or a missing predicate leaks a tenant's items or breaks rollback.
- 02 opus: it touches `Shared/Kernel/**` (Shared code) and `Platform/**/Identity/**`. The diff is small, but a wrong alias changes who is Admin everywhere.
- 03–05 sonnet/high: `Features/Items|Locations` isn't a high-stakes glob. They follow an inline pattern with exact rules and test cases, and their T5 probes and tests are the evidence code-guardian checks. 03–05 exceed ~5 files only because of the folder-per-endpoint layout the user chose. Each file is a few lines, and each prompt is one area, verified on its own.
- 06–09 sonnet: conventional seed and UI work against a fixed contract. 08 is `high` because AG Grid 36 is new to this zoneless Angular 22 app (module registration, the Theming API).
- Groups A→I run in order: 01–06 share the .NET build and test tree, and 07–09 share `web/` and its lockfile (workflow §5).

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false
cd ..

# R1: the migration's SQL and model sync
SCRATCH=$(mktemp -d)
dotnet ef migrations script TenantPolicy Items --project src/Modules/Inventory/Inventory --startup-project src/Api \
  --context InventoryDbContext -o "$SCRATCH/s4.sql"
grep -n 'CREATE TABLE \[inventory\].\[Items\]' "$SCRATCH/s4.sql"
grep -c 'ADD .*PREDICATE \[security\].\[fn_tenant\](\[TenantId\]) ON \[inventory\].\[Items\]' "$SCRATCH/s4.sql"   # 3
grep -n "SESSION_CONTEXT(N'TenantId')" "$SCRATCH/s4.sql"
grep -n 'UX_Items_Sku' "$SCRATCH/s4.sql"
dotnet ef migrations has-pending-model-changes --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext   # none

# R1/R2/R3/R4/R5/R6/R7: named tests
dotnet test --project tests/Inventory.IntegrationTests
# R8
dotnet test --project tests/Platform.IntegrationTests

# Review constraints and module surface
! grep -rnE --include='*.cs' '\.IgnoreQueryFilters\s*\(\s*\)' src
! grep -rn --include='*.cs' 'TenantId' src/Modules/Inventory/Inventory/Features src/Modules/Inventory/Inventory/Seeding
! grep -rnE --include='*.cs' '^\s*public (sealed |static )?(class|record|interface)' src/Modules/Inventory/Inventory/Features src/Modules/Inventory/Inventory/Seeding
! grep -rnE --include='*.cs' '\b(double|float)\b' src/Modules/Inventory/Inventory/Features
! grep -rn "ag-grid" web/src/app/app.ts web/src/app/app.config.ts
grep -n '"ag-grid-community": "36.2.0"\|"ag-grid-angular": "36.2.0"' web/package.json   # 2 lines

# Full stack on the existing dev volume: migrate applies Items, seed adds branches and items, the API answers
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"
dotnet run --project src/AppHost &      # dashboard: migrate Finished(0), seed Finished(0), api Healthy, web Running
for i in $(seq 90); do [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:4200/api/auth/me)" = 401 ] && break; sleep 2; done
# $SEED_PASSWORD: the AppHost's generated `seed-password` parameter, read into the shell and never echoed
login() { curl -s -c "$SCRATCH/$1" -H 'Content-Type: application/json' -d "{\"email\":\"$1@demo.test\",\"password\":\"$SEED_PASSWORD\"}" http://localhost:4200/api/auth/login -o /dev/null -w '%{http_code}\n'; }
login admin; login staff                                                                  # 200 200
curl -s -b "$SCRATCH/staff" 'http://localhost:4200/api/items?pageSize=100' | grep -o '"total":[0-9]*'   # "total":20 (21 after SMOKE-1 below has run once)
curl -s -b "$SCRATCH/staff" http://localhost:4200/api/locations | grep -o '"code":"[A-Z]*"'             # QC, MKT, SUP, CUS, ADJ (physical first)
curl -s -b "$SCRATCH/staff" -o /dev/null -w '%{http_code}\n' -H 'Content-Type: application/json' \
  -d '{"sku":"SMOKE-1","name":"Smoke","baseUnit":"pc"}' http://localhost:4200/api/items                 # 403
curl -s -b "$SCRATCH/admin" -o /dev/null -w '%{http_code}\n' -H 'Content-Type: application/json' \
  -d '{"sku":"SMOKE-1","name":"Smoke","baseUnit":"pc"}' http://localhost:4200/api/items                 # 201 (400 on a re-run: SKU exists)
# R9 by hand: open http://localhost:4200/items and /locations as admin, then as staff, at 375 px wide (devtools)
kill %1

# R10: after build-unit opens the PR
gh pr checks IMS-5                       # build ✓  test ✓
```

## Rollback
Order matters, as in IMS-4: `Items.Down` exists only in the IMS-5 build.
1. Roll the dev database back first, on the IMS-5 build. As `sa`, run `dotnet ef database update TenantPolicy --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext --connection "<sa connection string>"`. `Down` drops the three Items predicates, then the table. Alternatively, drop the AppHost's SQL volume.
2. Then revert the IMS-5 merge commit on `develop`.
- The seeded `QC` and `MKT` rows stay in `inventory.Locations` (no hard deletes). The IMS-4 code ignores physical locations, so they're harmless.

## Open questions
- None blocking. The Assumptions above are for the audit.

## Log
- 2026-11-05: planned (re-plan of IMS-5 from spec §12 S4; base `develop` @ `51923a16`, clean). User picks: defer the R7 blocking half to S5a; folder per endpoint with only the needed files; AG Grid now.
- 2026-11-05: audited, 2 fixes applied (prompt 08 forbids an animations provider; seed rename risk in Known risks and S5d Downstream). The 5 NOTEs weren't applied.
