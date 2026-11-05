---
type: prompt
plan: "[[2026-11-04-s3-tenancy-enforcement]]"
id: 01
unit: IMS-4
status: done
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Persistence/TenantSchema.cs
    - src/Modules/Inventory/Inventory/Persistence/TenantSessionInterceptor.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_TenantPolicy.cs (generated, then edited)
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_TenantPolicy.Designer.cs (generated)
  modify:
    - src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs
    - src/Modules/Inventory/Inventory/Persistence/InventoryDesignTimeFactory.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/InventoryDbContextModelSnapshot.cs (generated)
    - tests/Platform.IntegrationTests/SetupTests.cs
  delete: []
---
# 01 · Column default, RLS migration, session-context interceptor

## Task
Add SQL Server row-level security over `inventory.Locations`, plus the connection interceptor that sets the tenant's session context, and the `TenantId` column default. Ship all three together: RLS without the interceptor blocks every insert. Then fix the one S2 test that reads `Locations` from a scope with no tenant.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You're on branch `IMS-4`; don't create or switch branches. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any restore or build. Files have **no final newline** (`.editorconfig` `insert_final_newline = false`). Usings are grouped with a blank line between groups, as in the existing files. Everything is `internal`.

**Spec (`docs/Inventory System — Sprint 1 Spec.md` §8 `:494–515`, §5 `:283`):**
- Every `TenantId` column defaults to `CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)`.
- A `DbConnectionInterceptor` runs `EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1` on every connection open.
- With no session context set, the predicate matches nothing.
- Identity tables stay outside the policy.
- Every migration that creates a tenant table adds its FILTER and both BLOCK predicates. S2 created `Locations` without them, and this migration adds them now.

**1. Create `Persistence/TenantSchema.cs`:**
```csharp
namespace Inventory.Persistence;

/// <summary>Names shared by the tenant column default, the session-context interceptor and the RLS policy (spec §8).</summary>
internal static class TenantSchema
{
    /// <summary>The <c>SESSION_CONTEXT</c> key that <c>security.fn_tenant</c> reads.</summary>
    public const string SessionKey = "TenantId";

    /// <summary>Every tenant table's <c>TenantId</c> default, so raw-SQL inserts get the session's tenant.</summary>
    public const string ColumnDefaultSql = "CAST(SESSION_CONTEXT(N'" + SessionKey + "') AS uniqueidentifier)";
}
```

**2. Modify `Persistence/InventoryDbContext.cs`** (currently `:5` is `internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)`):
- Change the primary constructor to `(DbContextOptions<InventoryDbContext> options, ITenantContext tenant)` (`using Kernel.Tenancy;`).
- Add `/// <summary>The scope's tenant. Interceptors read it each time they run, so no interceptor holds a tenant (spec §8).</summary> internal ITenantContext Tenant => tenant;`
- Inside the `Location` config, add `location.Property(l => l.TenantId).HasDefaultValueSql(TenantSchema.ColumnDefaultSql).ValueGeneratedNever();`.
  - `ValueGeneratedNever` keeps EF always sending the value. Prompt 02's interceptor sets it, and the default serves raw SQL only.
  - Change nothing else in the model.

**3. Modify `Persistence/InventoryDesignTimeFactory.cs`:** pass a no-tenant `ITenantContext` as the second ctor argument. Add a private nested `sealed class NoTenant : ITenantContext`, where `HasTenant` is `false` and `TenantId` throws `InvalidOperationException("Design time has no tenant.")`. Inventory can't reference Api's `TenantContext`.

**4. Create `Persistence/TenantSessionInterceptor.cs`**, a stateless singleton:
```csharp
internal sealed class TenantSessionInterceptor : DbConnectionInterceptor
{
    public static readonly TenantSessionInterceptor Instance = new();
    // ConnectionOpened → if the context is an InventoryDbContext whose Tenant.HasTenant, run the command synchronously.
    // ConnectionOpenedAsync → same, async, honouring the CancellationToken.
}
```
- SQL: `"EXEC sp_set_session_context @key = N'" + TenantSchema.SessionKey + "', @value = @t, @read_only = 1;"`.
- Build the command with `connection.CreateCommand()`. Add a parameter via `command.CreateParameter()` with `ParameterName = "@t"`, `DbType = DbType.Guid` and `Value = tenantId`, then dispose the command.
- Get the tenant only through `eventData.Context is InventoryDbContext { Tenant.HasTenant: true } db`. With no context or no tenant, do nothing. That's the `Api migrate` case: `MigrateCommand` (`src/Api/Setup/MigrateCommand.cs:22–29`) runs migrators as `sa` in a scope with no tenant. RLS returns nothing in that case, and the migration reads no tenant rows.
- Never cache a tenant in a field.
- `ConnectionEndEventData` derives from `ConnectionEventData`, which carries `Context` (EF Core 10.0.14 Relational).

