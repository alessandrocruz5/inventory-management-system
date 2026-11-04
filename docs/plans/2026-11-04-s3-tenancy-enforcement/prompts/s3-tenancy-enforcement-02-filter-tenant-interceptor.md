---
type: prompt
plan: "[[2026-11-04-s3-tenancy-enforcement]]"
id: 02
unit: IMS-4
status: todo
model: opus
effort: high
depends_on: [01]
parallel_group: B
files:
  create:
    - src/Modules/Inventory/Inventory/Persistence/ITenantOwned.cs
    - src/Modules/Inventory/Inventory/Persistence/TenantSaveChangesInterceptor.cs
  modify:
    - src/Modules/Inventory/Inventory/Persistence/Location.cs
    - src/Modules/Inventory/Inventory/Persistence/TenantSchema.cs
    - src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - src/Modules/Inventory/Inventory/InventoryModule.cs
  delete: []
---
# 02 · Named filter, `TenantId` interceptor, provisioning cleanup

## Task
Add EF Core's layer of tenant isolation:
- The named query filter `"Tenant"` on every tenant-owned entity.
- A `SaveChanges` interceptor that stamps and validates `TenantId` on added entities.

Then remove the explicit tenant code from `InventoryModule.ProvisionTenantAsync`.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You're on branch `IMS-4`. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`. Files have **no final newline**. Usings are grouped with blank lines between groups. Everything is `internal`.

Prompt 01 already landed:
- `InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenant)` with `internal ITenantContext Tenant => tenant;`.
- `TenantSchema.SessionKey` and `TenantSchema.ColumnDefaultSql`.
- In the `Location` config: `location.Property(l => l.TenantId).HasDefaultValueSql(TenantSchema.ColumnDefaultSql).ValueGeneratedNever();`.
- `TenantSessionInterceptor.Instance`, added in `InventoryModuleExtensions` via `.AddInterceptors(...)`.
- The `TenantPolicy` migration (RLS on `inventory.Locations`).

`ITenantContext` (`src/Shared/Kernel/Tenancy/ITenantContext.cs`) has `bool HasTenant` and `Guid TenantId`. `TenantId` throws `InvalidOperationException` when there's no tenant (`src/Api/Tenancy/TenantContext.cs:12`).

**Spec §8 `:487–491`:**
- Every tenant-owned entity gets `HasQueryFilter("Tenant", e => e.TenantId == tenant.TenantId)`.
- `IgnoreQueryFilters()` without names is banned outside tests.
- Inserts set `TenantId` in a `SaveChanges` interceptor from `ITenantContext`, and handlers never set it.

**1. Create `Persistence/ITenantOwned.cs`:**
```csharp
namespace Inventory.Persistence;

/// <summary>A row owned by one tenant: it gets the "Tenant" query filter, the session default and RLS (spec §8).</summary>
internal interface ITenantOwned
{
    Guid TenantId { get; set; }
}
```

**2.** Make `Location` (`Persistence/Location.cs:3`) implement `ITenantOwned`. Its `public Guid TenantId { get; set; }` already matches.

**3.** In `TenantSchema.cs`, add `/// <summary>The named query filter on every tenant-owned entity.</summary> public const string FilterName = "Tenant";`.

**4. Modify `InventoryDbContext.cs`:**
- Add `private Guid CurrentTenantId => tenant.TenantId;`. EF evaluates DbContext members in a filter per query, against this context instance. With no tenant it throws, so a tenant query from a no-tenant scope fails loudly. That's intended (plan Decisions).
- Add a private instance helper, and call `ConfigureTenantOwned(location);` from the `Location` config:
  ```csharp
  private void ConfigureTenantOwned<T>(EntityTypeBuilder<T> entity) where T : class, ITenantOwned
  {
      entity.HasQueryFilter(TenantSchema.FilterName, e => e.TenantId == CurrentTenantId);
      entity.Property(e => e.TenantId).HasDefaultValueSql(TenantSchema.ColumnDefaultSql).ValueGeneratedNever();
  }
  ```
