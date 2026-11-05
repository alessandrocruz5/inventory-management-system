using System.Data;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// T6: row-level security holds on a raw connection as <c>inventory_app</c>, with no EF in the path,
/// and every tenant table sits in <c>security.TenantPolicy</c>.
/// </summary>
public sealed class RowLevelSecurityTests(DatabaseFixture db)
{
    private const int BlockPredicateViolation = 33504;
    private const int ReadOnlySessionKey = 15664;
    private const int NullIntoNotNull = 515;

    private const string InsertWithTenant =
        "INSERT INTO inventory.Locations (TenantId, Id, Code, Name, Kind, CreatedAt) " +
        "VALUES (@tenant, NEWID(), @code, N'T6', 'Physical', SYSDATETIMEOFFSET())";

    private const string InsertWithoutTenant =
        "INSERT INTO inventory.Locations (Id, Code, Name, Kind, CreatedAt) " +
        "VALUES (NEWID(), @code, N'T6', 'Physical', SYSDATETIMEOFFSET())";

    private const string TenantTables =
        "SELECT SCHEMA_NAME(t.schema_id) + N'.' + t.name " +
        "FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id AND c.name = N'TenantId' " +
        "WHERE SCHEMA_NAME(t.schema_id) <> N'identity'";

    [Fact]
    public async Task Tenant_A_session_cannot_read_tenant_B_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await CreateTenantsAsync();
        await using var connection = await OpenAppAsync(a, ct);

        var ofB = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.Locations WHERE TenantId = @b", ct, ("@b", b));
        var all = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.Locations", ct);

