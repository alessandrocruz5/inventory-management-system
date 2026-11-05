using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// The plan's rollback (roll the schema back before reverting the code) runs <c>TenantPolicy.Down</c>.
/// It runs here on a throwaway database, so the shared fixture database keeps the full schema.
/// </summary>
public sealed class MigrationRollbackTests(DatabaseFixture db)
{
    private const string Initial = "20261104110425_Initial";

    private const string SecurityObjects =
        "SELECT (SELECT COUNT(*) FROM sys.schemas WHERE name = N'security') " +
        "+ (SELECT COUNT(*) FROM sys.security_policies WHERE name = N'TenantPolicy') " +
        "+ (SELECT COUNT(*) FROM sys.objects WHERE name = N'fn_tenant')";

    private const string TenantIdDefaults =
        "SELECT COUNT(*) FROM sys.default_constraints d " +
        "JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id " +
        "WHERE d.parent_object_id = OBJECT_ID(N'inventory.Locations') AND c.name = N'TenantId'";

    [Fact]
    public async Task TenantPolicy_down_removes_rls_and_the_default_and_up_reapplies()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = "inventory_rollback_" + Guid.NewGuid().ToString("N")[..8];
        await ExecuteAsync(Master(), $"CREATE DATABASE [{database}]", ct);
        try
        {
            var connectionString = new SqlConnectionStringBuilder(db.AdminConnectionString) { InitialCatalog = database }.ConnectionString;
            await using var context = CreateContext(connectionString);
            var migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync(cancellationToken: ct);
            (await ScalarAsync(connectionString, SecurityObjects, ct)).ShouldBe(3);
            (await ScalarAsync(connectionString, TenantIdDefaults, ct)).ShouldBe(1);

            await migrator.MigrateAsync(Initial, ct);
            (await ScalarAsync(connectionString, SecurityObjects, ct)).ShouldBe(0, "Down must drop the policy, fn_tenant and the security schema");
            (await ScalarAsync(connectionString, TenantIdDefaults, ct)).ShouldBe(0, "Down must drop the TenantId default");
            (await context.Database.GetAppliedMigrationsAsync(ct)).ShouldBe([Initial]);

            await migrator.MigrateAsync(cancellationToken: ct);
            (await ScalarAsync(connectionString, SecurityObjects, ct)).ShouldBe(3, "Up must reapply after a rollback");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuteAsync(Master(), $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", CancellationToken.None);
        }
    }

    private string Master() => new SqlConnectionStringBuilder(db.AdminConnectionString) { InitialCatalog = "master" }.ConnectionString;

    // No interceptors: like the migrate step, this context has no tenant and sets no session context.
    private static InventoryDbContext CreateContext(string connectionString) => new(new DbContextOptionsBuilder<InventoryDbContext>()
        .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
        .Options, new NoTenant());

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> ScalarAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    private sealed class NoTenant : ITenantContext
    {
        public bool HasTenant => false;

        public Guid TenantId => throw new InvalidOperationException("The migrate step has no tenant.");
    }
}