---
type: prompt
plan: "[[2026-11-17-s14-azure]]"
id: 01
unit: IMS-20
status: todo
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Platform/Platform/Persistence/Migrations/<timestamp>_AddDataProtectionKeys.cs
    - src/Modules/Platform/Platform/Persistence/Migrations/<timestamp>_AddDataProtectionKeys.Designer.cs
  modify:
    - src/Modules/Platform/Platform/Platform.csproj
    - src/Modules/Platform/Platform/Persistence/PlatformDbContext.cs
    - src/Modules/Platform/Platform/Persistence/Migrations/PlatformDbContextModelSnapshot.cs
    - src/Modules/Platform/Platform/Identity/IdentityRegistration.cs
    - tests/Platform.IntegrationTests/AuthTests.cs
  delete: []
---
# 01 · Data Protection keys in the database

## Task
Persist ASP.NET Core Data Protection keys in the Platform database, so an auth cookie stays valid across API restarts, scale-to-zero and multiple hosts:
1. Add the package `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.14 to Platform.
2. Make `PlatformDbContext` an `IDataProtectionKeyContext`, with its table at `platform.DataProtectionKeys`.
3. Register `AddDataProtection().SetApplicationName("inventory").PersistKeysToDbContext<PlatformDbContext>()`.
4. Generate the migration `AddDataProtectionKeys`.
5. Add an integration test that proves the keys live in the database and a second host accepts the first host's cookie.

The README's compose "Known limits" line and CLAUDE.md's allowed-package list are updated by prompt 02, in the same unit. Don't touch them here.

## Context
- **Repo and branch:** the repo root is `git rev-parse --show-toplevel`. Work on the branch you were given, and stop if `git branch --show-current` isn't it. Restore on this machine needs `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`.
- **Why:** the API runs on Azure Container Apps with min replicas 0 (plan `2026-11-17-s14-azure`, R5, D3). Today Data Protection isn't configured anywhere (`grep -rn DataProtection src` is empty), so a container keeps its keys in its own filesystem, and every new container signs every user out. The same is true in compose (README `## Docker Compose`, "Known limits").
- **Package (user-approved, D3):** `<PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="10.0.14" />`. Put it in `src/Modules/Platform/Platform/Platform.csproj`'s package `ItemGroup`, in alphabetical order (after `Aspire.Microsoft.EntityFrameworkCore.SqlServer`, before `Microsoft.AspNetCore.Identity.EntityFrameworkCore`). 10.0.14 matches the other ASP.NET/EF packages. Don't take 10.0.15.
- **`src/Modules/Platform/Platform/Persistence/PlatformDbContext.cs`** today:
  ```csharp
  internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
      : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
  {
      public DbSet<Tenant> Tenants => Set<Tenant>();
      protected override void OnModelCreating(ModelBuilder modelBuilder)
      {
          base.OnModelCreating(modelBuilder);
          modelBuilder.HasDefaultSchema("identity");
          modelBuilder.Entity<Tenant>(tenant => { tenant.ToTable("Tenants", "platform"); ... });
          ...
  ```
  - Add `, IDataProtectionKeyContext` (namespace `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`).
  - Add the property `public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();` (the interface requires a `DbSet<DataProtectionKey> DataProtectionKeys { get; }`), in the same style as `Tenants`.
  - In `OnModelCreating`, add `modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys", "platform");`. It must not land in the default `identity` schema: `inventory_app` has `DELETE` there, and the key store needs only `SELECT, INSERT`, which the existing `platform` schema grant gives (`src/Api/Setup/DatabasePrincipals.cs:24`).
  - This table has no `TenantId`: it is server-wide, like `identity.*`. It stays outside `security.TenantPolicy` and outside T6, which only checks the `inventory` schema.
