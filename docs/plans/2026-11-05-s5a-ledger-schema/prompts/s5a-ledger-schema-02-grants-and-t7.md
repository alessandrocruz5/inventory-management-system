---
type: prompt
plan: "[[2026-11-05-s5a-ledger-schema]]"
id: 02
unit: IMS-6
status: done
model: opus
effort: high
depends_on: [01]
parallel_group: B
files:
  create:
    - tests/Inventory.IntegrationTests/LedgerRows.cs
    - tests/Inventory.IntegrationTests/LedgerImmutabilityTests.cs
    - tests/Inventory.IntegrationTests/LedgerSchemaTests.cs
  modify:
    - src/Api/Setup/DatabasePrincipals.cs
    - tests/Platform.IntegrationTests/SetupTests.cs
  delete: []
---
# 02 · Grants, T7 and schema tests

## Task
1. Narrow `inventory_app` on the ledger: `DENY UPDATE, DELETE` on `StockDocuments` and `StockMovements`, and an explicit `GRANT UPDATE` on the `MovementIds` sequence.
2. Assert those grants.
3. Write T7, and the tests that prove the S5a schema (CHECKs, unique keys, composite FKs, decimals, sequence, triggers vs `HasTrigger`, the EF insert path).
4. Add a raw-SQL helper that prompt 03 reuses.

## Context
- **Repo and branch:** the repo root is `/home/apa/Code/portfolio/inventory-management-system`, and you are on branch `IMS-6`. Don't switch it.
- **Stack:** .NET 10, xUnit v3 (`TestContext.Current.CancellationToken`), Shouldly, Testcontainers SQL Server 2025.
- **Prerequisite:** run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` first.
- **What prompt 01 created** (`src/Modules/Inventory/Inventory/Persistence/`):
  - `StockDocument`, `StockMovement` and `StockBalance` entities, with sets `db.StockDocuments`, `db.StockMovements` and `db.StockBalances`.
  - The enums `StockDocumentType` and `AdjustmentReason`.
  - The sequence `[inventory].[MovementIds]` (bigint).
  - The triggers `TR_StockDocuments_AppendOnly` and `TR_StockMovements_AppendOnly`: `INSTEAD OF UPDATE, DELETE` → `THROW 51000`.
  - These CHECKs:
    - `CK_StockDocuments_Type`, `_Reversal` and `_ReasonCode` (required on Adjustment, NULL otherwise)
    - `CK_StockMovements_Locations`, `_Quantity` and `_TotalCost`
    - `CK_StockBalances_Physical` and `_VirtualValue`
  - These indexes: `UX_StockDocuments_IdempotencyKey`, `UX_StockDocuments_Reverses` (filtered), `UX_StockMovements_DocumentLine`, `IX_Movements_Item` and `IX_Balances_Location`.
  - Composite `NoAction` FKs that include `TenantId`.
  - RLS on all three tables.
- **The two principals (spec §8 `:522`):**
  - Migrate runs as `sa`. `MigrateCommand.cs:22-31` applies every migration first, then calls `DatabasePrincipals.EnsureAsync`, so the ledger tables exist when the grants run.
  - The API and the tests run as `inventory_app`, which has schema-level `SELECT, INSERT, UPDATE` on `inventory`.

**1. `src/Api/Setup/DatabasePrincipals.cs`.**
- Replace the comment at line 17 (`// S5a adds the sequence grant and DENY UPDATE on the ledger tables.`) with nothing.
- Append to `EnsureSql`, after line 30:
  ```sql
  -- The ledger is append-only (spec §6). An object DENY overrides the schema grant above; DELETE too, so a later schema grant can't reach it.
  DENY UPDATE, DELETE ON OBJECT::[inventory].[StockDocuments] TO [inventory_app];
  DENY UPDATE, DELETE ON OBJECT::[inventory].[StockMovements] TO [inventory_app];
  -- NEXT VALUE FOR needs UPDATE on the sequence (spec §8).
  GRANT UPDATE ON OBJECT::[inventory].[MovementIds] TO [inventory_app];
  ```
- Each statement is idempotent, and migrate runs on every AppHost start. Change nothing else.

**2. `tests/Platform.IntegrationTests/SetupTests.cs`.**
- Rename `Inventory_app_has_exactly_the_s2_grants` (line 64) to `Inventory_app_has_exactly_its_grants`.
- After the `schemaGrants` assertion (line 88), add an object-level query. Copy the `COLLATE DATABASE_DEFAULT` style of lines 70-75, and use the file's `ReadStrings`:
  ```sql
  SELECT p.state_desc COLLATE DATABASE_DEFAULT + N':' + SCHEMA_NAME(o.schema_id) COLLATE DATABASE_DEFAULT + N'.' + o.name COLLATE DATABASE_DEFAULT + N':' + p.permission_name COLLATE DATABASE_DEFAULT
  FROM sys.database_permissions p
  JOIN sys.objects o ON o.object_id = p.major_id
  WHERE p.class = 1 AND p.grantee_principal_id = USER_ID(N'inventory_app')
  ```
