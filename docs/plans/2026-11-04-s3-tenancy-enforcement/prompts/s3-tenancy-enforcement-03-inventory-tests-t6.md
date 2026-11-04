---
type: prompt
plan: "[[2026-11-04-s3-tenancy-enforcement]]"
id: 03
unit: IMS-4
status: todo
model: sonnet
effort: high
depends_on: [02]
parallel_group: C
files:
  create:
    - tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj
    - tests/Inventory.IntegrationTests/DatabaseFixture.cs
    - tests/Inventory.IntegrationTests/ApiFactory.cs
    - tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs
  modify:
    - InventorySystem.slnx
    - src/Api/Api.csproj
    - src/Modules/Platform/Platform/Platform.csproj
    - src/Modules/Inventory/Inventory/Inventory.csproj
  delete: []
---
# 03 · Inventory.IntegrationTests and T6

## Task
Create `tests/Inventory.IntegrationTests`, copying the Platform integration fixture, and write **T6**. T6 proves row-level security holds on a raw `SqlConnection` as `inventory_app`, with no EF in the path, and that every tenant table is in the policy.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You're on branch `IMS-4`. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`. Files have **no final newline**.

**Project.** Copy `tests/Platform.IntegrationTests/Platform.IntegrationTests.csproj` exactly:
- `net10.0`, `OutputType Exe`, `IsPackable false`, `<Using Include="Xunit" />`.
- `xunit.v3` 4.0.2, `Shouldly` 4.3.0, `Testcontainers.MsSql` 4.16.0, `Microsoft.AspNetCore.Mvc.Testing` 10.0.14. No `Microsoft.NET.Test.Sdk` (TASKS.md Downstream; MTP comes from `global.json`).
- The same three project references (Api, Platform, Inventory).

Then:
- Add `<Project Path="tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj" />` under `/tests/` in `InventorySystem.slnx` (`:20–23`).
- Add `<InternalsVisibleTo Include="Inventory.IntegrationTests" />` beside the existing `Platform.IntegrationTests` line in `src/Api/Api.csproj` (`:22–24`), `src/Modules/Platform/Platform/Platform.csproj` (`:25–27`) and `src/Modules/Inventory/Inventory/Inventory.csproj` (`:23–25`).

**Fixture.** Copy `tests/Platform.IntegrationTests/DatabaseFixture.cs` and `ApiFactory.cs` verbatim, changing only the namespace to `Inventory.IntegrationTests`, including `[assembly: AssemblyFixture(typeof(Inventory.IntegrationTests.DatabaseFixture))]`.
- It's one SQL Server 2025 container (`mcr.microsoft.com/mssql/server:2025-latest`), migrated once as `sa` through `Api.Setup.MigrateCommand`.
- `AdminConnectionString`, `AppConnectionString` (as `inventory_app`), `CreateAdminApp()`, `CreateApp(...)` and `CreateTenantWithUserAsync(app, role)` stay as they are. The last one provisions the tenant's 3 virtual locations, `SUP`, `CUS` and `ADJ`, through `TenantProvisioner` → `IInventoryModule.ProvisionTenantAsync`.
- Every test creates its own tenants; there's no reset (spec §11 `:629`).

**What exists in the database** (prompts 01–02):
- `inventory.Locations(TenantId, Id, Code nvarchar(32), Name nvarchar(200), Kind varchar(16) CHECK IN ('Physical','Supplier','Customer','Adjustment'), AllowNegativeStock bit DEFAULT 0, IsActive bit DEFAULT 1, CreatedAt datetimeoffset NOT NULL, CreatedBy, UpdatedAt, UpdatedBy, RowVersion rowversion)`.
  - PK `(TenantId, Id)`; `UX_Locations_Code (TenantId, Code)`; `UX_Locations_VirtualKind (TenantId, Kind) WHERE Kind <> 'Physical'`.
  - `Id` has **no** default, so raw inserts pass `NEWID()`.
- `TenantId` defaults to `CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)`.
- `security.fn_tenant(@TenantId)` and `security.TenantPolicy` (`STATE = ON`, `SCHEMABINDING = ON`) carry FILTER + BLOCK `AFTER INSERT` + BLOCK `AFTER UPDATE` on `inventory.Locations`.

**Raw connection helper.** In the test class, write a helper that opens `new SqlConnection(new SqlConnectionStringBuilder(db.AppConnectionString) { Pooling = false }.ConnectionString)`. `Pooling = false` keeps T6 independent of pool resets, which T11 covers. When a tenant is given, it runs `EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1;` with `@t` as a `SqlDbType.UniqueIdentifier` parameter. **No EF** on these connections.