        ofB.ShouldBe(0);
        all.ShouldBe(3);
    }

    [Fact]
    public async Task Tenant_A_session_cannot_insert_tenant_B_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await CreateTenantsAsync();
        var code = NewCode();

        await using (var asA = await OpenAppAsync(a, ct))
        {
            var ex = await Should.ThrowAsync<SqlException>(
                ExecuteAsync(asA, InsertWithTenant, ct, ("@tenant", b), ("@code", code)));
            ex.Number.ShouldBe(BlockPredicateViolation);
        }

        await using var asB = await OpenAppAsync(b, ct);
        (await ScalarAsync<int>(asB, "SELECT COUNT(*) FROM inventory.Locations WHERE Code = @code", ct, ("@code", code))).ShouldBe(0);
    }

    [Fact]
    public async Task Tenant_A_session_cannot_move_a_row_to_tenant_B()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await CreateTenantsAsync();
        var code = NewCode();
        await using var connection = await OpenAppAsync(a, ct);

        // A fresh Physical row: B's own SUP row would trip the (TenantId, Code) unique index before the predicate runs.
        await ExecuteAsync(connection, InsertWithoutTenant, ct, ("@code", code));

        var ex = await Should.ThrowAsync<SqlException>(
            ExecuteAsync(connection, "UPDATE inventory.Locations SET TenantId = @b WHERE Code = @code", ct, ("@b", b), ("@code", code)));

        ex.Number.ShouldBe(BlockPredicateViolation);
        (await ScalarAsync<Guid>(connection, "SELECT TenantId FROM inventory.Locations WHERE Code = @code", ct, ("@code", code))).ShouldBe(a);
    }

    [Fact]
    public async Task Insert_without_TenantId_takes_the_session_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, _) = await CreateTenantsAsync();
        var code = NewCode();
        await using var connection = await OpenAppAsync(a, ct);

        await ExecuteAsync(connection, InsertWithoutTenant, ct, ("@code", code));

        var tenant = await ScalarAsync<Guid>(connection, "SELECT TenantId FROM inventory.Locations WHERE Code = @code", ct, ("@code", code));
        tenant.ShouldBe(a);
    }

    [Fact]
    public async Task No_session_context_sees_nothing_and_cannot_insert()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, _) = await CreateTenantsAsync();
        await using var connection = await OpenAppAsync(null, ct);

        (await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.Locations", ct)).ShouldBe(0);

        var explicitTenant = await Should.ThrowAsync<SqlException>(
            ExecuteAsync(connection, InsertWithTenant, ct, ("@tenant", a), ("@code", NewCode())));
        explicitTenant.Number.ShouldBe(BlockPredicateViolation);

        var noTenant = await Should.ThrowAsync<SqlException>(
            ExecuteAsync(connection, InsertWithoutTenant, ct, ("@code", NewCode())));
        noTenant.Number.ShouldBe(NullIntoNotNull);
    }

    [Fact]
    public async Task Session_tenant_is_read_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await CreateTenantsAsync();
        await using var connection = await OpenAppAsync(a, ct);

        var ex = await Should.ThrowAsync<SqlException>(ExecuteAsync(
            connection, "EXEC sp_set_session_context @key = N'TenantId', @value = @b;", ct, ("@b", b)));

        ex.Number.ShouldBe(ReadOnlySessionKey);
    }

    [Fact]
    public async Task Every_tenant_table_has_a_filter_and_both_block_predicates()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await OpenAdminAsync(ct);

        var policy = await ReadRowAsync(connection, ct,
            "SELECT is_enabled, is_schema_bound FROM sys.security_policies WHERE object_id = OBJECT_ID(N'security.TenantPolicy')");
        policy.ShouldNotBeNull("security.TenantPolicy is missing");
        policy.ShouldBe([true, true], "security.TenantPolicy must be enabled and schema-bound");

        var tables = await ReadStrings(connection, ct, TenantTables);
        tables.ShouldContain("inventory.Locations");

        var failing = new List<string>();
        foreach (var table in tables)
        {
            var predicates = await ReadStrings(connection, ct,
                "SELECT CAST(p.predicate_type AS nvarchar(4)) + N'|' + CAST(ISNULL(p.operation, 0) AS nvarchar(4)) + N'|' + p.predicate_definition " +
                "FROM sys.security_predicates p " +
                "JOIN sys.security_policies sp ON sp.object_id = p.object_id " +
                $"WHERE sp.object_id = OBJECT_ID(N'security.TenantPolicy') AND p.target_object_id = OBJECT_ID(N'{table}')");

            var rows = predicates.Select(p => p.Split('|', 3)).ToList();
            bool Has(string type, string operation) =>
                rows.Count(r => r[0] == type && r[1] == operation && r[2].Contains("fn_tenant", StringComparison.OrdinalIgnoreCase)) == 1;

            // predicate_type 0 = FILTER, 1 = BLOCK; operation 1 = AFTER INSERT, 2 = AFTER UPDATE.
            if (rows.Count != 3 || !Has("0", "0") || !Has("1", "1") || !Has("1", "2"))
            {
                failing.Add(table);
            }
        }

        failing.ShouldBeEmpty($"Tables without exactly one FILTER, one BLOCK AFTER INSERT and one BLOCK AFTER UPDATE: {string.Join(", ", failing)}");
    }

    [Fact]
    public async Task Every_tenant_table_defaults_TenantId_to_the_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await OpenAdminAsync(ct);

        var tables = await ReadStrings(connection, ct, TenantTables);
        tables.ShouldContain("inventory.Locations");

        var defaults = await ReadStrings(connection, ct,
            "SELECT SCHEMA_NAME(t.schema_id) + N'.' + t.name + N'|' + dc.definition " +
            "FROM sys.default_constraints dc " +
            "JOIN sys.tables t ON t.object_id = dc.parent_object_id " +
            "JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id " +
            "WHERE c.name = N'TenantId'");
        var withSessionDefault = defaults
            .Select(d => d.Split('|', 2))
            .Where(d => d[1].Contains("session_context(n'tenantid')", StringComparison.OrdinalIgnoreCase))
            .Select(d => d[0])
            .ToHashSet();

        var failing = tables.Where(t => !withSessionDefault.Contains(t)).ToList();
        failing.ShouldBeEmpty($"Tables whose TenantId has no SESSION_CONTEXT default: {string.Join(", ", failing)}");
    }

    [Fact]
    public async Task Tenant_tables_lead_every_key_with_TenantId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await OpenAdminAsync(ct);

        var tables = await ReadStrings(connection, ct, TenantTables);
        tables.ShouldContain("inventory.Locations");

        var failing = await ReadStrings(connection, ct,
            "SELECT SCHEMA_NAME(t.schema_id) + N'.' + t.name + N'.' + i.name " +
            "FROM sys.indexes i " +
            "JOIN sys.tables t ON t.object_id = i.object_id " +
            "WHERE (i.is_primary_key = 1 OR i.is_unique = 1) " +
            "AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'TenantId') " +
            "AND SCHEMA_NAME(t.schema_id) <> N'identity' " +
            "AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'TenantId')");

        failing.ShouldBeEmpty($"Keys that do not lead with TenantId: {string.Join(", ", failing)}");
    }

    private static string NewCode() => "T6-" + Guid.NewGuid().ToString("N")[..12];

    private async Task<(Guid A, Guid B)> CreateTenantsAsync()
    {
        await using var app = db.CreateApp();
        var (a, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        return (a, b);
    }

    /// <summary>A raw connection as <c>inventory_app</c>, unpooled so T6 does not depend on pool resets (T11 covers those).</summary>
    private async Task<SqlConnection> OpenAppAsync(Guid? tenant, CancellationToken ct)
    {
        var connection = new SqlConnection(new SqlConnectionStringBuilder(db.AppConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(ct);
        if (tenant is { } id)
        {
            await ExecuteAsync(connection, "EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1;", ct, ("@t", id));
        }

        return connection;
    }

    private async Task<SqlConnection> OpenAdminAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(db.AdminConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static SqlCommand Command(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new SqlParameter(name, value is Guid ? SqlDbType.UniqueIdentifier : SqlDbType.NVarChar) { Value = value });
        }

        return command;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<bool[]?> ReadRowAsync(SqlConnection connection, CancellationToken ct, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? [reader.GetBoolean(0), reader.GetBoolean(1)] : null;
    }

    private static async Task<List<string>> ReadStrings(SqlConnection connection, CancellationToken ct, string sql)
    {
        var values = new List<string>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            values.Add(reader.GetString(reader.FieldCount - 1));
        }

        return values;
    }
}