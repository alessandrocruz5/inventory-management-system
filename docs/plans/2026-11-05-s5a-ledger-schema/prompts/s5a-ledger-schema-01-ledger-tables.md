---
type: prompt
plan: "[[2026-11-05-s5a-ledger-schema]]"
id: 01
unit: IMS-6
status: done
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Persistence/StockDocumentType.cs
    - src/Modules/Inventory/Inventory/Persistence/AdjustmentReason.cs
    - src/Modules/Inventory/Inventory/Persistence/StockDocument.cs
    - src/Modules/Inventory/Inventory/Persistence/StockMovement.cs
    - src/Modules/Inventory/Inventory/Persistence/StockBalance.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_Ledger.cs (generated, then edited)
    - src/Modules/Inventory/Inventory/Persistence/Migrations/<timestamp>_Ledger.Designer.cs (generated)
  modify:
    - src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs
    - src/Modules/Inventory/Inventory/Persistence/Migrations/InventoryDbContextModelSnapshot.cs (generated)
    - tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs
    - tests/Inventory.IntegrationTests/MigrationRollbackTests.cs
  delete: []
---
# 01 · Ledger tables, sequence, triggers, RLS predicates

## Task
Add the three ledger tables and the movement-id sequence to `InventoryDbContext`, as tenant-owned entities with every spec §5 column, CHECK, key and composite FK. Generate the `Ledger` migration. Then add, by hand:
- the RLS predicates for the three tables,
- the two append-only triggers,
- the right `Down` order.

Finally, make the T6 catalog tests and the rollback test name the new objects.

## Context
- **Repo and branch:** the repo root is `/home/apa/Code/portfolio/inventory-management-system`, and you are on branch `IMS-6`. Don't switch it.
- **Stack:** .NET 10, EF Core 10.0.14, SQL Server 2025. `Nullable` and `TreatWarningsAsErrors` are on.
- **Prerequisite:** run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any `dotnet` command.

**Spec §5 contract** (`docs/Inventory System — Sprint 1 Spec.md:232-277`). It is the full column and index list. Add nothing beyond it.
```sql
inventory.StockDocuments (
  TenantId, Id uniqueidentifier,
  Type varchar(16) NOT NULL CHECK (Type IN ('Receipt','Issue','Transfer','Adjustment','Reversal')),
  ReversesDocumentId uniqueidentifier NULL,   -- set if and only if Type = 'Reversal'
  ReasonCode varchar(24) NULL,                -- adjustments only: Damaged, Lost, Found, CountCorrection, Other
  Reference nvarchar(100) NULL, Notes nvarchar(500) NULL,
  IdempotencyKey varchar(64) NOT NULL, RequestHash binary(32) NOT NULL,
  PostedAt datetimeoffset NOT NULL, PostedBy uniqueidentifier NOT NULL,
  PRIMARY KEY (TenantId, Id), UNIQUE (TenantId, IdempotencyKey),
  UNIQUE (TenantId, ReversesDocumentId) WHERE ReversesDocumentId IS NOT NULL)
CREATE SEQUENCE inventory.MovementIds AS bigint START WITH 1 INCREMENT BY 1;
inventory.StockMovements (
  TenantId, Id bigint NOT NULL, DocumentId uniqueidentifier NOT NULL, LineNo int NOT NULL,
  ItemId uniqueidentifier NOT NULL, FromLocationId uniqueidentifier NOT NULL, ToLocationId uniqueidentifier NOT NULL,
  Quantity decimal(19,4) NOT NULL, TotalCost decimal(19,4) NOT NULL CHECK (TotalCost >= 0),
  ReversesMovementId bigint NULL, PostedAt datetimeoffset NOT NULL, PostedBy uniqueidentifier NOT NULL,
  CHECK (FromLocationId <> ToLocationId), CHECK (Quantity > 0 OR (Quantity = 0 AND TotalCost > 0)),
  PRIMARY KEY (TenantId, Id), UNIQUE (TenantId, DocumentId, LineNo), INDEX IX_Movements_Item (TenantId, ItemId, Id))
inventory.StockBalances (
  TenantId, ItemId, LocationId, IsVirtual bit NOT NULL, AllowNegative bit NOT NULL,
  OnHand decimal(19,4) NOT NULL DEFAULT 0, StockValue decimal(19,4) NOT NULL DEFAULT 0,
  LastUnitCost decimal(19,4) NULL, LastMovementId bigint NULL, RowVersion rowversion,
  PRIMARY KEY (TenantId, ItemId, LocationId), INDEX IX_Balances_Location (TenantId, LocationId),
  CHECK (IsVirtual = 1 OR (OnHand >= 0 AND StockValue >= 0 AND (OnHand <> 0 OR StockValue = 0)) OR (OnHand < 0 AND AllowNegative = 1)),
  CHECK (IsVirtual = 0 OR StockValue = 0))
```
Every FK is composite and includes `TenantId`. Every `TenantId` defaults to the session tenant. Both come from `ConfigureTenantOwned` and the configuration below.