**Error numbers** **\[Likely\]**:
- A BLOCK predicate violation raises `SqlException.Number == 33504` ("…has a block predicate that conflicts with this operation…").
- Re-setting a read-only session key raises **15664**.
- Inserting NULL into `TenantId` raises **515**.

Assert `SqlException` and its `Number`. If a number differs but the message clearly matches, use the observed number and report it.

**`RowLevelSecurityTests` (T6).** Each data test creates tenants A and B with `CreateTenantWithUserAsync(app, Roles.Admin)` on `db.CreateApp()`.
1. `Tenant_A_session_cannot_read_tenant_B_rows`: with A's context, `SELECT COUNT(*) FROM inventory.Locations WHERE TenantId = @b` is 0, and `SELECT COUNT(*) FROM inventory.Locations` is 3 (A's rows only, though other tenants exist).
2. `Tenant_A_session_cannot_insert_tenant_B_rows`: with A's context, `INSERT INTO inventory.Locations (TenantId, Id, Code, Name, Kind, CreatedAt) VALUES (@b, NEWID(), @code, N'T6', 'Physical', SYSDATETIMEOFFSET())` raises 33504. With B's context afterwards, `COUNT(*) WHERE Code = @code` is 0.
3. `Tenant_A_session_cannot_move_a_row_to_tenant_B`: with A's context, `UPDATE inventory.Locations SET TenantId = @b WHERE Code = N'SUP'` raises 33504. A still sees its `SUP` row.
4. `Insert_without_TenantId_takes_the_session_tenant`: with A's context, insert omitting `TenantId` (the `Id, Code, Name, Kind, CreatedAt` columns). Then `SELECT TenantId FROM inventory.Locations WHERE Code = @code` returns A.
5. `No_session_context_sees_nothing_and_cannot_insert`: with no `sp_set_session_context`:
   - `SELECT COUNT(*) FROM inventory.Locations` is 0, though A and B exist.
   - An insert with explicit `TenantId = @a` raises 33504.
   - An insert omitting `TenantId` raises 515 (the default is NULL).
6. `Session_tenant_is_read_only`: after setting A read-only, `EXEC sp_set_session_context @key = N'TenantId', @value = @b` raises 15664. A connection never switches tenant (spec §8 `:512`).

Catalog tests run on an admin `SqlConnection(db.AdminConnectionString)` and need no tenants:

7. `Every_tenant_table_has_a_filter_and_both_block_predicates`. Select every user table with a column named `TenantId` whose schema is **not** `identity`; Identity stays outside the policy by design (spec §8 `:513`, TASKS.md Downstream). For each one, `sys.security_predicates` joined to `sys.security_policies` where the policy is `security.TenantPolicy` has exactly:
   - one `predicate_type = 0` (FILTER),
   - one `predicate_type = 1 AND operation = 1` (BLOCK AFTER INSERT),
   - one `predicate_type = 1 AND operation = 2` (BLOCK AFTER UPDATE),
   - each with a `predicate_definition` containing `fn_tenant` (case-insensitive).

   Also check:
   - The policy has `is_enabled = 1` and `is_schema_bound = 1`.
   - The table list contains `inventory.Locations`, so the check can't pass vacuously.

   Report the failing tables in the assertion message.
8. `Every_tenant_table_defaults_TenantId_to_the_session`: for the same table set, `sys.default_constraints` on the `TenantId` column has a definition containing `session_context(n'tenantid')`, compared case-insensitively. SQL Server stores it as `(CONVERT([uniqueidentifier],session_context(N'TenantId')))`.
9. `Tenant_tables_lead_every_key_with_TenantId`: for the same set, every index with `is_primary_key = 1 OR is_unique = 1` has `TenantId` as its `key_ordinal = 1` column (CLAUDE.md Conventions).

Copy the `ReadStrings` helper and the admin-connection style from `tests/Platform.IntegrationTests/SetupTests.cs:36–48`, `:210–221`. Use `TestContext.Current.CancellationToken`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Use EF on the T6 connections, or connect as `sa` for the data tests (1–6).
- Turn the policy off, or alter any schema. The container is shared by every test in the assembly.
- Add packages, or a shared test-support project.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj   # 9/9
dotnet test                                                    # Architecture 10, Platform 19, Inventory 9; 0 failed
```
- Mutation check: temporarily change test 1's expected cross-tenant count to 1, and confirm it fails. Then revert it. (It must not pass vacuously.)
