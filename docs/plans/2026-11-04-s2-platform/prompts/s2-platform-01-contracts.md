---
type: prompt
plan: "[[2026-11-04-s2-platform]]"
id: 01
unit: IMS-3
status: todo
model: opus
effort: medium
depends_on: []
parallel_group: A
files:
  create:
    - src/Shared/Kernel/Tenancy/ITenantContext.cs
    - src/Shared/Kernel/Tenancy/ICurrentUser.cs
    - src/Shared/Kernel/Tenancy/ITenantScopeFactory.cs
    - src/Shared/Kernel/Tenancy/TenantClaims.cs
    - src/Shared/Kernel/Hosting/IModuleMigrator.cs
    - src/Shared/Kernel/Hosting/IDataSeeder.cs
    - src/Shared/Kernel/Problems/ProblemTypes.cs
    - src/Modules/Inventory/Inventory.Contracts/IInventoryModule.cs
  modify: []
  delete: []
---
# 01 · Kernel and Inventory contracts

## Task
Add the shared abstractions that S2 and every later slice bind to: tenant context, current user, tenant scope factory, migrate/seed hooks, problem slugs, and `IInventoryModule`. Interfaces and constants only. No implementations.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system` (.NET 10, `Directory.Build.props` sets Nullable, TreatWarningsAsErrors, ImplicitUsings, LangVersion latest). Namespaces follow folders: project `Kernel` → `Kernel.Tenancy`, `Kernel.Hosting`, `Kernel.Problems`; project `Inventory.Contracts` → namespace `Inventory.Contracts`.

These are **public** types: Kernel and `*.Contracts` are the only projects other modules may reference. Kernel must not reference any project or package (T12 `Kernel_references_no_project`, `Kernel_has_no_infrastructure_dependencies` bans `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore`, `Microsoft.Data.SqlClient`, `System.Data`). Use only `System.*` types. Don't add a package or FrameworkReference to `Kernel.csproj` or `Inventory.Contracts.csproj`.

Write exactly these shapes (XML doc comments: one short `<summary>` each, matching the spec wording):

```csharp
// Kernel/Tenancy/ITenantContext.cs
public interface ITenantContext
{
    /// True once the request or scope has a tenant.
    bool HasTenant { get; }
    /// The current tenant. Throws InvalidOperationException when HasTenant is false.
    Guid TenantId { get; }
}

// Kernel/Tenancy/ICurrentUser.cs
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }      // throws when not authenticated
    string Email { get; }     // throws when not authenticated
    string Role { get; }      // "Admin" or "Staff"; throws when not authenticated
}

// Kernel/Tenancy/ITenantScopeFactory.cs
public interface ITenantScopeFactory
{
    /// Opens a new DI scope (and so a new DB connection) acting as the tenant. Used outside requests: seed, tests.
    ITenantScope BeginScope(Guid tenantId);
}
public interface ITenantScope : IAsyncDisposable
{
    IServiceProvider Services { get; }
}

// Kernel/Tenancy/TenantClaims.cs
public static class TenantClaims
{
    public const string TenantId = "tenant_id";
}

// Kernel/Hosting/IModuleMigrator.cs — implemented once per module, run only by `Api migrate`
public interface IModuleMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken);
}

// Kernel/Hosting/IDataSeeder.cs — run only by `Api seed`; must be idempotent
public interface IDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}

// Kernel/Problems/ProblemTypes.cs — RFC 9457 `type` is "/problems/<slug>" (spec §9)
public static class ProblemTypes
{
    public const string Prefix = "/problems/";
    public const string Validation = "validation";
    public const string InvalidCredentials = "invalid-credentials";
    public static string Uri(string slug) => Prefix + slug;
}

// Inventory.Contracts/IInventoryModule.cs
public interface IInventoryModule
{
    /// Inserts the virtual locations SUP, CUS and ADJ for the current scope's tenant; does nothing for codes that exist.
    Task ProvisionTenantAsync(CancellationToken cancellationToken);
}
```

The tenant for `ProvisionTenantAsync` comes from the scope's `ITenantContext`, never from a parameter (Review constraint: TenantId never comes from a request). Only these two slugs exist in S2. Later slices add the others from spec §9 together with the Angular map (Hidden coupling).

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here. In particular, no `PostDocument`/`GetBalance` yet.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test        # T12 8/8 green
git diff --stat -- src/Shared/Kernel/Kernel.csproj src/Modules/Inventory/Inventory.Contracts/Inventory.Contracts.csproj   # empty
```
