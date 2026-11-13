---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 01
unit: IMS-11
status: todo
model: opus
effort: medium
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_DocumentsPostedAtIndex.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_DocumentsPostedAtIndex.Designer.cs
  modify:
    - src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/InventoryDbContextModelSnapshot.cs
    - tests/Inventory.IntegrationTests/LedgerSchemaTests.cs
  delete: []
---
# 01 · Documents posted-at index

## Task
Add the non-unique index `IX_StockDocuments_PostedAt` on `inventory.StockDocuments (TenantId, PostedAt)` through the EF model and a generated migration named `DocumentsPostedAtIndex`. Then add one schema test that proves the index exists with exactly those key columns in that order.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11` (already checked out; never create or switch branches). .NET 10, EF Core 10, SQL Server 2025. `TreatWarningsAsErrors` is on.
- Why (plan D2): `GET /api/documents` (prompt 03) lists a tenant's documents `ORDER BY PostedAt DESC, Id DESC` with an optional half-open `PostedAt` range. SQL Server appends the clustered key (`TenantId, Id`) to a non-unique index's key, so `(TenantId, PostedAt)` serves both the order and the range without a sort. The spec audit (`docs/Inventory System — Sprint 1 Spec.audit.md:35`) named this exact index.
- `src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs:78-104` configures `StockDocument`. Add one line after the `UX_StockDocuments_Reverses` index (`:102-103`), matching the style there:
  ```csharp
  document.HasIndex(d => new { d.TenantId, d.PostedAt }).HasDatabaseName("IX_StockDocuments_PostedAt");
  ```
  Don't make it unique, descending or filtered, and don't add `INCLUDE` columns.
- Generate the migration (it never connects to a database):
  ```bash
  export DOTNET_SYSTEM_NET_DISABLEIPV6=1
  dotnet ef migrations add DocumentsPostedAtIndex --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext --output-dir Persistence/Migrations
  ```
  The latest existing migration is `20261105155422_Ledger`. Open the generated `Up`/`Down`. `Up` must contain exactly one `migrationBuilder.CreateIndex(name: "IX_StockDocuments_PostedAt", schema: "inventory", table: "StockDocuments", columns: new[] { "TenantId", "PostedAt" })`, and `Down` exactly one matching `DropIndex`. If the generator emits anything else (a column, a trigger, a constraint, a sequence change), stop and report: the model and the snapshot have drifted, and that's out of scope.
- **Must not change** (CLAUDE.md, ask-first rules): no column on `StockDocuments` or `StockMovements`, no change to spec §6, the `HasTrigger` declarations, the checks or RLS. `CREATE INDEX` is DDL run by the `migrate` step as the server admin. It isn't an `UPDATE`, so `TR_StockDocuments_AppendOnly` and the `inventory_app` `DENY`s don't fire. `security.TenantPolicy` is schema-bound on `TenantId`, but an index doesn't alter the column, so the policy is unaffected.
- IMS-6 lock: the master-data reads use `FORCESEEK` on `Items`, `Locations`, `StockBalances` and `StockMovements`. Add no index to those tables.
- Test: `tests/Inventory.IntegrationTests/LedgerSchemaTests.cs` already holds the ledger schema tests (fixture `DatabaseFixture db`; see how its existing facts open an admin connection and query the catalog). Add one `[Fact]` named `Documents_posted_at_index_leads_with_TenantId`. It queries `sys.indexes` + `sys.index_columns` + `sys.columns` for `inventory.StockDocuments` index `IX_StockDocuments_PostedAt`, and asserts: it exists, `is_unique = 0`, and its key columns ordered by `key_ordinal` are exactly `["TenantId", "PostedAt"]` (`is_included_column = 0`). Copy the connection and helper style of the nearest existing catalog query in that file. If none fits, `RowLevelSecurityTests.cs:200-223` shows a catalog query through `OpenAdminAsync` and `ReadStrings`, so mirror its SQL style inline.
- Existing tests that must stay green unmodified: `MigrationRollbackTests` (migrates down to `Initial` and back up, which exercises your `Down`), `RowLevelSecurityTests` (unique keys lead with TenantId; yours is non-unique and leads anyway), `LedgerImmutabilityTests`, `LedgerSchemaTests`.
- Known flake: if Testcontainers crashes on start (`handle-crash.sh`), rerun with `-- --max-parallel-test-modules 1`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Hand-edit the generated migration beyond what the generator produced (formatting fixes from `dotnet format` are fine).
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerSchemaTests' --filter-class '*MigrationRollbackTests' --filter-class '*RowLevelSecurityTests' --filter-class '*LedgerImmutabilityTests'
dotnet test
```
- `git diff --stat` shows only the five files above. The migration's `Up` has one `CreateIndex` and its `Down` one `DropIndex`.
- `Documents_posted_at_index_leads_with_TenantId` passes, and every pre-existing test passes unmodified.