- Remove the separate `TenantId` property line that 01 added, because the helper now owns it. The model must stay identical (`has-pending-model-changes` reports none).
- If EF can't translate the generic `e.TenantId` (an interface-member access), use `EF.Property<Guid>(e, nameof(ITenantOwned.TenantId)) == CurrentTenantId` in the helper instead, and report it.

**5. Create `Persistence/TenantSaveChangesInterceptor.cs`**, a stateless singleton like `TenantSessionInterceptor`:
```csharp
internal sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly TenantSaveChangesInterceptor Instance = new();
    // SavingChanges and SavingChangesAsync both call Stamp(eventData.Context) and return the result unchanged.
}
```
`Stamp(DbContext? context)`:
- If `context` isn't an `InventoryDbContext`, return.
- For each `db.ChangeTracker.Entries<ITenantOwned>()` with `State == EntityState.Added`:
  - If `!db.Tenant.HasTenant`, throw `InvalidOperationException("Tenant-owned rows can only be added inside a tenant scope.")`.
  - If `TenantId == Guid.Empty`, set it through `entry.Property(e => e.TenantId).CurrentValue = db.Tenant.TenantId`. Use the change tracker, not the CLR setter, so the key fix-up happens.
  - If it's any other value `!= db.Tenant.TenantId`, throw `InvalidOperationException("A tenant-owned row was added for another tenant.")`. Never overwrite.
- `Modified` entries need no check: `TenantId` is part of the PK, so EF refuses to change it.
- It throws before any SQL runs, so nothing is saved.
- EF allows changing a key value on an `Added` entry **\[Likely\]**. If it throws instead, stop and report the exact exception; don't work around it.

**6.** In `InventoryModuleExtensions.cs`, add `TenantSaveChangesInterceptor.Instance` to the existing `.AddInterceptors(...)` call, next to `TenantSessionInterceptor.Instance`.

**7. Modify `InventoryModule.cs`** (`:10–45`):
- Remove the `ITenantContext tenant` primary-ctor parameter, `var tenantId = tenant.TenantId;`, the `// S3:` comment, `l.TenantId == tenantId &&` in the `Where`, and `TenantId = tenantId,` in the initializer.
- Remove `using Kernel.Tenancy;` if it becomes unused.
- Keep the rest byte-for-byte: the `VirtualLocations` array, the idempotent "add missing codes" loop, `clock.GetUtcNow()` and `CreatedBy = null`.
- The query becomes `db.Locations.Where(l => l.Kind != LocationKind.Physical).Select(l => l.Code)`. The filter and RLS scope it to the tenant.

**Review constraints that apply:**
- `TenantId` never comes from a request.
- No bare `IgnoreQueryFilters()` anywhere in `src`.
- Module internals stay `internal`. T12 checks that `Inventory` exposes only `InventoryModuleExtensions`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add a migration. Query filters aren't in the migrations model; if `has-pending-model-changes` reports a change, you altered the model, so revert it.
- Move `ITenantOwned` or the interceptors to Kernel, or make them public.
- Change `TenantContext`, `TenantMiddleware`, `TenantScopeFactory` or any test.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet ef migrations has-pending-model-changes --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext   # no changes
! grep -n 'TenantId\|ITenantContext' src/Modules/Inventory/Inventory/InventoryModule.cs
! grep -rnE --include='*.cs' '\.IgnoreQueryFilters\s*\(\s*\)' src
dotnet test                                                    # Architecture 10/10, Platform.IntegrationTests 19/19
```
- `SetupTests.ProvisionTenant_is_idempotent` and `Seed_twice_changes_nothing` pass with no explicit `TenantId` in `ProvisionTenantAsync`. That proves the interceptor stamps the tenant and the filter scopes the existence check.