**1. Enums.** Copy `Persistence/LocationKind.cs` exactly in style (an `internal enum` with trailing commas):
- `StockDocumentType`: `Receipt`, `Issue`, `Transfer`, `Adjustment`, `Reversal`.
- `AdjustmentReason`: `Damaged`, `Lost`, `Found`, `CountCorrection`, `Other`.

**2. Entities.** Copy `Persistence/Location.cs` in style: `internal sealed class … : ITenantOwned`, a blank line between members, and `/// <summary>` only where noted.
```csharp
internal sealed class StockDocument : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid Id { get; set; }
    public required StockDocumentType Type { get; set; }
    /// <summary>Set if and only if <see cref="Type"/> is <see cref="StockDocumentType.Reversal"/>.</summary>
    public Guid? ReversesDocumentId { get; set; }
    /// <summary>Required on an adjustment, null on every other type.</summary>
    public AdjustmentReason? ReasonCode { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public required string IdempotencyKey { get; set; }
    /// <summary>SHA-256 of the canonical request (spec §6, step 2).</summary>
    public required byte[] RequestHash { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public Guid PostedBy { get; set; }
}

internal sealed class StockMovement : ITenantOwned
{
    public Guid TenantId { get; set; }
    /// <summary>Taken from <c>inventory.MovementIds</c> before the insert.</summary>
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    /// <summary>Request line n; its revaluation, if any, is 1000 + n.</summary>
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public Guid FromLocationId { get; set; }
    public Guid ToLocationId { get; set; }
    /// <summary>0 only on a revaluation movement (spec §7).</summary>
    public decimal Quantity { get; set; }
    public decimal TotalCost { get; set; }
    public long? ReversesMovementId { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public Guid PostedBy { get; set; }
}

internal sealed class StockBalance : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    /// <summary>Copied from the location: its kind is not Physical.</summary>
    public bool IsVirtual { get; set; }
    /// <summary>Mirrors <c>Locations.AllowNegativeStock</c> (CLAUDE.md Hidden coupling).</summary>
    public bool AllowNegative { get; set; }
    public decimal OnHand { get; set; }
    public decimal StockValue { get; set; }
    public decimal? LastUnitCost { get; set; }
    public long? LastMovementId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
```

**3. `InventoryDbContext.cs`.**
- Add the sets `StockDocuments`, `StockMovements` and `StockBalances` after `Items` (line 15), in the same style.
- Add this override above `OnModelCreating` (`using Microsoft.EntityFrameworkCore.Metadata.Conventions;`):
  ```csharp
  protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
  {
      // Quantities and money are decimal(19,4) everywhere (CLAUDE.md conventions).
      configurationBuilder.Properties<decimal>().HavePrecision(19, 4);

      // The spec §5 sketch is the full index list; EF's per-FK indexes would add three unread indexes to StockMovements.
      configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();
  }
  ```
  Check that `LastUnitCost` (`decimal?`) comes out as `decimal(19,4)` in the migration. If it doesn't, also add `Properties<decimal?>().HavePrecision(19, 4)`.
