---
type: prompt
plan: "[[2026-11-04-s3-tenancy-enforcement]]"
id: 04
unit: IMS-4
status: todo
model: sonnet
effort: high
depends_on: [03]
parallel_group: D
files:
  create:
    - tests/Inventory.IntegrationTests/SessionContextTests.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
    - tests/Inventory.IntegrationTests/TenantInterceptorTests.cs
  modify: []
  delete: []
---
# 04 · T11, T5 and interceptor tests

## Task
Add three test classes to `tests/Inventory.IntegrationTests`:
- **T11**: a pooled connection never carries the previous tenant.
- **T5**: tenants stay isolated across requests and scopes, through EF with and without the tenant filter, plus a guard that every `/api` route is probed or exempt.
- The `TenantId` interceptor and model checks.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You're on branch `IMS-4`. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`. Files have **no final newline**. The namespace is `Inventory.IntegrationTests`; test classes take `DatabaseFixture db` by primary ctor (copy `tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs`, prompt 03). Use `TestContext.Current.CancellationToken`.

**Available, all `internal`** (`InternalsVisibleTo Inventory.IntegrationTests` exists):
- `DatabaseFixture`:
  - `AppConnectionString` (`inventory_app`) and `CreateApp(params (string, string)[])`.
  - `static CreateTenantWithUserAsync(ApiFactory app, string role) → (Guid TenantId, string Email)`. It provisions `SUP`/`CUS`/`ADJ`.
  - `UserPassword` and `TenantName(Guid)` = `"T-" + id`.
- `ApiFactory(string connectionString, params (string Key, string Value)[] settings)` with `NewClient()` (https, cookies).
- `Kernel.Tenancy.ITenantScopeFactory.BeginScope(Guid) → ITenantScope` (`Services`, `IAsyncDisposable`). A plain `app.Services.CreateAsyncScope()` is a scope with **no tenant**.
- `Inventory.Persistence`:
  - `InventoryDbContext` (`Locations`).
  - `Location` (`required Code`, `required Name`, `required Kind`, `CreatedAt`, `TenantId`).
  - `LocationKind.Physical`.
  - `ITenantOwned`.
  - `TenantSchema.FilterName` (`"Tenant"`) and `TenantSchema.ColumnDefaultSql`.
- `Platform.Persistence.PlatformDbContext` and `Platform.Identity.Roles.Admin`/`.Staff`.
- Auth: `POST /api/auth/login` with `{ email, password }`, then `GET /api/auth/me` → JSON with `tenantName`. Copy the login helper from `tests/Platform.IntegrationTests/AuthTests.cs:176–177`.

**Behaviour under test** (prompts 01–02):
- The session interceptor runs `sp_set_session_context N'TenantId', @t, @read_only = 1` on every `InventoryDbContext` connection open when the scope has a tenant, and does nothing otherwise.
- The query filter `"Tenant"` is `e.TenantId == <scope tenant>`, and **throws `InvalidOperationException`** in a no-tenant scope.
- The SaveChanges interceptor:
  - stamps `TenantId` on added `ITenantOwned` entries that have `Guid.Empty`,
  - throws `InvalidOperationException` for a different tenant or no tenant,
  - throws before any SQL.
- RLS on `inventory.Locations`.

Scalar queries follow `tests/Platform.IntegrationTests/SetupTests.cs:31`: `Database.SqlQuery<T>($"SELECT … AS [Value]").SingleAsync(ct)`.
- The session tenant is `SELECT CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) AS [Value]`, read as `SqlQuery<Guid?>`.
- The SPID is `SELECT @@SPID AS [Value]`, read as `SqlQuery<short>`.

**`SessionContextTests` (T11).** Build a pool of one physical connection: `new ApiFactory(new SqlConnectionStringBuilder(db.AppConnectionString) { MaxPoolSize = 1, ApplicationName = "t11-" + Guid.NewGuid().ToString("N")[..8] }.ConnectionString)`. The unique `ApplicationName` gives each test its own pool. Both DbContexts read `ConnectionStrings:inventory`, so they share it. Dispose each scope before opening the next.
1. `Pooled_connection_never_carries_the_previous_tenant`:
   - Create tenants A and B on that factory.
   - In `BeginScope(A)`, through `InventoryDbContext`, read the SPID, then the session tenant. It's A, and because these are two separate opens in one scope, the second open proves the read-only key was reset and set again.
   - In a no-tenant scope, through `PlatformDbContext`, read the SPID, then the session tenant. It's null.
   - In `BeginScope(B)`, through `InventoryDbContext`, the session tenant is B.
   - All three SPIDs are equal, which proves it's the same physical connection.
2. `Inventory_context_without_a_tenant_sets_no_session_tenant`: in a no-tenant scope, `InventoryDbContext`'s session tenant is null. `SqlQuery` applies no query filter, so it doesn't throw.

**`TenantIsolationTests` (T5).** Spec §11 `:637`: requests alternate A, B, A in one host; each tenant lists every collection; zero foreign rows.
3. `Requests_alternate_tenants_in_one_host`:
   - One `db.CreateApp()`, two tenants with Staff users, two clients each logged in.
   - Call `GET /api/auth/me` as A, then B, then A. Each returns `200` with its own `TenantName`.