- Assert that its `.Order().ToList()` equals:
  - `DENY:inventory.StockDocuments:DELETE`
  - `DENY:inventory.StockDocuments:UPDATE`
  - `DENY:inventory.StockMovements:DELETE`
  - `DENY:inventory.StockMovements:UPDATE`
  - `GRANT:inventory.MovementIds:UPDATE`
- Leave the role and database-level checks unchanged.

**3. `tests/Inventory.IntegrationTests/LedgerRows.cs`** (create). This is the raw-SQL helper for S5a tests and for prompt 03; there's no posting engine until S5c.
- Make it `internal static class LedgerRows`, with a `<summary>` saying: inserts ledger rows as `inventory_app` on a raw connection whose session context is the tenant; `TenantId` comes from the column default.
- Copy the connection setup from `RowLevelSecurityTests.cs:226-237` (unpooled, `sp_set_session_context … @read_only = 1`) and its `Command`/parameter style (`:246-255`). Add `SqlDbType.Decimal` with `Precision = 19, Scale = 4` for `decimal`, `Bit` for `bool`, `BigInt` for `long`, `Int` for `int`, `VarChar` for the varchar columns, and `DBNull.Value` for nulls.
- Members:
  ```csharp
  public static Task<SqlConnection> OpenAsync(DatabaseFixture db, Guid tenant, CancellationToken ct);
  public static Task<Guid> LocationIdAsync(SqlConnection c, string code, CancellationToken ct, SqlTransaction? tx = null); // SUP, CUS, ADJ or a physical code
  public static Task<Guid> AddItemAsync(SqlConnection c, string sku, CancellationToken ct, SqlTransaction? tx = null);   // Id = NEWID(), BaseUnit 'pc', CreatedAt SYSDATETIMEOFFSET()
  public static Task<Guid> AddLocationAsync(SqlConnection c, string code, CancellationToken ct, bool allowNegative = false, SqlTransaction? tx = null); // Kind 'Physical'
  public static Task<Guid> AddDocumentAsync(SqlConnection c, CancellationToken ct, SqlTransaction? tx = null,
      string type = "Receipt", Guid? reverses = null, string? reasonCode = null, string? idempotencyKey = null);
      // Id = NEWID(); key defaults to a new Guid string; RequestHash = HASHBYTES('SHA2_256', @key); PostedAt = SYSDATETIMEOFFSET(); PostedBy = NEWID()
  public static Task<long> AddMovementAsync(SqlConnection c, Guid documentId, int lineNo, Guid itemId, Guid from, Guid to,
      decimal quantity, decimal totalCost, CancellationToken ct, SqlTransaction? tx = null, long? reverses = null);
      // DECLARE @id bigint = NEXT VALUE FOR [inventory].[MovementIds]; INSERT … VALUES (@id, …); SELECT @id;
  public static Task AddBalanceAsync(SqlConnection c, Guid itemId, Guid locationId, bool isVirtual, bool allowNegative,
      decimal onHand, decimal stockValue, CancellationToken ct, SqlTransaction? tx = null);
  /// <summary>Waits (10 s at most) until a request is blocked by <paramref name="holder"/>'s session, polling sys.dm_exec_requests as sa.</summary>
  public static Task WaitUntilBlockedAsync(DatabaseFixture db, SqlConnection holder, CancellationToken ct, SqlTransaction? tx = null);
  ```
- `WaitUntilBlockedAsync`:
  1. Read `@@SPID` on `holder` (in `tx` if given).
  2. On a new `SqlConnection(db.AdminConnectionString)`, poll every 50 ms with `SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = @spid`.
  3. Fail with `Shouldly`'s `ShouldBeGreaterThan(0, "…")` once 10 s pass.

  This is what makes lock tests deterministic: a request that doesn't lock finishes instead of blocking, so the test fails.
- On a table with an enabled trigger, `OUTPUT` without `INTO` is refused. Declaring `@id = NEXT VALUE FOR …` first avoids that, and it is how S5c takes ids ("taken before insert", spec §5).

