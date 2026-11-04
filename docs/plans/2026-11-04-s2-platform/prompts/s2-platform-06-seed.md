---
type: prompt
plan: "[[2026-11-04-s2-platform]]"
id: 06
unit: IMS-3
status: done
model: sonnet
effort: high
depends_on: [05]
parallel_group: F
files:
  create:
    - src/Modules/Platform/Platform/Tenants/TenantProvisioner.cs
    - src/Modules/Platform/Platform/Seeding/PlatformSeeder.cs
    - src/Api/Setup/SeedCommand.cs
  modify:
    - src/Modules/Platform/Platform/Platform.csproj
    - src/Modules/Platform/Platform/PlatformModuleExtensions.cs
    - src/Api/Program.cs
  delete: []
---
# 06 · Tenant provisioner and seed command

## Task
Add `TenantProvisioner` (create a tenant, then provision its inventory inside that tenant's scope) and `PlatformSeeder : IDataSeeder` (two fixed tenants, the two roles, four users), plus the `Api seed` run mode. Running the seed twice must change nothing.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. Existing types:
- `Platform.Tenants.Tenant { Guid Id; string Name; DateTimeOffset CreatedAt }`.
- `Platform.Persistence.PlatformDbContext` (or `Platform.Identity` if prompt 02 moved it), `Platform.Identity.AppUser { Guid TenantId }`, and `Platform.Identity.Roles.Admin`/`.Staff`.
- `UserManager<AppUser>` and `RoleManager<IdentityRole<Guid>>` are registered (prompt 05).
- Kernel: `ITenantScopeFactory.BeginScope(Guid) : ITenantScope { IServiceProvider Services }` (IAsyncDisposable), and `Kernel.Hosting.IDataSeeder { Task SeedAsync(CancellationToken) }`.
- `Inventory.Contracts.IInventoryModule.ProvisionTenantAsync(CancellationToken)` takes its tenant from the scope's `ITenantContext`.
- `src/Api/Program.cs` has the `migrate` early return after `builder.Build()`. Copy its shape from `src/Api/Setup/MigrateCommand.cs`.

**`Platform.csproj`:** add `<ProjectReference Include="..\..\Inventory\Inventory.Contracts\Inventory.Contracts.csproj" />`. A module may reference another module's Contracts (TASKS.md Downstream; T12 `ProjectReferenceTests` allows it).

**`Platform.Tenants.TenantProvisioner`** (internal sealed, scoped). Ctor: `(PlatformDbContext db, UserManager<AppUser> users, RoleManager<IdentityRole<Guid>> roles, ITenantScopeFactory scopes, TimeProvider clock)`.
- `Task CreateAsync(Guid id, string name, CancellationToken ct)`:
  1. If `!await db.Tenants.AnyAsync(t => t.Id == id, ct)`, add `new Tenant { Id = id, Name = name, CreatedAt = clock.GetUtcNow() }` and save. The id is explicit here, so EF won't generate one (fixed ids, spec §3 `:127`).
  2. `await using var scope = scopes.BeginScope(id);` then `await scope.Services.GetRequiredService<IInventoryModule>().ProvisionTenantAsync(ct);`. It runs in a **new** scope, so the inventory work uses a new connection acting as that tenant (spec §8 `:512`).
- `Task<AppUser> EnsureUserAsync(Guid tenantId, string email, string role, string password, CancellationToken ct)`:
  - `FindByEmailAsync`. If found, return it unchanged: don't reset the password or roles, so a re-run changes nothing.
  - Otherwise create `new AppUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = tenantId }` with the password, add it to `role`, and return it.
  - If any `IdentityResult` fails, throw `InvalidOperationException` with the error **codes** only (never the password).
- `Task EnsureRolesAsync(CancellationToken ct)`: create `Admin` and `Staff` if missing.

Prompt 08's tests reuse `TenantProvisioner` to create their own tenants (spec §11 test isolation), so keep these three methods exactly.

**`Platform.Seeding.PlatformSeeder : IDataSeeder`** (internal sealed, scoped). Ctor: `(TenantProvisioner provisioner, IConfiguration config)`.
- Constants:
  - `DemoTenantId = new Guid("00000000-0000-0000-0000-00000000d001")`, name `"Demo Retail"`.
  - `IsolationTenantId = new Guid("00000000-0000-0000-0000-00000000d002")`, name `"Isolation Tenant"`.
- `SeedAsync(ct)`:
  - Read `config["Seed:Password"]`; if it's null or empty, throw `InvalidOperationException("Seed:Password is required")`.
  - `EnsureRolesAsync`, then for each tenant `CreateAsync`, then `EnsureUserAsync` for `admin@demo.test` (Admin), `staff@demo.test` (Staff), `admin@isolation.test` (Admin) and `staff@isolation.test` (Staff).
- Later slices extend the seed (S4 items, S5d opening stock): leave it as a straight-line method, with no plugin mechanism.

**`PlatformModuleExtensions.AddPlatformModule`:** `AddScoped<TenantProvisioner>()` and `AddScoped<IDataSeeder, PlatformSeeder>()`. The public surface is unchanged (T12).

**`src/Api/Setup/SeedCommand.cs`** (internal static): `RunAsync(IServiceProvider services, CancellationToken ct)` creates a scope and runs `foreach (var s in scope.ServiceProvider.GetServices<IDataSeeder>()) await s.SeedAsync(ct);`.

**`Program.cs`:** next to the `migrate` branch, add `if (args is ["seed", ..]) { await SeedCommand.RunAsync(app.Services, CancellationToken.None); return; }`.

The seed connects as `inventory_app` (spec §8 `:522`). It needs only `SELECT`/`INSERT`/`UPDATE` on `platform`, `identity` and `inventory`, which prompt 04 grants. If it hits a permission error, report the statement; don't widen the grants.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Seed items, physical locations or stock (S4, S5d).
- Log the seed password or connection strings.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                                  # T12 green
docker run -d --name ims3-seed -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Str0ng!Passw0rd' -p 14335:1433 mcr.microsoft.com/mssql/server:2025-latest
until docker exec ims3-seed /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Str0ng!Passw0rd' -Q 'SELECT 1' >/dev/null 2>&1; do sleep 2; done
ConnectionStrings__inventory='Server=localhost,14335;Database=inventory;User ID=sa;Password=Str0ng!Passw0rd;TrustServerCertificate=true' InventoryApp__Password='App!Passw0rd-123' \
  dotnet run --project src/Api --no-launch-profile -- migrate
for i in 1 2; do
  ConnectionStrings__inventory='Server=localhost,14335;Database=inventory;User ID=inventory_app;Password=App!Passw0rd-123;TrustServerCertificate=true' Seed__Password='Seed!Passw0rd-123' \
    dotnet run --project src/Api --no-launch-profile -- seed || echo "seed $i FAILED"
done
docker exec ims3-seed /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Str0ng!Passw0rd' -d inventory -Q "
SELECT COUNT(*) FROM platform.Tenants; SELECT COUNT(*) FROM [identity].AspNetUsers;
SELECT TenantId, Code, Kind FROM inventory.Locations ORDER BY TenantId, Code;"
#   -> 2; 4; six rows: ADJ/CUS/SUP for ...d001 and ...d002
docker rm -f ims3-seed
```
