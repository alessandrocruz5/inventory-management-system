---
type: prompt
plan: "[[2026-11-04-s2-platform]]"
id: 04
unit: IMS-3
status: done
model: opus
effort: xhigh
depends_on: [03]
parallel_group: D
files:
  create:
    - src/Api/Tenancy/TenantContext.cs
    - src/Api/Tenancy/CurrentUser.cs
    - src/Api/Tenancy/TenantScopeFactory.cs
    - src/Api/Tenancy/TenantMiddleware.cs
    - src/Api/Setup/MigrateCommand.cs
    - src/Api/Setup/DatabasePrincipals.cs
  modify:
    - src/Api/Program.cs
    - src/Api/Api.csproj
  delete: []
---
# 04 · Api tenancy and migrate command

## Task
In `src/Api`, implement the Kernel tenancy abstractions and the tenant middleware, and add the `migrate` run mode. It runs every module migrator, then creates or updates the `inventory_app` SQL login and user and grants it DML on the three schemas.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. Current `src/Api/Program.cs`:
```csharp
using Inventory;
using Platform;
using Scalar.AspNetCore;
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.AddPlatformModule();
builder.AddInventoryModule();
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment()) { app.MapOpenApi(); app.MapScalarApiReference(); }
app.MapDefaultEndpoints();
var api = app.MapGroup("/api");
api.MapPlatformEndpoints();
api.MapInventoryEndpoints();
app.Run();
```
Kernel interfaces (from prompt 01): `Kernel.Tenancy.ITenantContext { bool HasTenant; Guid TenantId }`, `ICurrentUser { bool IsAuthenticated; Guid UserId; string Email; string Role }`, `ITenantScopeFactory { ITenantScope BeginScope(Guid) }`, `ITenantScope : IAsyncDisposable { IServiceProvider Services }`, `TenantClaims.TenantId = "tenant_id"`, `Kernel.Hosting.IModuleMigrator { Task MigrateAsync(CancellationToken) }`.

**Tenancy (`src/Api/Tenancy/`, namespace `Api.Tenancy`, all `internal sealed`):**
- `TenantContext : ITenantContext`, registered scoped:
  - `internal void Set(Guid tenantId)` throws `InvalidOperationException` if already set, or if `tenantId == Guid.Empty`.
  - `TenantId` throws when unset.
  - A context never switches tenant (spec §8: a connection never switches tenant).
- `CurrentUser : ICurrentUser`, registered scoped. It has `internal void Set(Guid userId, string email, string role)`, and its getters throw when unauthenticated.
- `TenantScopeFactory : ITenantScopeFactory`, a singleton over `IServiceScopeFactory`. `BeginScope(id)`:
  - `var scope = scopeFactory.CreateAsyncScope();`
  - `scope.ServiceProvider.GetRequiredService<TenantContext>().Set(id);`
  - Return a small private `ITenantScope` wrapper whose `DisposeAsync` disposes the scope.
- Register the context and user so that the interface and the concrete type resolve to the **same scoped instance**: `AddScoped<TenantContext>()` plus `AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>())`, and the same for `CurrentUser`.
- `TenantMiddleware` (conventional middleware with `InvokeAsync(HttpContext, TenantContext, CurrentUser)`):
  - If `context.User.Identity?.IsAuthenticated != true`, call `next`. Authorization decides later.
  - Otherwise read `TenantClaims.TenantId`. If it's missing or not a valid non-empty Guid, write `401` as ProblemDetails through `IProblemDetailsService` (`Status = 401`, `Title = "Unauthorized"`) and **don't** call `next`.
  - Otherwise `tenant.Set(id)`. For the user, read `ClaimTypes.NameIdentifier` (a Guid), `ClaimTypes.Email` and `ClaimTypes.Role`; if any is missing, return the same `401`. Then `user.Set(...)` and call `next`.
  - Api may read claims. Only modules are barred from it, except in `Platform.Identity` (T12).
  - Never log claim values or request bodies. Log tenant id and user id only, if at all.
- No request body, query string or route ever supplies a tenant id (Review constraint).

**Migrate step (`src/Api/Setup/`, namespace `Api.Setup`, `internal static`):**
- `MigrateCommand.RunAsync(IServiceProvider services, IConfiguration config, CancellationToken ct)`:
  - Create a scope and run `foreach (var m in scope.ServiceProvider.GetServices<IModuleMigrator>()) await m.MigrateAsync(ct);`. Registration order is Platform then Inventory (Program calls `AddPlatformModule` first), and Platform's first migration turns RCSI on.
  - Then `await DatabasePrincipals.EnsureAsync(config.GetConnectionString("inventory")!, config["InventoryApp:Password"], ct)`.
  - If the password is null or empty, throw `InvalidOperationException("InventoryApp:Password is required")` **before** migrating.
