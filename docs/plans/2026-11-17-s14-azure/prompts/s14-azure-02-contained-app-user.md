---
type: prompt
plan: "[[2026-11-17-s14-azure]]"
id: 02
unit: IMS-20
status: todo
model: opus
effort: high
depends_on: [01]
parallel_group: B
files:
  create: []
  modify:
    - src/Api/Setup/DatabasePrincipals.cs
    - src/Modules/Platform/Platform/Persistence/Migrations/20261104105221_EnableReadCommittedSnapshot.cs
    - tests/Platform.IntegrationTests/SetupTests.cs
    - README.md
    - CLAUDE.md
  delete: []
---
# 02 · Azure SQL compatibility: contained `inventory_app`, RCSI guard, IMS-20 docs

## Task
1. Make the `migrate` step create `inventory_app` as a **contained database user** when the database is Azure SQL Database or partially contained. Keep today's login-based path, byte for byte, for every other database. The grants and DENYs must stay identical on both paths. Prove the contained path with an integration test on a partially contained database.
2. Guard the existing migration `EnableReadCommittedSnapshot` so it runs on Azure SQL Database, which allows `SET READ_COMMITTED_SNAPSHOT` only from `master` (D14).
3. Update the README's compose "Known limits" line and CLAUDE.md's allowed-package list for prompt 01's Data Protection change (D15).

## Context
- **Repo and branch:** the repo root is `git rev-parse --show-toplevel`. Work on the branch you were given, and stop if `git branch --show-current` isn't it. Use `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`.
- **Why:** S14 runs `migrate` as the Azure SQL server admin against database `inventory` (plan `2026-11-17-s14-azure`, D5, D6). Azure SQL Database can't `CREATE LOGIN` from a user database, and `CHECK_POLICY` isn't valid there. The Azure idiom is a contained user: `CREATE USER [x] WITH PASSWORD = '…'`. Spec §8: the API and seed connect as `inventory_app` (DML only). T7: `UPDATE`/`DELETE` on the ledger fail, and the API's login can't disable triggers or RLS.
- **`src/Api/Setup/DatabasePrincipals.cs`** today (54 lines):
  - `EnsureSql`, at lines 17-33, is one batch. It does three things:
    - Quotes the password with `DECLARE @q nvarchar(300) = QUOTENAME(@p, '''')`.
    - Runs `CREATE LOGIN … CHECK_POLICY = ON`, or `ALTER LOGIN … WITH PASSWORD`, decided by `sys.server_principals`, then `CREATE USER [inventory_app] FOR LOGIN [inventory_app]` if missing.
    - Applies the grant block (lines 24-32: four schema grants, two object DENYs, and `GRANT UPDATE ON OBJECT::[inventory].[MovementIds]`).
  - `EnsureAsync(string adminConnectionString, string password, CancellationToken)` rejects passwords over 128 characters, opens a `SqlConnection`, and runs the batch with `@p` as `NVarChar(128)`.
  - It has one caller: `src/Api/Setup/MigrateCommand.cs:31`.
- **Required design:**
  1. Split the T-SQL into three `const string`s:
     - `LoginSql`: today's password part.
     - `ContainedSql`: `IF USER_ID(N'inventory_app') IS NULL EXEC (N'CREATE USER [inventory_app] WITH PASSWORD = ' + @q + N';') ELSE EXEC (N'ALTER USER [inventory_app] WITH PASSWORD = ' + @q + N';');`, with the same `@q`/`THROW` preamble.
     - `GrantsSql`: today's grant block, unchanged, comments included.
     - Run the chosen principal batch, then `GrantsSql`, on the same connection. Keep the password only ever in `@p` and inside T-SQL through `QUOTENAME`, never concatenated in C#.
  2. Decide the path in C#, on the open connection, with a private `static async Task<bool> IsContainedAsync(SqlConnection, CancellationToken)`:
     - First run `SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)`; `5` (Azure SQL Database) means contained.
     - Otherwise run `SELECT containment FROM sys.databases WHERE database_id = DB_ID()`; a value of `1` means contained.
     - Two separate commands, so the `containment` query never compiles on Azure.
     - Update the class `<summary>` to say it creates a login-mapped user on SQL Server and a contained user on Azure SQL Database or a partially contained database.
  3. Leave `EnsureAsync`'s signature, the 128 check and the comments' intent unchanged.
- **RCSI migration (D14)** `src/Modules/Platform/Platform/Persistence/Migrations/20261104105221_EnableReadCommittedSnapshot.cs`:
  - Today `Up` is `migrationBuilder.Sql("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;", suppressTransaction: true);`.
  - Microsoft Learn (ALTER DATABASE SET options, Azure SQL Database): *"In Azure SQL Database, the `ALTER DATABASE` command to set READ_COMMITTED_SNAPSHOT ON or OFF for a database must be executed in the `master` database."* EF runs migrations in the user database, so the first Azure `migrate` would fail here. Azure SQL Database turns RCSI on by default.
  - Replace `Up`'s body with exactly this. Keep `suppressTransaction: true`, and use a raw string literal:
    ```csharp
    // Azure SQL Database has RCSI on by default and only lets master change it, so skip when it's already on (S14, D14).
    migrationBuilder.Sql(
        """
        IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) = 0
        BEGIN
            IF CAST(SERVERPROPERTY('EngineEdition') AS int) = 5 THROW 50000, 'READ_COMMITTED_SNAPSHOT is OFF on this Azure SQL database: turn it on from master, then rerun migrate', 1;
            EXEC (N'ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;');
        END
        """,
        suppressTransaction: true);
    ```
  - Leave `Down` unchanged. A rollback on Azure isn't supported, and that's a non-goal.
  - Don't touch the `.Designer.cs` or the model snapshot. The model doesn't change, so `has-pending-model-changes` must still say "No changes".
  - Databases that have already applied this migration (dev, compose volumes) skip it, because EF tracks migration ids, not bodies. On a fresh SQL Server database RCSI is off, so the `ALTER` still runs, and `SetupTests.Read_committed_snapshot_is_on` keeps passing unchanged.
  - The `THROW` makes a successful Azure `migrate` job prove RCSI is on.
