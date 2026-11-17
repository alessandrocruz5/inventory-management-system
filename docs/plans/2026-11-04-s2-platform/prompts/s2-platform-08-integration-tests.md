---
type: prompt
plan: "[[2026-11-04-s2-platform]]"
id: 08
unit: IMS-3
status: done
model: sonnet
effort: high
depends_on: [07]
parallel_group: H
files:
  create:
    - tests/Platform.IntegrationTests/Platform.IntegrationTests.csproj
    - tests/Platform.IntegrationTests/DatabaseFixture.cs
    - tests/Platform.IntegrationTests/ApiFactory.cs
    - tests/Platform.IntegrationTests/AuthTests.cs
    - tests/Platform.IntegrationTests/SetupTests.cs
    - tests/Platform.IntegrationTests/TenantMiddlewareTests.cs
  modify:
    - InventorySystem.slnx
  delete: []
---
# 08 · Platform integration tests

## Task
Create `tests/Platform.IntegrationTests`, which runs against a real SQL Server 2025 container. It migrates as `sa` through the real migrate command, then hosts the API as `inventory_app`. It proves login, `401`, `me`, lockout, the indistinguishable failure response, the SQL principal split, RCSI, idempotent migrate, provision and seed, and the no-tenant `401`.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. Restore: `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`. Copy the project shape from `tests/Architecture.Tests/Architecture.Tests.csproj`:
- `net10.0`, `OutputType Exe`, `IsPackable false`, `<Using Include="Xunit" />`.
- `xunit.v3` 4.0.2 **only**: no `Microsoft.NET.Test.Sdk`, no `xunit.runner.visualstudio`. MTP comes from `global.json` (TASKS.md Downstream).

Packages (allowed list): `xunit.v3` 4.0.2, `Shouldly` 4.3.0, `Testcontainers.MsSql` 4.16.0, `Microsoft.AspNetCore.Mvc.Testing` 10.0.14. Project references: `src/Api/Api.csproj`, `src/Modules/Platform/Platform/Platform.csproj` and `src/Modules/Inventory/Inventory/Inventory.csproj`. Test projects may reference implementations (T12). Api, Platform and Inventory already grant `InternalsVisibleTo Platform.IntegrationTests`. Add the project to `InventorySystem.slnx` under `/tests/`.

Internal APIs you'll call:
- `Api.Setup.MigrateCommand.RunAsync(IServiceProvider, IConfiguration, CancellationToken)`: reads `ConnectionStrings:inventory` (admin) and `InventoryApp:Password`.
- `Api.Setup.SeedCommand.RunAsync(IServiceProvider, CancellationToken)`: reads `Seed:Password`.
- `Platform.Tenants.TenantProvisioner`: `CreateAsync(Guid id, string name, ct)`, `EnsureRolesAsync(ct)`, `EnsureUserAsync(Guid tenantId, string email, string role, string password, ct)`.
- `Platform.Seeding.PlatformSeeder.DemoTenantId` / `IsolationTenantId`.
- `Platform.Identity.Roles.Admin`/`.Staff`.
- `Kernel.Tenancy.ITenantScopeFactory`, `ITenantContext`.
- `Inventory.Contracts.IInventoryModule`.
- `Inventory.Persistence.InventoryDbContext` (`Locations`).
- `Platform.Persistence.PlatformDbContext` (or `Platform.Identity` if prompt 02 moved it).
- `Api.Tenancy.TenantMiddleware`, `TenantContext`, `CurrentUser`.

**`DatabaseFixture`** (xunit v3 assembly fixture: `[assembly: AssemblyFixture(typeof(DatabaseFixture))]`; implements `IAsyncLifetime`):
- Start `MsSqlBuilder` with image **`mcr.microsoft.com/mssql/server:2025-latest`** (spec §11 `:649`). Use the 4.16.0 constructor or `WithImage`, whichever compiles without an obsolete warning (warnings are errors).
- `AdminConnectionString`: the container's string with `InitialCatalog = "inventory"` (via `SqlConnectionStringBuilder`).
- `AppPassword = "App!Passw0rd-" + Guid.NewGuid().ToString("N")[..8]`.
- `AppConnectionString`: same server, `UserID = "inventory_app"`, `Password = AppPassword`.
- Then migrate: `await using var admin = new ApiFactory(AdminConnectionString, ("InventoryApp:Password", AppPassword));` and `await MigrateCommand.RunAsync(admin.Services, admin.Services.GetRequiredService<IConfiguration>(), ct)`.
  - Starting the factory doesn't migrate (the serving app never does). Only the explicit call does.
- Expose `ApiFactory CreateApp(params (string Key, string Value)[] settings)`, which builds an **inventory_app** factory, plus a helper `Task<(Guid TenantId, string Email)> CreateTenantWithUserAsync(ApiFactory app, string role)`:
  - New tenant id `Guid.NewGuid()`, name `"T-" + id`, email `$"{role.ToLowerInvariant()}-{id:N}@test.local"`, password `UserPassword = "User!Passw0rd-1"`.
  - Calls `EnsureRolesAsync`, `CreateAsync` and `EnsureUserAsync` through `TenantProvisioner` from a scope.
  - Every test creates its own tenant (spec §11 `:629`). There's no database reset.

**`ApiFactory : WebApplicationFactory<Program>`**: ctor `(string connectionString, params (string Key, string Value)[] settings)`. `ConfigureWebHost` → `builder.UseEnvironment("Development")`, `builder.UseSetting("ConnectionStrings:inventory", connectionString)` and each setting via `UseSetting`.
- `HttpClient NewClient()`: `CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true })`. It's `https` because the cookie is `Secure`, and `CookieContainer` won't send it over http.
- If `WebApplicationFactory<Program>` can't see `Program` (it's internal), first check whether .NET 10 generated `public partial class Program`. If not, stop and report. Adding `public partial class Program;` to Api is a decision for the plan's owner.