- In `OnModelCreating`, after the `Item` block (line 58), add the three blocks below. They mirror the `Location` block at lines 24-43, and every FK uses `.OnDelete(DeleteBehavior.NoAction)`. SQL Server refuses an `INSTEAD OF UPDATE/DELETE` trigger on a table with a cascading FK, and EF cascades required FKs by default.
  ```csharp
  modelBuilder.HasSequence<long>("MovementIds").StartsAt(1).IncrementsBy(1);

  modelBuilder.Entity<StockDocument>(document =>
  {
      document.ToTable("StockDocuments", t =>
      {
          // Declared so SaveChanges skips the OUTPUT clause; the trigger SQL is in the Ledger migration (CLAUDE.md Hidden coupling).
          t.HasTrigger("TR_StockDocuments_AppendOnly");
          t.HasCheckConstraint("CK_StockDocuments_Type", "[Type] IN ('Receipt', 'Issue', 'Transfer', 'Adjustment', 'Reversal')");
          t.HasCheckConstraint("CK_StockDocuments_Reversal",
              "([Type] = 'Reversal' AND [ReversesDocumentId] IS NOT NULL) OR ([Type] <> 'Reversal' AND [ReversesDocumentId] IS NULL)");
          // IS NOT NULL is needed: a CHECK passes when its expression is UNKNOWN.
          t.HasCheckConstraint("CK_StockDocuments_ReasonCode",
              "([Type] = 'Adjustment' AND [ReasonCode] IS NOT NULL AND [ReasonCode] IN ('Damaged', 'Lost', 'Found', 'CountCorrection', 'Other')) " +
              "OR ([Type] <> 'Adjustment' AND [ReasonCode] IS NULL)");
      });
      document.HasKey(d => new { d.TenantId, d.Id });
      ConfigureTenantOwned(document);
      document.Property(d => d.Id).ValueGeneratedOnAdd();
      document.Property(d => d.Type).HasConversion<string>().HasMaxLength(16).IsUnicode(false).IsRequired();
      document.Property(d => d.ReasonCode).HasConversion<string>().HasMaxLength(24).IsUnicode(false);
      document.Property(d => d.Reference).HasMaxLength(100);
      document.Property(d => d.Notes).HasMaxLength(500);
      document.Property(d => d.IdempotencyKey).HasMaxLength(64).IsUnicode(false).IsRequired();
      document.Property(d => d.RequestHash).HasMaxLength(32).IsFixedLength().IsRequired();
      document.HasIndex(d => new { d.TenantId, d.IdempotencyKey }).IsUnique().HasDatabaseName("UX_StockDocuments_IdempotencyKey");
      document.HasIndex(d => new { d.TenantId, d.ReversesDocumentId }).IsUnique()
          .HasFilter("[ReversesDocumentId] IS NOT NULL").HasDatabaseName("UX_StockDocuments_Reverses");
      document.HasOne<StockDocument>().WithMany().HasForeignKey(d => new { d.TenantId, d.ReversesDocumentId }).OnDelete(DeleteBehavior.NoAction);
  });
  ```
  - **`StockMovement`:**
    - `ToTable("StockMovements", t => …)` with `t.HasTrigger("TR_StockMovements_AppendOnly")` and three CHECKs:
      - `CK_StockMovements_Locations`: `[FromLocationId] <> [ToLocationId]`
      - `CK_StockMovements_Quantity`: `[Quantity] > 0 OR ([Quantity] = 0 AND [TotalCost] > 0)`
      - `CK_StockMovements_TotalCost`: `[TotalCost] >= 0`
    - Key `(TenantId, Id)`, `ConfigureTenantOwned`, `Id` `.ValueGeneratedNever()`.
    - Unique index `(TenantId, DocumentId, LineNo)` named `UX_StockMovements_DocumentLine`.
    - Index `(TenantId, ItemId, Id)` named `IX_Movements_Item`.
    - FKs (each `HasOne<T>().WithMany().HasForeignKey(m => new { m.TenantId, m.X }).OnDelete(DeleteBehavior.NoAction)`):
      - `DocumentId` → `StockDocument`
      - `ItemId` → `Item`
      - `FromLocationId` → `Location`
      - `ToLocationId` → `Location`
      - `ReversesMovementId` → `StockMovement`
  - **`StockBalance`:**
    - `ToTable("StockBalances", t => …)` with two CHECKs:
      - `CK_StockBalances_Physical`: `[IsVirtual] = 1 OR ([OnHand] >= 0 AND [StockValue] >= 0 AND ([OnHand] <> 0 OR [StockValue] = 0)) OR ([OnHand] < 0 AND [AllowNegative] = 1)`
      - `CK_StockBalances_VirtualValue`: `[IsVirtual] = 0 OR [StockValue] = 0`
    - Key `(TenantId, ItemId, LocationId)`, `ConfigureTenantOwned`.
    - `OnHand` and `StockValue` get `.HasDefaultValue(0m)`. `RowVersion` gets `.IsRowVersion()`.
    - Index `(TenantId, LocationId)` named `IX_Balances_Location`.
    - FKs `(TenantId, ItemId)` → `Item` and `(TenantId, LocationId)` → `Location`, both `NoAction`.
    - Spec §6 step 6: add no other index. The clustered key is the posting lock path.