- `DatabasePrincipals.EnsureAsync(string adminConnectionString, string password, CancellationToken ct)` uses `Microsoft.Data.SqlClient` (it comes transitively through EF; don't add a PackageReference). This is server-level DDL with no tenant, run only by the migrate step, so it doesn't go through EF.
  - Run one batch with the password as a `SqlParameter` (`@p`, `nvarchar(128)`).
  - The password is **never** concatenated in C#. It's quoted in T-SQL with `QUOTENAME(@p, '''')` inside dynamic SQL:
  ```sql
  DECLARE @q nvarchar(300) = QUOTENAME(@p, '''');
  IF @q IS NULL THROW 50000, 'password too long', 1;
  IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'inventory_app')
      EXEC (N'CREATE LOGIN [inventory_app] WITH PASSWORD = ' + @q + N', CHECK_POLICY = ON;');
  ELSE
      EXEC (N'ALTER LOGIN [inventory_app] WITH PASSWORD = ' + @q + N';');
  IF USER_ID(N'inventory_app') IS NULL
      CREATE USER [inventory_app] FOR LOGIN [inventory_app];
  GRANT SELECT, INSERT, UPDATE ON SCHEMA::[platform]  TO [inventory_app];
  GRANT SELECT, INSERT, UPDATE ON SCHEMA::[identity]  TO [inventory_app];
  GRANT DELETE                 ON SCHEMA::[identity]  TO [inventory_app];
  GRANT SELECT, INSERT, UPDATE ON SCHEMA::[inventory] TO [inventory_app];
  ```
  - These are exactly the spec §8 `:522` grants for S2. S5a adds the sequence grant and the `DENY UPDATE` on the ledger tables.
  - No `ALTER`, `CONTROL`, `db_owner`, `db_datareader` or `db_datawriter`.
  - Running it a second time must succeed and change nothing except the password.
- `src/Api/Api.csproj`: add `<ItemGroup><InternalsVisibleTo Include="Platform.IntegrationTests" /></ItemGroup>`. Prompt 08's tests call `MigrateCommand.RunAsync` and the tenancy types directly.

**Program.cs changes:**
- After `AddInventoryModule()`, add `builder.Services.AddSingleton(TimeProvider.System);` and the tenancy registrations above.
- After `var app = builder.Build();`:
  ```csharp
  if (args is ["migrate", ..])
  {
      await MigrateCommand.RunAsync(app.Services, app.Configuration, CancellationToken.None);
      return;
  }
  ```
  Keep it before any middleware setup, so `app.Run()` never runs in that mode.
- Add `app.UseMiddleware<TenantMiddleware>();` right after `app.UseStatusCodePages();`. Prompt 05 moves it between `UseAuthentication` and `UseAuthorization` once Identity exists. **Don't** add `UseAuthentication`/`UseAuthorization` now: no authentication services are registered yet, and the app would fail at startup.
- The serving API never migrates (spec §4 `:193`). `migrate` runs with the server-admin connection string, and the serving process gets the `inventory_app` one (the AppHost wires this in prompt 07).

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add a PackageReference (SqlClient is transitive).
- Log the password or the connection string.
- Call `Database.Migrate*` here. Only the module migrators do.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                                    # T12 8/8 green

# Manual migrate against a throwaway container (as sa), run twice:
docker run -d --name ims3-mig -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Str0ng!Passw0rd' -p 14333:1433 mcr.microsoft.com/mssql/server:2025-latest
until docker exec ims3-mig /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Str0ng!Passw0rd' -Q 'SELECT 1' >/dev/null 2>&1; do sleep 2; done
export ConnectionStrings__inventory='Server=localhost,14333;Database=inventory;User ID=sa;Password=Str0ng!Passw0rd;TrustServerCertificate=true'
export InventoryApp__Password='App!Passw0rd-123'
dotnet run --project src/Api --no-launch-profile -- migrate && dotnet run --project src/Api --no-launch-profile -- migrate   # both exit 0
docker exec ims3-mig /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Str0ng!Passw0rd' -d inventory -Q "
SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name='inventory';
SELECT name FROM sys.database_principals WHERE name='inventory_app';
SELECT permission_name, SCHEMA_NAME(major_id) FROM sys.database_permissions WHERE grantee_principal_id=USER_ID('inventory_app') AND class=3 ORDER BY 2,1;"
#   -> 1; inventory_app; DELETE/INSERT/SELECT/UPDATE identity; INSERT/SELECT/UPDATE inventory; INSERT/SELECT/UPDATE platform
unset InventoryApp__Password; dotnet run --project src/Api --no-launch-profile -- migrate; echo "exit=$?"   # non-zero, message names InventoryApp:Password
docker rm -f ims3-mig
```