**`AuthTests`** (each test creates its own tenant and user):
1. `Login_sets_an_httponly_secure_strict_cookie`: `200`, and a `Set-Cookie` that contains `httponly`, `secure` and `samesite=strict` (case-insensitive).
2. `Me_without_a_cookie_is_401_and_not_a_redirect`: `401` and `Headers.Location` is null.
3. `Me_returns_id_email_role_and_tenant_name`: after login as Staff, the JSON has `id` (non-empty Guid), `email`, `role` == `"Staff"` and `tenantName` == the tenant's name.
4. `Login_failures_are_indistinguishable`:
   - (a) an unknown email, (b) a wrong password for a real user, (c) the correct password for a user locked by 5 wrong attempts.
   - All three are `401`, and their bodies are byte-identical **after removing `traceId`**, if present. Parse with `JsonNode` and drop that property.
   - The `type` is `/problems/invalid-credentials`.
5. `Five_failures_lock_the_account`: after 5 wrong passwords, the correct password still gets `401`, and `UserManager.IsLockedOutAsync` is true.
6. `Logout_ends_the_session`: login, then `POST /api/auth/logout` → `204`, then `GET /api/auth/me` → `401`.
7. `Login_rejects_non_json`: `text/plain` body → `415`.
8. `Login_with_empty_fields_is_a_validation_problem`: `400`, `type` `/problems/validation`, with `errors` containing `Email` and `Password` (match case-insensitively).
19. `Cookie_is_8h_sliding`: from an app factory, `Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme)` has `ExpireTimeSpan == TimeSpan.FromHours(8)` and `SlidingExpiration == true`. The login cookie is a session cookie (`isPersistent: false`) with no `Expires`, so R1's 8 h sliding expiry is proven here, not in test 1.

**`SetupTests`:**
9. `Api_connects_as_inventory_app`: from an app factory scope, `PlatformDbContext.Database.SqlQuery<string>($"SELECT SUSER_NAME() AS [Value]").SingleAsync()` == `"inventory_app"`.
10. `Read_committed_snapshot_is_on`: `sys.databases.is_read_committed_snapshot_on` for `inventory` is `1`. Read it on an admin `SqlConnection`.
11. `Migrate_is_idempotent`: running `MigrateCommand.RunAsync` again with the admin factory succeeds. Both contexts then have no pending migrations (`GetPendingMigrationsAsync` is empty).
12. `Inventory_app_has_exactly_the_s2_grants`:
    - Schema-level permissions (`sys.database_permissions`, `class = 3`) for `inventory_app` equal exactly `{identity: DELETE, INSERT, SELECT, UPDATE; inventory: INSERT, SELECT, UPDATE; platform: INSERT, SELECT, UPDATE}`.
    - It belongs to no database role (`sys.database_role_members` is empty for it).
    - It has no database-level `ALTER`/`CONTROL` permission.
13. `ProvisionTenant_is_idempotent`: create a tenant, then in `BeginScope(id)` call `ProvisionTenantAsync` twice more. That tenant's `Locations` are exactly `ADJ`/Adjustment, `CUS`/Customer and `SUP`/Supplier, all `IsActive` and not `AllowNegativeStock`.
14. `BeginScope_acts_as_the_tenant_in_a_fresh_scope`: `ITenantContext.TenantId` in the scope equals the id, and a second `BeginScope` with a different id sees its own id.
15. `Seed_twice_changes_nothing`: use an app factory with `("Seed:Password", "Seed!Passw0rd-1")` and run `SeedCommand.RunAsync` twice. Afterwards:
    - Exactly the two fixed tenants exist with ids `…d001` and `…d002`.
    - The four seed users exist with roles `admin@demo.test`→Admin, `staff@demo.test`→Staff, `admin@isolation.test`→Admin, `staff@isolation.test`→Staff.
    - 3 virtual locations per seed tenant.
    - Snapshot the row counts of `platform.Tenants`, `identity.AspNetUsers` and `inventory.Locations` restricted to the two seed tenant ids before the second run and after it: they're equal.
    - This is the only test that uses the fixed seed ids.

**`TenantMiddlewareTests`** (no DB):
- Build a `ServiceCollection` with `AddProblemDetails()` and logging, and a `DefaultHttpContext { RequestServices = provider }`.
16. `Authenticated_principal_without_tenant_claim_gets_401`:
    - The `User` is `new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Email, "x@test.local"), new Claim(ClaimTypes.Role, "Staff")], "test"))`.
    - `InvokeAsync` sets status `401`, `next` isn't called, and `TenantContext.HasTenant` is false.
17. `Anonymous_request_passes_through`: `next` is called and the status is untouched.
18. `Tenant_claim_fills_the_context`: with a `tenant_id` claim, `HasTenant` is true and the `TenantId` matches.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Mock the database (spec §11: prefer the real database; NSubstitute only where a fake is impossible).
- Disable or skip a test (CLAUDE.md: ask first).
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
SCRATCH=$(mktemp -d)
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test 2>&1 | tee "$SCRATCH/test.txt" | grep -Ei 'total|passed|failed'   # Architecture 8 + Platform.IntegrationTests 19, 0 failed
```
- Temporarily give `api` the admin connection (change `CreateApp` to pass `AdminConnectionString`). Test 9 fails. Revert.
- Temporarily remove `o.Cookie.SameSite = SameSiteMode.Strict` in `IdentityRegistration`. Test 1 fails. Revert.