**4. `tests/Inventory.IntegrationTests/LedgerImmutabilityTests.cs`** (create): T7 (spec §11 `:639`).
- Make it `public sealed class LedgerImmutabilityTests(DatabaseFixture db)`, with the `<summary>`: *T7: the ledger is append-only for the owner (triggers) and for `inventory_app` (permissions), and `inventory_app` can't switch the guarantees off.*
- Each test creates its own tenant with `DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin)` (copy `RowLevelSecurityTests.cs:218-224`). Seed one item (`AddItemAsync`), one physical location, a receipt document, and a movement SUP → physical (10 / 1000.0000).
- **`Owner_updates_and_deletes_hit_the_triggers`:**
  - On `db.AdminConnectionString` (sa), first run `EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1;`, so the RLS filter shows the rows and each `WHERE` matches one.
  - Run each of these, expecting `SqlException.Number == 51000`:
    - `UPDATE inventory.StockDocuments SET Notes = N'x' WHERE Id = @d`
    - `DELETE FROM inventory.StockDocuments WHERE Id = @d`
    - `UPDATE inventory.StockMovements SET TotalCost = 1 WHERE Id = @m`
    - `DELETE FROM inventory.StockMovements WHERE Id = @m`
  - Afterwards, as `inventory_app`, the document `Notes` is still NULL, the movement `TotalCost` is still `1000.0000`, and both rows exist.
- **`Inventory_app_cannot_update_or_delete_the_ledger`:** the same four statements on `LedgerRows.OpenAsync` → `SqlException.Number == 229` (permission denied: the `DENY` runs before the trigger). The rows are unchanged.
- **`Inventory_app_cannot_disable_triggers_truncate_or_alter_the_policy`:**
  - As `inventory_app`, each of these throws `SqlException`:
    - `DISABLE TRIGGER [inventory].[TR_StockDocuments_AppendOnly] ON [inventory].[StockDocuments]`
    - `DISABLE TRIGGER [inventory].[TR_StockMovements_AppendOnly] ON [inventory].[StockMovements]`
    - `TRUNCATE TABLE [inventory].[StockMovements]`
    - `TRUNCATE TABLE [inventory].[StockBalances]`
    - `ALTER SECURITY POLICY [security].[TenantPolicy] WITH (STATE = OFF)`
  - Expected numbers: 1088 for `DISABLE TRIGGER` and `TRUNCATE`, and 15151 or 15247 for the policy. Verify on the first run. Assert the observed number only if it is a permission error. It must **not** be 4712 (FK-referenced `TRUNCATE`): that would prove the FK, not the permission.
  - Then, as sa, assert that `sys.triggers.is_disabled = 0` for both triggers, `sys.security_policies.is_enabled = 1` for `TenantPolicy`, and the movement row still exists (read as `inventory_app`).
- **`Inventory_app_can_insert_the_ledger_and_update_balances`:** as `inventory_app`, insert a document, a movement (proving `NEXT VALUE FOR` works) and a balance, then `UPDATE inventory.StockBalances SET OnHand = 10, StockValue = 1000 WHERE ItemId = @i AND LocationId = @l` succeeds (1 row). S5c needs both permissions.

**5. `tests/Inventory.IntegrationTests/LedgerSchemaTests.cs`** (create). Make it `public sealed class LedgerSchemaTests(DatabaseFixture db)`, with the `<summary>`: *The S5a schema: spec §5 CHECKs, keys and composite FKs; decimal(19,4); the sequence; triggers matching `HasTrigger`.*
- **`Checks_reject_bad_rows`:** a `[Theory]` with `[InlineData]` per case. Each case runs one insert through `LedgerRows` (or inline SQL on its connection) and expects `SqlException.Number == 547` naming the CHECK in `Message`.
  - Document cases:
    - type `Bogus` → `CK_StockDocuments_Type`
    - `Reversal` with no `reverses` → `CK_StockDocuments_Reversal`
    - `Receipt` with `reverses` pointing at a receipt inserted in the test → `CK_StockDocuments_Reversal`
    - `Adjustment` with NULL reason → `CK_StockDocuments_ReasonCode`
    - `Adjustment` with reason `Bogus` → `CK_StockDocuments_ReasonCode`
    - `Receipt` with reason `Damaged` → `CK_StockDocuments_ReasonCode`
  - Movement cases:
    - from = to → `CK_StockMovements_Locations`
    - quantity 0 with cost 0 → `CK_StockMovements_Quantity`
    - quantity −1 → `CK_StockMovements_Quantity`
    - cost −1 → `CK_StockMovements_TotalCost`
  - Balance cases:
    - physical −1 / 0 with `allowNegative` false → `CK_StockBalances_Physical`
    - physical 0 / 5 → `CK_StockBalances_Physical`
    - physical 1 / −1 → `CK_StockBalances_Physical`
    - virtual −5 / 1 → `CK_StockBalances_VirtualValue`
  - Use a short case key plus a `switch` in the test body, so the `InlineData` stays primitive.
  - **Every case satisfies every FK**, so the CHECK is the only constraint the row breaks. Use an item, locations and a document inserted in the test, never a random Guid. FK and CHECK violations are both 547, and which one SQL Server names depends on the plan, so a broken FK makes the `Message` assertion nondeterministic.