4. `Scopes_alternate_tenants_on_one_connection`:
   - On a `MaxPoolSize = 1` factory, as in T11, list `InventoryDbContext.Locations` in `BeginScope` A, then B, then A, with **no** `Where`.
   - Each list has exactly 3 rows, all with its own `TenantId`, with codes `ADJ`, `CUS`, `SUP`.
   - In each of the three scopes, also read `@@SPID` (as in T11), and assert that all three are equal. That proves the test name's "one connection".
5. `Rls_alone_isolates_when_the_tenant_filter_is_ignored`: in `BeginScope(A)`, `Locations.IgnoreQueryFilters([TenantSchema.FilterName]).ToListAsync()` returns only A's 3 rows. With the EF layer off, RLS still holds (spec §4 `:198`). This is the **named** overload; a bare `IgnoreQueryFilters()` is banned.
   - Also assert that the same ignored query's `ToQueryString()` does **not** contain `[TenantId] = @` (case-insensitive), the mirror of test 6. Without it, the test passes even if the filter weren't actually dropped: only RLS may have filtered these rows.
6. `Tenant_filter_is_in_the_generated_sql`: in `BeginScope(A)`, `Locations.ToQueryString()` contains `[TenantId] = @` (case-insensitive is fine).
7. `Query_without_a_tenant_throws`: in a no-tenant scope, `await Should.ThrowAsync<InvalidOperationException>(() => db.Locations.ToListAsync(ct))`.
8. `Every_api_route_is_probed_or_exempt`, the guard (user pick, 2026-11-04):
   - Resolve `app.Services.GetRequiredService<EndpointDataSource>()` (namespace `Microsoft.AspNetCore.Routing`). If it doesn't resolve, use `IEnumerable<EndpointDataSource>` and flatten.
   - Take every `RouteEndpoint` whose `RoutePattern.RawText` starts with `/api`. For each method in its `HttpMethodMetadata`, the key is `"<METHOD> <RawText>"`, for example `POST /api/auth/login`.
   - If `RawText` lacks the `/api/auth` group prefix, build the path from `RoutePattern.PathSegments`, and report it.
   - Assert the set of keys equals exactly `Probed ∪ Exempt.Keys`, failing in both directions, with the difference in the message.
   - Declare at the top of the class:
     ```csharp
     // T5 grows with every slice that adds a route (spec §11): each new /api route gets a probe here
     // (a tenant B route id → 404, a tenant B body id → 400) or an exemption with its reason.
     private static readonly string[] Probed = ["GET /api/auth/me"];   // Requests_alternate_tenants_in_one_host
     private static readonly Dictionary<string, string> Exempt = new()
     {
         ["POST /api/auth/login"] = "anonymous; reads no tenant data",
         ["POST /api/auth/logout"] = "signs out; reads no tenant data",
     };
     ```
   - Non-`/api` endpoints are ignored: `/health`, `/alive`, OpenAPI and Scalar.

**`TenantInterceptorTests`.** Locations are added as `new Location { Code = "I-" + Guid.NewGuid().ToString("N")[..8], Name = "Interceptor", Kind = LocationKind.Physical, CreatedAt = TimeProvider.System.GetUtcNow(), IsActive = true }`, with no `TenantId` unless stated.
9. `Insert_takes_the_scope_tenant`: in `BeginScope(A)`, add and `SaveChangesAsync`. In a new `BeginScope(A)`, the row with that code has `TenantId == A`.
10. `Insert_for_another_tenant_throws_and_saves_nothing`: in `BeginScope(A)`, add with `TenantId = B`. `SaveChangesAsync` throws `InvalidOperationException`. Neither A's nor B's scope has a row with that code.
11. `Insert_without_a_tenant_throws`: in a no-tenant scope, add, and `SaveChangesAsync` throws `InvalidOperationException`.
12. `Every_tenant_entity_has_the_tenant_filter_and_default`:
    - In any scope, take `ctx.GetService<IDesignTimeModel>().Model` (`Microsoft.EntityFrameworkCore.Metadata` / `.Infrastructure`), because the runtime model may drop design-time annotations.
    - For every entity type with a `TenantId` property:
      - the CLR type implements `ITenantOwned`,
      - `FindDeclaredQueryFilter(TenantSchema.FilterName)` is not null,
      - `TenantId`'s `GetDefaultValueSql()` equals `TenantSchema.ColumnDefaultSql`,
      - its `ValueGenerated` is `ValueGenerated.Never`.
    - The set includes `typeof(Location)`.

Tenants for 4–5 and 9–12 come from `DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin)`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Call `IgnoreQueryFilters()` with no names, even in tests. Use `[TenantSchema.FilterName]`.
- Alter the schema or the policy, or connect as `sa`. The container is shared.
- Add packages.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj   # 21/21 (9 from 03 + 12)
dotnet test                                                    # 0 failed
```
- Guard mutation: temporarily remove `"POST /api/auth/logout"` from `Exempt`, and confirm test 8 fails naming it. Then revert.
- Interceptor mutation: back up the file first (`SCRATCH=$(mktemp -d); cp src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs "$SCRATCH/"`). Temporarily remove `TenantSaveChangesInterceptor.Instance` from it, and confirm test 9 fails: the empty `TenantId` hits the RLS BLOCK predicate. Restore it from the backup, and confirm `cmp` against the backup shows no difference. Don't use `git diff`: earlier prompts' output is uncommitted.