- **`src/Modules/Platform/Platform/Identity/IdentityRegistration.cs`**: `AddPlatformIdentity(this IHostApplicationBuilder builder)` registers Identity, then `AddAuthentication(...).AddIdentityCookies()` and `ConfigureApplicationCookie(...)` (around lines 28-40). Right before `AddAuthentication`, add:
  ```csharp
  // Keys live in the database, so a cookie survives restarts, scale-to-zero and more than one host (S14).
  builder.Services.AddDataProtection()
      .SetApplicationName("inventory")
      .PersistKeysToDbContext<PlatformDbContext>();
  ```
  - `using Microsoft.AspNetCore.DataProtection;` is needed. `Platform.Persistence` is already imported.
  - Don't touch the cookie options. Every option there is pinned by `AuthTests` and spec §8.
  - T12 allows `System.Security.Claims` only in `Platform.Identity`. This change adds no Claims use.
  - `PlatformDbContext` has no tenant interceptor, so Data Protection can resolve it from its own scope with no tenant.
- **Migration** (the exact command used for this context before):
  ```bash
  dotnet tool restore
  dotnet ef migrations add AddDataProtectionKeys --project src/Modules/Platform/Platform --startup-project src/Api --context PlatformDbContext --output-dir Persistence/Migrations
  ```
  - Check the generated `Up`: it creates `platform.DataProtectionKeys` (`Id int IDENTITY` PK, `FriendlyName nvarchar(max) NULL`, `Xml nvarchar(max) NULL`) and nothing else.
  - If it touches any other table, stop and report.
  - No float columns, because `tests/Inventory.IntegrationTests/LedgerSchemaTests.cs:183` checks `platform`.
  - Don't hand-edit the migration. Format it with `dotnet format` if needed.
- **Test**: add to `tests/Platform.IntegrationTests/AuthTests.cs` (class `AuthTests(DatabaseFixture db)`), a `[Fact] public async Task Sign_in_keys_persist_in_the_database()`.
  - **Config checks.** Use `await using var a = db.CreateApp();`, then:
    - `a.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository` is an `EntityFrameworkCoreXmlRepository<PlatformDbContext>`; use `ShouldBeOfType`.
    - `a.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator` is `"inventory"`.
    - Both checks matter. On Linux, Data Protection falls back to `~/.aspnet/DataProtection-Keys`, which test hosts already share, so the cookie check alone proves nothing.
  - **Cookie checks.**
    - Create a Staff user with `DatabaseFixture.CreateTenantWithUserAsync(a, Roles.Staff)`.
    - Sign in through `a.NewClient()` with the class's `Login(client, email, DatabaseFixture.UserPassword)` helper (`AuthTests.cs:176`), and assert 200.
    - Take the `Set-Cookie` value for `.AspNetCore.Identity.Application`; the name/value pair is the part before the first `;`.
  - **Key row.** Count the rows in `platform.DataProtectionKeys` as the admin: `new SqlConnection(db.AdminConnectionString)`, the same pattern as `SetupTests.cs:42-50`. Assert `>= 1`.
  - **Second host.**
    - Start a second host, `await using var b = db.CreateApp();`. Make a client with `b.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false })`.
    - Send `GET /api/auth/me` with the header `Cookie: <name>=<value>` and assert 200.
  - Use `TestContext.Current.CancellationToken`, and Shouldly, never FluentAssertions.
- **Review constraints:** no banned packages, and nothing outside the allowed list beyond this approved one. No hard deletes. The API connects as `inventory_app`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change the cookie options, `DatabasePrincipals`, the grants, or any Inventory code.
- Protect the keys with a certificate or Key Vault (non-goal).
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet ef migrations has-pending-model-changes --project src/Modules/Platform/Platform --startup-project src/Api --context PlatformDbContext
dotnet test --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false && cd ..
bash .github/scripts/check-banned-packages.sh
```
- `Sign_in_keys_persist_in_the_database` passes; `SetupTests.Migrate_is_idempotent` and `Inventory_app_has_exactly_its_grants` pass unchanged.
- `git diff --stat` lists only the `files` above.