**5. Modify `InventoryModuleExtensions.cs`** (`:19–21`). Chain `.AddInterceptors(TenantSessionInterceptor.Instance)` onto the options builder inside the existing `AddDbContext<InventoryDbContext>(o => o.UseSqlServer(...))` lambda. Keep `AddDbContext` + `EnrichSqlServerDbContext` exactly as is: never pooled (spec §8 `:492`).

**6. Generate the migration** (the dotnet-ef 10.0.14 local tool is in `.config/dotnet-tools.json`):
```bash
dotnet tool restore
dotnet ef migrations add TenantPolicy --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext --output-dir Persistence/Migrations
```
- EF should generate one `AlterColumn` on `Locations.TenantId` that adds the default.
- Run `dotnet ef migrations script Initial TenantPolicy … -o "$SCRATCH/s3.sql"` (see Done when).
- **If the script contains `ALTER COLUMN [TenantId]`**, which fails on a PK column, replace the generated `AlterColumn` with hand-written SQL:
  - In `Up`: `migrationBuilder.Sql("ALTER TABLE [inventory].[Locations] ADD CONSTRAINT [DF_Locations_TenantId] DEFAULT (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)) FOR [TenantId];")`.
  - In `Down`: `ALTER TABLE [inventory].[Locations] DROP CONSTRAINT [DF_Locations_TenantId];`.
  - Keep the generated Designer and snapshot.
  - Otherwise, keep EF's op.

Then **append to `Up`**, after the default:
```csharp
migrationBuilder.EnsureSchema(name: "security");

migrationBuilder.Sql("""
    CREATE FUNCTION [security].[fn_tenant](@TenantId uniqueidentifier)
    RETURNS TABLE
    WITH SCHEMABINDING
    AS
    RETURN SELECT 1 AS ok
    WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier);
    """);

// Every later tenant table adds its three predicates with ALTER SECURITY POLICY in the migration that creates it.
migrationBuilder.Sql("""
    CREATE SECURITY POLICY [security].[TenantPolicy]
        ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Locations],
        ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Locations] AFTER INSERT,
        ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Locations] AFTER UPDATE
        WITH (STATE = ON, SCHEMABINDING = ON);
    """);
```
**Prepend to `Down`**, before the default's revert: `DROP SECURITY POLICY [security].[TenantPolicy];`, then `DROP FUNCTION [security].[fn_tenant];` (each its own `migrationBuilder.Sql`), then `migrationBuilder.DropSchema(name: "security");`.
- `CREATE FUNCTION` must be the first statement in its batch, and each `migrationBuilder.Sql` call is its own batch.
- Migrations are `generated_code` for `dotnet format` (`.editorconfig:391–392`), and their namespace `.Migrations` is exempt from T12's public-surface rule.

**7. Modify `tests/Platform.IntegrationTests/SetupTests.cs`.** The `Snapshot` helper (`:199–208`) counts `inventory.Locations` from `app.Services.CreateAsyncScope()`, a scope with no tenant, so under RLS it returns 0 and `Seed_twice_changes_nothing` (`:164`, `after.Locations.ShouldBe(6)`) fails.
- Count Locations per tenant instead: loop over `tenantIds`, open `app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenantId)`, run `GetRequiredService<InventoryDbContext>().Locations.CountAsync(l => l.TenantId == tenantId, ct)`, and sum the results.
- Keep the Tenants and Users counts exactly as they are.
- Change nothing else in this file.

**Review constraints that apply:**
- `TenantId` never comes from a request.
- The API connects as `inventory_app`. This prompt changes no grants: `SetupTests.Inventory_app_has_exactly_the_s2_grants` must still pass unchanged. RLS needs no permission on the predicate function (ownership chaining).

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add the query filter, `ITenantOwned` or the SaveChanges interceptor. Those are prompt 02.
- Change `InventoryModule.cs`. Its explicit `TenantId` still matches the session tenant, so it keeps working until 02.
- Add packages, grants, or RLS on `identity`/`platform` tables.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
SCRATCH=$(mktemp -d)
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet ef migrations script Initial TenantPolicy --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext -o "$SCRATCH/s3.sql"
grep -n "CREATE SECURITY POLICY \[security\].\[TenantPolicy\]" "$SCRATCH/s3.sql"
grep -c "PREDICATE \[security\].\[fn_tenant\](\[TenantId\]) ON \[inventory\].\[Locations\]" "$SCRATCH/s3.sql"   # 3
grep -n "STATE = ON, SCHEMABINDING = ON" "$SCRATCH/s3.sql"
grep -n "SESSION_CONTEXT(N'TenantId')" "$SCRATCH/s3.sql"
! grep -n 'ALTER COLUMN \[TenantId\]' "$SCRATCH/s3.sql"
dotnet ef migrations has-pending-model-changes --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext   # no changes
dotnet test                                                    # Architecture 10/10, Platform.IntegrationTests 19/19
```
- Every S2 test passes: provisioning inserts `Locations` as `inventory_app` under the BLOCK predicates, which proves the interceptor sets the session tenant. `Migrate_is_idempotent` proves migrate as `sa` with no tenant still works.
- Report the generated migration's file name, and whether you kept EF's `AlterColumn` or hand-wrote the named default constraint.
