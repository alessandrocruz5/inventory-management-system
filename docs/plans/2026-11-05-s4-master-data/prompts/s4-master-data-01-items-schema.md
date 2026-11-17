---
type: prompt
plan: "[[2026-11-05-s4-master-data]]"
id: 01
unit: IMS-5
status: done
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Persistence/Item.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_Items.cs (generated, then edited)
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_Items.Designer.cs (generated)
  modify:
    - src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/InventoryDbContextModelSnapshot.cs (generated)
    - tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs
    - tests/Inventory.IntegrationTests/MigrationRollbackTests.cs
  delete: []
---
# 01 · Items table, RLS predicates, catalog tests

## Task
Add the `inventory.Items` table as a tenant-owned entity. Its creating migration adds the table to `security.TenantPolicy` (one FILTER predicate and two BLOCK predicates). Make the T6 catalog tests and the rollback test name it explicitly.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. .NET 10, EF Core 10, SQL Server 2025. `Nullable` and `TreatWarningsAsErrors` are on. You are on branch `IMS-5`; don't switch it.

**Spec §5 contract** (`docs/Inventory System — Sprint 1 Spec.md:212-219`):
```sql
inventory.Items (
  TenantId uniqueidentifier, Id uniqueidentifier,
  Sku nvarchar(64) NOT NULL, Name nvarchar(200) NOT NULL,
  BaseUnit nvarchar(16) NOT NULL, IsActive bit NOT NULL DEFAULT 1,
  CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion rowversion,
  PRIMARY KEY (TenantId, Id), UNIQUE (TenantId, Sku))
```

**1. `src/Modules/Inventory/Inventory/Persistence/Item.cs`.** Copy `Persistence/Location.cs` exactly in style:
```csharp
namespace Inventory.Persistence;

internal sealed class Item : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid Id { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public required string BaseUnit { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Null when the item was seeded.</summary>
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
```
Put a blank line between members, as `Location.cs` does.

**2. `InventoryDbContext.cs`.** Add `public DbSet<Item> Items => Set<Item>();` after `Locations` (line 13). In `OnModelCreating`, after the `Location` block (line 41), add this block. It mirrors lines 22-41:
```csharp
modelBuilder.Entity<Item>(item =>
{
    item.ToTable("Items");
    item.HasKey(i => new { i.TenantId, i.Id });
    ConfigureTenantOwned(item);
    item.Property(i => i.Id).ValueGeneratedOnAdd();
    item.Property(i => i.Sku).HasMaxLength(64).IsRequired();
    item.Property(i => i.Name).HasMaxLength(200).IsRequired();
    item.Property(i => i.BaseUnit).HasMaxLength(16).IsRequired();
    item.Property(i => i.IsActive).HasDefaultValue(true);
    item.Property(i => i.CreatedAt).IsRequired();
    item.Property(i => i.RowVersion).IsRowVersion();
    item.HasIndex(i => new { i.TenantId, i.Sku }).IsUnique().HasDatabaseName("UX_Items_Sku");
});
```
`ConfigureTenantOwned` (line 44) adds the named `"Tenant"` query filter and the `TenantId` session default. Don't change it.

**3. Generate the migration** from the repo root:
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet ef migrations add Items --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext --output-dir Persistence/Migrations
```
Check that the generated `CreateTable` has the composite PK `PK_Items`, the `TenantId` default `CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)`, `IsActive` default `true`, `RowVersion` `rowversion`, and the unique index `UX_Items_Sku` on `(TenantId, Sku)`. Then edit the migration by hand:
- **Up**, *after* `CreateTable` and `CreateIndex`, add one `migrationBuilder.Sql` with a raw string literal. Copy the style of `Migrations/20261104152736_TenantPolicy.cs:36-42`:
  ```sql
  ALTER SECURITY POLICY [security].[TenantPolicy]
      ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Items],
      ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Items] AFTER INSERT,
      ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Items] AFTER UPDATE;
  ```
  Put this comment above it: `// Same migration as the table (spec §8): the policy is schema-bound, so Down drops these before the table.`
- **Down**, *before* `DropTable`, add:
  ```sql
  ALTER SECURITY POLICY [security].[TenantPolicy]
      DROP FILTER PREDICATE ON [inventory].[Items],
      DROP BLOCK PREDICATE ON [inventory].[Items] AFTER INSERT,
      DROP BLOCK PREDICATE ON [inventory].[Items] AFTER UPDATE;
  ```
  A schema-bound policy refuses `DROP TABLE` while a predicate references the table, so the order matters.

**4. `tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs`.** Three catalog tests each read `TenantTables` and assert `tables.ShouldContain("inventory.Locations");`:
- `Every_tenant_table_has_a_filter_and_both_block_predicates`, near line 141.
- `Every_tenant_table_defaults_TenantId_to_the_session`, near line 172.
- `Tenant_tables_lead_every_key_with_TenantId`, near line 197.

Add `tables.ShouldContain("inventory.Items");` right after each of those lines. The rest of each test already loops over every table.

**5. `tests/Inventory.IntegrationTests/MigrationRollbackTests.cs`.** In `TenantPolicy_down_removes_rls_and_the_default_and_up_reapplies` (line 33), add a constant `private const string ItemsTable = "SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N'inventory.Items')";` beside the others (lines 22-30). Then assert:
- `1` after the first full `MigrateAsync`.
- `0` after `MigrateAsync(Initial, …)`, with the message `"Items.Down must drop the table (after its predicates)"`.
- `1` after the final `MigrateAsync`.

Use the existing `ScalarAsync` helper, and leave every existing assertion unchanged. Update the class's `<summary>` so it says the rollback runs `Items.Down` and then `TenantPolicy.Down`.

Conventions: every new type is `internal sealed`; migrations stay EF's generated `public partial class`, which is allowed under `.Migrations` (T12). Don't add a grant: `inventory_app` already has schema-level `SELECT, INSERT, UPDATE` on `inventory`. Review constraints: no hard deletes (the `Down` only drops what this migration created), and no bare `IgnoreQueryFilters()`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change `ConfigureTenantOwned`, `TenantSchema`, the interceptors, or the `TenantPolicy` migration.
- Add a foreign key from `Items` to anything (spec §5 has none for Items).
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet ef migrations has-pending-model-changes --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext   # no changes
dotnet test
```
- `dotnet ef migrations script TenantPolicy Items …` shows `CREATE TABLE [inventory].[Items]`, three `ADD … PREDICATE … ON [inventory].[Items]` clauses after it, and `UX_Items_Sku`.
- `RowLevelSecurityTests`, `MigrationRollbackTests` and `TenantInterceptorTests` pass, and every other existing test is still green.
