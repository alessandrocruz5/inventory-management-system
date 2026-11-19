---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 01
unit: IMS-28
status: done
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Persistence/ItemUnit.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_ItemUnits.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_ItemUnits.Designer.cs
  modify:
    - src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/InventoryDbContextModelSnapshot.cs
    - tests/Inventory.IntegrationTests/LedgerSchemaTests.cs
  delete: []
---
# 01 · Item units schema: `inventory.ItemUnits` with RLS

## Task
Add the tenant-owned table `inventory.ItemUnits`, which holds an item's alternate units, each a fixed factor of the item's base unit. Add its EF entity and configuration, and one migration that creates the table, its check, its FK and its row-level-security predicates. Extend the schema tests.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. .NET 10, `Nullable` on, `TreatWarningsAsErrors`. Module types are `internal`; EF migrations are public by the T12 exemption. Quantities are `decimal(19,4)`. `TenantId` is the first column of every PK, unique index and FK. No hard deletes.
- Plan [[2026-11-18-units-stocktakes]] D3: `ItemUnits (TenantId, ItemId, Code nvarchar(16), Factor decimal(19,4) > 0, IsActive bit default 1, CreatedAt datetimeoffset, CreatedBy uniqueidentifier NULL, UpdatedAt datetimeoffset NULL, UpdatedBy uniqueidentifier NULL)`, PK `(TenantId, ItemId, Code)`. A factor is immutable once created, and a unit is deactivated, never deleted. There is no `RowVersion`: the only update is an idempotent deactivate.
- Hidden coupling (CLAUDE.md): every tenant table is in `security.TenantPolicy` (FILTER plus two BLOCK predicates), added **in the migration that creates it**. T6 (`tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs`, the catalog check at `:136-160`) fails otherwise. `LedgerSchemaTests.Foreign_keys_include_the_tenant` and `Every_inventory_decimal_is_19_4` pick the new table up by themselves.
- Patterns to copy:
  - Entity: `src/Modules/Inventory/Inventory/Persistence/Item.cs` (implements `ITenantOwned`).
  - Configuration: `InventoryDbContext.cs:61-73` (`Items`), and `:131-145` for composite FKs `HasOne<Item>().WithMany().HasForeignKey(x => new { x.TenantId, x.ItemId }).OnDelete(DeleteBehavior.NoAction)` and `t.HasCheckConstraint(...)` inside `ToTable(..., t => ...)`. Always call `ConfigureTenantOwned(entity)` (`:148-152`), which sets the named `Tenant` filter and the `SESSION_CONTEXT` column default.
  - Migration with RLS: `Persistence/Migrations/20261105132515_Items.cs`. `Up` creates the table and then runs `ALTER SECURITY POLICY [security].[TenantPolicy] ADD FILTER PREDICATE … ADD BLOCK PREDICATE … AFTER INSERT, ADD BLOCK PREDICATE … AFTER UPDATE`. `Down` drops the three predicates **before** dropping the table, because the policy is schema-bound.

### Steps
1. `Persistence/ItemUnit.cs`: `internal sealed class ItemUnit : ITenantOwned` with `TenantId`, `ItemId`, `required string Code`, `decimal Factor`, `bool IsActive`, `DateTimeOffset CreatedAt`, `Guid? CreatedBy`, `DateTimeOffset? UpdatedAt` and `Guid? UpdatedBy`. Add an XML doc: "An alternate unit of an item: one `Code` is `Factor` base units (plan D3). Immutable except for deactivation."
2. `InventoryDbContext`:
   - `public DbSet<ItemUnit> ItemUnits => Set<ItemUnit>();`
   - Configure the entity:
     - `ToTable("ItemUnits", t => t.HasCheckConstraint("CK_ItemUnits_Factor", "[Factor] > 0"))`.
     - `HasKey(u => new { u.TenantId, u.ItemId, u.Code })` and `ConfigureTenantOwned(u)`.
     - `Code` `HasMaxLength(16).IsRequired()`, `Factor` `HasPrecision(19, 4)`, `IsActive` `HasDefaultValue(true)`, `CreatedAt` `IsRequired()`.
     - FK to `Item` on `(TenantId, ItemId)` with `NoAction`.
3. Generate the migration with the local tool (`.config/dotnet-tools.json`):
   `dotnet ef migrations add ItemUnits --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext --output-dir Persistence/Migrations`
   Then hand-add the RLS `migrationBuilder.Sql` blocks to `Up` (after `CreateTable` and its index) and `Down` (before `DropTable`), word for word in the `Items.cs` style with the comment "Same migration as the table (spec §8): the policy is schema-bound, so Down drops these before the table." Don't edit anything else the tool generates.
4. `tests/Inventory.IntegrationTests/LedgerSchemaTests.cs`: read the file first, especially the `[Theory]` `Checks_reject_bad_rows` (`:42`) and `Unique_keys_hold` (`:103`), and follow their exact style.
   - Add a case that inserts an `ItemUnits` row with `Factor = 0` as `inventory_app` and expects `CK_ItemUnits_Factor`.
   - Extend `Unique_keys_hold`: a second row with the same `(ItemId, Code)` fails with 2627.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Add API endpoints, a `RowVersion`, a reactivation path or seed data. Those are prompts 02–03 or non-goals.
- Change `Items`, the ledger tables, `DatabasePrincipals` or grants. The schema-level `SELECT, INSERT, UPDATE` already covers the new table, and no `DELETE` is granted.
- Add dependencies or abstractions. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerSchemaTests' --filter-class '*RowLevelSecurityTests' --filter-class '*MigrationRollbackTests'
dotnet test --project tests/Architecture.Tests
grep -n "TenantPolicy" src/Modules/Inventory/Inventory/Persistence/Migrations/*_ItemUnits.cs
```
- T6's catalog lists `inventory.ItemUnits` with one FILTER and two BLOCK predicates.