- **README.md** `## Docker Compose`: its last line today is `**Known limits:** sign-in sessions don't survive recreating the \`api\` container (Data Protection keys aren't persisted).`. Replace it with `**Sessions:** Data Protection keys live in \`platform.DataProtectionKeys\`, so a sign-in survives recreating the \`api\` container.`
- **CLAUDE.md** "Allowed packages" paragraph: add `` `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` `` after `` `Microsoft.AspNetCore.Identity.EntityFrameworkCore` ``. Prompt 01 added that package, user-approved (D3). Change nothing else.
- **Test file `tests/Platform.IntegrationTests/SetupTests.cs`:**
  - `public sealed class SetupTests(DatabaseFixture db)` has `Inventory_app_has_exactly_its_grants` at lines 66-123. It opens `new SqlConnection(db.AdminConnectionString)` and asserts four things with `ReadStrings(connection, ct, sql)` (line 289): the exact schema grants, the exact object grants/DENYs, no role memberships, and no `ALTER%`/`CONTROL` database permission.
  - Move those four assertions, unchanged, into `private static async Task ShouldHaveExactlyTheAppGrants(SqlConnection connection, CancellationToken ct)`. Call it from the existing test. That test also asserts `SELECT authentication_type_desc FROM sys.database_principals WHERE name = N'inventory_app'` is `"INSTANCE"`.
- **New test** `[Fact] public async Task Contained_database_gets_a_contained_inventory_app_user()`:
  1. Open an admin connection to master: `new SqlConnectionStringBuilder(db.AdminConnectionString) { InitialCatalog = "master" }`. Then run:
     - `EXEC sp_configure 'contained database authentication', 1; RECONFIGURE;`
     - `CREATE DATABASE [<name>];` and `ALTER DATABASE [<name>] SET CONTAINMENT = PARTIAL;`, with `<name> = "contained_" + Guid.NewGuid().ToString("N")[..8]`.
     - The server-wide setting doesn't affect non-contained databases.
  2. Migrate it with `await using var admin = new ApiFactory(<admin string with InitialCatalog = name>, ("InventoryApp:Password", p1));`, then `await MigrateCommand.RunAsync(admin.Services, admin.Services.GetRequiredService<IConfiguration>(), ct);`.
     - This copies `DatabaseFixture.cs:39-42`.
     - `p1 = "App!Passw0rd-" + Guid.NewGuid().ToString("N")[..8]`, the fixture's pattern, which meets the password policy.
  3. On that database, as admin:
     - `authentication_type_desc` for `inventory_app` is `"DATABASE"`.
     - `await ShouldHaveExactlyTheAppGrants(connection, ct)`.
     - `SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()` is `1`. The guarded migration's `ALTER` branch ran on this fresh database.
  4. Sign in as the contained user: `new SqlConnectionStringBuilder(<contained admin string>) { UserID = "inventory_app", Password = p1 }`, then open and run `SELECT USER_NAME()`, which must be `inventory_app`.
     - A server login with the same name already exists from the fixture's database, with a different password. Connecting with the contained database as the initial catalog authenticates the contained user first, which is what this proves.
  5. Run migrate again with a new password `p2`. Opening with `p2` succeeds, and opening with `p1` throws `SqlException`. This is the `ALTER USER` path.
  6. Don't drop the database: the container is disposed at the end of the run.
- **Conventions:** Shouldly, `TestContext.Current.CancellationToken`, xUnit v3. No `IgnoreQueryFilters()`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change a single grant, DENY or role: `Inventory_app_has_exactly_its_grants` must keep passing with an unchanged expected list.
- Create a login on the contained path, or use `CHECK_POLICY` there.
- Add config switches. The path is detected, never configured.
- Change the RCSI migration's `Down`, its Designer file, the snapshot, or any other migration. Add no new migration.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/Modules/Platform/Platform --startup-project src/Api --context PlatformDbContext   # "No changes"
dotnet test --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false && cd ..
grep -n 'DataProtection.EntityFrameworkCore' CLAUDE.md && grep -n 'platform.DataProtectionKeys' README.md
```
- `Contained_database_gets_a_contained_inventory_app_user`, `Inventory_app_has_exactly_its_grants`, `Read_committed_snapshot_is_on` and `Migrate_is_idempotent` pass, and every other test is unchanged and green.
- `git diff --stat` lists only the `files` above. The migration diff touches only `Up`.
- `git diff src/Api/Setup/DatabasePrincipals.cs` shows the original login SQL and grant block, moved but textually unchanged.