- Don't change `ConfigureTenantOwned` (line 61), `TenantSchema` or the interceptors.

**4. Generate the migration** from the repo root:
```bash
dotnet ef migrations add Ledger --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext --output-dir Persistence/Migrations
```
Check the generated `Up`:
- It contains `EnsureSchema` or nothing for the schema, then `CreateSequence<long>("MovementIds", schema: "inventory")` and the three `CreateTable`s.
- Every FK has `onDelete: ReferentialAction.NoAction`.
- `TenantId` defaults to `CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)`.
- `decimal(19,4)` is on the 5 decimal columns, `binary(32)` on `RequestHash` and `varchar` on `Type`, `ReasonCode` and `IdempotencyKey`.
- The indexes are exactly `UX_StockDocuments_IdempotencyKey`, `UX_StockDocuments_Reverses` (filtered), `UX_StockMovements_DocumentLine`, `IX_Movements_Item` and `IX_Balances_Location`.
- **Nothing** touches `Items` or `Locations`. If something does, the convention change leaked: stop and report.

Then edit it by hand, copying the raw-string style of `Migrations/20261105132515_Items.cs:43-60`:
- **Up**, *after* every `CreateTable`/`CreateIndex`:
  1. One `migrationBuilder.Sql`, with the comment `// Same migration as the tables (spec §8): the policy is schema-bound, so Down drops these before the tables.`
     ```sql
     ALTER SECURITY POLICY [security].[TenantPolicy]
         ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockDocuments],
         ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockDocuments] AFTER INSERT,
         ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockDocuments] AFTER UPDATE,
         -- the same three lines for [inventory].[StockMovements] and [inventory].[StockBalances]
     ```
     Write out all nine clauses, with no trailing comma.
  2. Two separate `migrationBuilder.Sql` calls, one per trigger, because `CREATE TRIGGER` must be alone in its batch. Put this comment above them: `// Append-only (spec §6): even the owner can't edit or delete a posted row. Names match HasTrigger in InventoryDbContext.`
     ```sql
     CREATE TRIGGER [inventory].[TR_StockDocuments_AppendOnly] ON [inventory].[StockDocuments]
     INSTEAD OF UPDATE, DELETE
     AS
     BEGIN
         SET NOCOUNT ON;
         THROW 51000, N'inventory.StockDocuments is append-only. Post a reversal instead.', 1;
     END
     ```
     The movement trigger is the same, with `StockMovements` in both names and the message.
