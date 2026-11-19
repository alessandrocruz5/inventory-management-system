---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 09
unit: IMS-30
status: done
model: opus
effort: high
depends_on: []
parallel_group: I
files:
  create:
    - src/Modules/Inventory/Inventory/Persistence/Stocktake.cs
    - src/Modules/Inventory/Inventory/Persistence/StocktakeLine.cs
    - src/Modules/Inventory/Inventory/Persistence/StocktakeStatus.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_Stocktakes.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_Stocktakes.Designer.cs
  modify:
    - src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/InventoryDbContextModelSnapshot.cs
    - tests/Inventory.IntegrationTests/LedgerSchemaTests.cs
  delete: []
---
# 09 · Stocktake schema: `Stocktakes`, `StocktakeLines`, RLS

## Task
Add two tenant-owned work tables for stocktakes, with their entities, configuration, checks, FKs, the one-open-per-location filtered unique index and RLS predicates, all in one migration. Extend the schema tests.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. IMS-28 (`ItemUnits`) and IMS-29 are merged into the base, so the latest migration is `*_ItemUnits`.
- Conventions:
  - `decimal(19,4)` for quantities and costs.
  - `TenantId` first in every PK, unique index and FK, with composite FKs and `NoAction`.
  - Times are `datetimeoffset`.
  - No hard deletes.
  - Types are `internal`; migrations are public (T12 exemption).
- Hidden coupling: every tenant table is in `security.TenantPolicy`, added in **this** migration (T6 catalog, `RowLevelSecurityTests.cs:136-160`).
- These are mutable work documents, not ledger rows. No triggers, no `HasTrigger` and no `DENY`. `inventory_app` already has schema-level `SELECT, INSERT, UPDATE` and no `DELETE`.
- Plan D8, D10, D12, D13:
  - `StocktakeStatus { Draft, Posting, Posted, Cancelled }`, stored as a string (`HasConversion<string>().HasMaxLength(16).IsUnicode(false)`), like `Locations.Kind` at `InventoryDbContext.cs:52`.
  - `inventory.Stocktakes`: `TenantId`, `Id uniqueidentifier` (EF default generator, `ValueGeneratedOnAdd`, like `Items`), `LocationId`, `Status`, `Notes nvarchar(500) NULL`, `PostedDocumentId uniqueidentifier NULL`, `CreatedAt datetimeoffset NOT NULL`, `CreatedBy uniqueidentifier NOT NULL`, `UpdatedAt datetimeoffset NULL`, `UpdatedBy uniqueidentifier NULL`, `RowVersion rowversion`.
    - PK `(TenantId, Id)`.
    - FK `(TenantId, LocationId)` → `Locations`. FK `(TenantId, PostedDocumentId)` → `StockDocuments`. This adds no column to `StockDocuments`.
    - Checks: `CK_Stocktakes_Status` `[Status] IN ('Draft', 'Posting', 'Posted', 'Cancelled')` and `CK_Stocktakes_PostedDocument` `[PostedDocumentId] IS NULL OR [Status] = 'Posted'`. A Posted stocktake with no differences has no document (D13).
    - Index `UX_Stocktakes_Open` on `(TenantId, LocationId)`, unique, filter `[Status] IN ('Draft', 'Posting')` (D12).
    - Index `IX_Stocktakes_Created` on `(TenantId, CreatedAt)` for the newest-first list.
  - `inventory.StocktakeLines`: `TenantId`, `StocktakeId`, `ItemId`, `ExpectedQuantity decimal(19,4) NULL`, `ExpectedLastMovementId bigint NULL`, `CountedQuantity decimal(19,4) NULL`, `UnitCost decimal(19,4) NULL`, `CountedAt datetimeoffset NULL`, `CountedBy uniqueidentifier NULL`.
    - PK `(TenantId, StocktakeId, ItemId)`.
    - FKs `(TenantId, StocktakeId)` → `Stocktakes` and `(TenantId, ItemId)` → `Items`.
    - `CK_StocktakeLines_Counted`: `([CountedQuantity] IS NULL AND [ExpectedQuantity] IS NULL AND [CountedAt] IS NULL AND [CountedBy] IS NULL AND [ExpectedLastMovementId] IS NULL) OR ([CountedQuantity] IS NOT NULL AND [CountedQuantity] >= 0 AND [ExpectedQuantity] IS NOT NULL AND [CountedAt] IS NOT NULL AND [CountedBy] IS NOT NULL)`. Note that `ExpectedLastMovementId` may be null for a balance that never existed. Add a comment: `IS NOT NULL` is needed because a CHECK passes on UNKNOWN (the `CK_StockDocuments_ReasonCode` comment at `InventoryDbContext.cs:86`).
    - `CK_StocktakeLines_UnitCost`: `[UnitCost] IS NULL OR [UnitCost] >= 0`.
    - `ExpectedQuantity` may be negative, at a location that allows negative stock.
- Patterns:
  - `Persistence/Item.cs` and `InventoryDbContext.cs:61-73` (entity and config).
  - `:76-118` (checks inside `ToTable(..., t => ...)`).
  - `:131-145` (composite FKs and `HasPrecision`).
  - `ConfigureTenantOwned` at `:148`.
  - `Persistence/Migrations/20261105132515_Items.cs` (RLS `Sql` blocks in `Up`, dropped first in `Down`).

### Steps
1. Create the three files, with XML docs that cite plan D8/D12/D13. `Stocktake` has a navigation-free shape (ids only), like the other entities.
2. Add `DbSet<Stocktake> Stocktakes` and `DbSet<StocktakeLine> StocktakeLines`, plus their configurations as above.
3. Generate the migration: `dotnet ef migrations add Stocktakes --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext --output-dir Persistence/Migrations`. Then hand-add the RLS blocks for **both** tables, `Stocktakes` before `StocktakeLines` in `Up` and the reverse in `Down`, all before the `DropTable`s.
4. `LedgerSchemaTests.cs`, in the style of `Checks_reject_bad_rows` (`:42`) and `Unique_keys_hold` (`:103`), as `inventory_app`:
   - Status `'Open'` → `CK_Stocktakes_Status`.
   - A Draft row with a `PostedDocumentId` → `CK_Stocktakes_PostedDocument`.
   - A line with `CountedQuantity` but no `ExpectedQuantity` → `CK_StocktakeLines_Counted`.
   - A line with `CountedQuantity` −1 → `CK_StocktakeLines_Counted`.
   - `UnitCost` −1 → `CK_StocktakeLines_UnitCost`.
   - Two Draft stocktakes for one location → 2601 on `UX_Stocktakes_Open`. Then one Draft and one Cancelled → accepted.
   - Edge rows accepted (`Checks_accept_the_edge_rows`): an uncounted line; a counted line with `ExpectedQuantity` −2 and a null `ExpectedLastMovementId`; a Posted stocktake with no document.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Add triggers, `HasTrigger`, grants, endpoints, or a column on `StockDocuments` or `StockMovements` (ask-first rule).
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerSchemaTests' --filter-class '*RowLevelSecurityTests' --filter-class '*MigrationRollbackTests'
dotnet test --project tests/Architecture.Tests
```
- T6's catalog lists both tables, each with one FILTER and two BLOCK predicates.