- **`Checks_accept_the_edge_rows`:** each of these inserts succeeds:
  - a revaluation movement (quantity 0, cost 18.0000)
  - a physical balance −2 / −100 with `allowNegative` true
  - a virtual balance −10 / 0
  - an `Adjustment` with `CountCorrection`
  - a `Reversal` whose `reverses` points at an existing receipt
- **`Unique_keys_hold`:**
  - The same idempotency key twice in one tenant → 2601.
  - The same key in a second tenant succeeds.
  - Two reversals of one document → 2601.
  - A duplicate `(DocumentId, LineNo)` → 2601.
- **`Foreign_keys_include_the_tenant`:** as tenant A, a movement whose `ItemId` is tenant B's item → 547 with `FK_` in `Message`. Tenant B's item is created through B's own connection. The same holds for a balance whose `LocationId` is B's location.
- **`Every_inventory_decimal_is_19_4`:**
  - As sa, `SELECT … FROM sys.columns c JOIN sys.tables t … WHERE SCHEMA_NAME(t.schema_id) = N'inventory' AND c.system_type_id = TYPE_ID(N'decimal') AND (c.precision <> 19 OR c.scale <> 4)` returns no rows.
  - The same query over `float`/`real` in `inventory` and `platform` returns no rows.
  - At least 5 decimal columns exist.
- **`Movement_ids_come_from_a_bigint_sequence`:** `sys.sequences` holds `inventory.MovementIds` with `TYPE_NAME(system_type_id) = 'bigint'`, `start_value = 1` and `increment = 1`.
- **`Declared_triggers_match_the_database`** (Hidden coupling):
  - Read the design-time model the way `TenantInterceptorTests.cs:84-89` does (`IDesignTimeModel`). For each entity type, collect `GetDeclaredTriggers()` names into the set `{table|trigger}`.
  - As sa, read `sys.triggers` joined to `sys.tables` in schema `inventory`. Every trigger must have `is_instead_of_trigger = 1`, and its `sys.trigger_events` must be exactly `UPDATE` and `DELETE`.
  - The two sets are equal: `StockDocuments|TR_StockDocuments_AppendOnly` and `StockMovements|TR_StockMovements_AppendOnly`.
- **`Ef_inserts_ledger_rows_as_inventory_app`:**
  - In `app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant)` (copy `ItemsTests.cs:377-402`), take the `InventoryDbContext`.
  - Take two ids with `db.Database.SqlQuery<long>($"SELECT NEXT VALUE FOR [inventory].[MovementIds] AS [Value]").SingleAsync()`.
  - Add one item and one physical location the same way `ItemsTests.AddItemsAsync` does. Then add:
    - a `StockDocument` (Receipt, `RequestHash = new byte[32]`, `PostedAt = DateTimeOffset.UtcNow`, `PostedBy = Guid.NewGuid()`)
    - two `StockMovement`s (lines 1 and 2, SUP → physical)
    - a `StockBalance`
  - One `SaveChangesAsync`, with no user transaction (the retrying strategy forbids one outside `ExecuteAsync`), succeeds.
  - Reading back through a fresh scope finds 1 document, 2 movements and 1 balance, each with `TenantId == tenant`. This proves `HasTrigger`, the interceptor stamp and the `DENY` don't block EF's insert path.

**Conventions:**
- Test classes are `public sealed`, with primary-constructor `DatabaseFixture db`. They use the fixture's `AdminConnectionString`/`AppConnectionString`, `using Platform.Identity;` for `Roles`, and Shouldly.
- Every test creates its own tenant (spec §11 `:629`: the ledger refuses `DELETE`).
- Don't refactor `RowLevelSecurityTests`'s private helpers; copy what you need.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Disable or skip a test. If an expected error number differs, follow the rule in step 4.
- Grant `inventory_app` anything beyond the sequence `UPDATE`.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
```
- `LedgerImmutabilityTests` (4), `LedgerSchemaTests` (all) and `SetupTests.Inventory_app_has_exactly_its_grants` pass, and every other test is still green.
- `grep -n 'DENY UPDATE, DELETE' src/Api/Setup/DatabasePrincipals.cs` shows 2 lines.