- **Down**: as the *first* statement, one `migrationBuilder.Sql` that drops the nine predicates (`DROP FILTER PREDICATE ON [inventory].[StockDocuments], DROP BLOCK PREDICATE ON … AFTER INSERT, DROP BLOCK PREDICATE ON … AFTER UPDATE`, ×3 tables). Leave EF's generated `DropTable`s and `DropSequence` after it. The triggers drop with their tables.
- If `ALTER SECURITY POLICY` or `CREATE TRIGGER` is refused (for example, a predicate conflicting with an `INSTEAD OF` trigger), **stop and report** the exact error. Don't drop either.

**5. `tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs`.** In each of the three catalog tests, after `tables.ShouldContain("inventory.Items");`, add `ShouldContain` for `inventory.StockDocuments`, `inventory.StockMovements` and `inventory.StockBalances`:
- `Every_tenant_table_has_a_filter_and_both_block_predicates`, line 142.
- `Every_tenant_table_defaults_TenantId_to_the_session`, line 175.
- `Tenant_tables_lead_every_key_with_TenantId`, line 201.

The rest of each test already loops over every table.

**6. `tests/Inventory.IntegrationTests/MigrationRollbackTests.cs`.**
- Beside `ItemsTable` (line 32), add:
  ```csharp
  private const string LedgerObjects =
      "SELECT (SELECT COUNT(*) FROM sys.tables WHERE object_id IN (OBJECT_ID(N'inventory.StockDocuments'), OBJECT_ID(N'inventory.StockMovements'), OBJECT_ID(N'inventory.StockBalances'))) " +
      "+ (SELECT COUNT(*) FROM sys.sequences WHERE object_id = OBJECT_ID(N'inventory.MovementIds'))";
  ```
- Assert its value:
  - `4` after the first `MigrateAsync` (line 46).
  - `0` after `MigrateAsync(Initial, …)` (line 51), with the message `"Ledger.Down must drop the tables (after their predicates) and the sequence"`.
  - `4` after the final `MigrateAsync` (line 57).
- Use the existing `ScalarAsync`, and leave every other assertion unchanged.
- Update the class `<summary>` (line 15) so it says the rollback runs `Ledger.Down`, `Items.Down` and then `TenantPolicy.Down`.

**Conventions:**
- Every new type is `internal sealed` or `internal enum`. Migrations stay EF's generated `public partial class`, which T12 allows under `.Migrations`.
- Review constraints: decimals only, never `double`/`float`; no hard deletes (`Down` drops only what this migration creates); no bare `IgnoreQueryFilters()`; no column beyond the §5 sketch.
- Grants are prompt 02's job. Don't touch `DatabasePrincipals`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add a column, index or FK that isn't listed above, including an FK on `LastMovementId` or `PostedBy`.
- Put anything in `Inventory/Domain/` (S5b owns it).
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet ef migrations has-pending-model-changes --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext   # no changes
SCRATCH=$(mktemp -d)
dotnet ef migrations script Items Ledger --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext -o "$SCRATCH/s5a.sql"
grep -c 'ADD .*PREDICATE \[security\].\[fn_tenant\](\[TenantId\]) ON \[inventory\].\[Stock' "$SCRATCH/s5a.sql"   # 9
grep -c 'INSTEAD OF UPDATE, DELETE' "$SCRATCH/s5a.sql"                                                          # 2
! grep -n 'CASCADE' "$SCRATCH/s5a.sql"
dotnet test
```
- `RowLevelSecurityTests`, `MigrationRollbackTests` and `TenantInterceptorTests` pass, and every other existing test is still green.
- The migration's `Up` creates only the sequence, the three tables, their indexes and FKs, the nine predicates and two triggers. Its `Down` drops the predicates first.
