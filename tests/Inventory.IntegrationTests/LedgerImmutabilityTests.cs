using System.Data;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// T7: the ledger is append-only for the owner (triggers) and for <c>inventory_app</c> (permissions),
/// and <c>inventory_app</c> can't switch the guarantees off.
/// </summary>
public sealed class LedgerImmutabilityTests(DatabaseFixture db)
{
    private const int AppendOnlyTrigger = 51000;
    private const int PermissionDenied = 229;
    private const int NotFoundOrNoPermission = 1088;
    // SQL Server 2025 reports a security policy it won't let the caller alter as 33268, not 15151 or 15247.
    private const int PolicyNotFoundOrNoPermission = 33268;
    private const int TruncateReferencedTable = 4712;

    private static readonly string[] LedgerWrites =
    [
        "UPDATE inventory.StockDocuments SET Notes = N'x' WHERE Id = @d",
        "DELETE FROM inventory.StockDocuments WHERE Id = @d",
        "UPDATE inventory.StockMovements SET TotalCost = 1 WHERE Id = @m",
        "DELETE FROM inventory.StockMovements WHERE Id = @m",
    ];

    [Fact]
    public async Task Owner_updates_and_deletes_hit_the_triggers()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await CreateTenantAsync();
        var (document, movement) = await SeedAsync(tenant, ct);

        await using (var owner = new SqlConnection(db.AdminConnectionString))
        {
            await owner.OpenAsync(ct);
            // The RLS filter applies to sa too: without the tenant, each WHERE would match nothing and the trigger wouldn't fire.
            await ExecuteAsync(owner, "EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1;", ct, ("@t", tenant));

            foreach (var sql in LedgerWrites)
            {
                var ex = await Should.ThrowAsync<SqlException>(ExecuteAsync(owner, sql, ct, ("@d", document), ("@m", movement)));
                ex.Number.ShouldBe(AppendOnlyTrigger, sql);
            }
        }

        await AssertUnchangedAsync(tenant, document, movement, ct);
    }

    [Fact]
    public async Task Inventory_app_cannot_update_or_delete_the_ledger()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await CreateTenantAsync();
        var (document, movement) = await SeedAsync(tenant, ct);

        await using (var app = await LedgerRows.OpenAsync(db, tenant, ct))
        {
            foreach (var sql in LedgerWrites)
            {
                // The DENY is checked before the statement runs, so the trigger never fires.
                var ex = await Should.ThrowAsync<SqlException>(ExecuteAsync(app, sql, ct, ("@d", document), ("@m", movement)));
                ex.Number.ShouldBe(PermissionDenied, sql);
            }
        }

        await AssertUnchangedAsync(tenant, document, movement, ct);
    }

    [Fact]
    public async Task Inventory_app_cannot_disable_triggers_truncate_or_alter_the_policy()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await CreateTenantAsync();
        var (document, movement) = await SeedAsync(tenant, ct);

        (string Sql, int Number)[] attempts =
        [
            ("DISABLE TRIGGER [inventory].[TR_StockDocuments_AppendOnly] ON [inventory].[StockDocuments]", NotFoundOrNoPermission),
            ("DISABLE TRIGGER [inventory].[TR_StockMovements_AppendOnly] ON [inventory].[StockMovements]", NotFoundOrNoPermission),
            ("TRUNCATE TABLE [inventory].[StockMovements]", NotFoundOrNoPermission),
            ("TRUNCATE TABLE [inventory].[StockBalances]", NotFoundOrNoPermission),
            ("ALTER SECURITY POLICY [security].[TenantPolicy] WITH (STATE = OFF)", PolicyNotFoundOrNoPermission),
        ];

        await using (var app = await LedgerRows.OpenAsync(db, tenant, ct))
        {
            foreach (var (sql, number) in attempts)
            {
                var ex = await Should.ThrowAsync<SqlException>(ExecuteAsync(app, sql, ct));
                // 4712 would mean the FK stopped TRUNCATE, which proves nothing about the permission.
                ex.Number.ShouldNotBe(TruncateReferencedTable, $"{sql}: {ex.Message}");
                ex.Number.ShouldBe(number, $"{sql}: {ex.Message}");
            }
        }

        await using (var owner = new SqlConnection(db.AdminConnectionString))
        {
            await owner.OpenAsync(ct);
            var disabled = await ScalarAsync<int>(owner,
                "SELECT COUNT(*) FROM sys.triggers WHERE name IN (N'TR_StockDocuments_AppendOnly', N'TR_StockMovements_AppendOnly') AND is_disabled = 0", ct);
            disabled.ShouldBe(2, "both append-only triggers must stay enabled");
            var enabled = await ScalarAsync<bool>(owner,
                "SELECT is_enabled FROM sys.security_policies WHERE object_id = OBJECT_ID(N'security.TenantPolicy')", ct);
            enabled.ShouldBeTrue("security.TenantPolicy must stay enabled");
        }

        await AssertUnchangedAsync(tenant, document, movement, ct);
    }

    [Fact]
    public async Task Inventory_app_can_insert_the_ledger_and_update_balances()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await CreateTenantAsync();
        await using var app = await LedgerRows.OpenAsync(db, tenant, ct);
        var item = await LedgerRows.AddItemAsync(app, NewCode("I"), ct);
        var location = await LedgerRows.AddLocationAsync(app, NewCode("L"), ct);
        var supplier = await LedgerRows.LocationIdAsync(app, "SUP", ct);

        var document = await LedgerRows.AddDocumentAsync(app, ct);
        var movement = await LedgerRows.AddMovementAsync(app, document, 1, item, supplier, location, 10m, 1000m, ct);
        await LedgerRows.AddBalanceAsync(app, item, location, isVirtual: false, allowNegative: false, 0m, 0m, ct);

        movement.ShouldBeGreaterThan(0L);
        var updated = await ExecuteAsync(app,
            "UPDATE inventory.StockBalances SET OnHand = 10, StockValue = 1000 WHERE ItemId = @i AND LocationId = @l", ct,
            ("@i", item), ("@l", location));
        updated.ShouldBe(1);
    }

    private static string NewCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    private async Task<Guid> CreateTenantAsync()
    {
        await using var app = db.CreateApp();
        var (tenant, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        return tenant;
    }

    /// <summary>One item, one physical location, a receipt and a movement SUP → physical (10 / 1000.0000).</summary>
    private async Task<(Guid Document, long Movement)> SeedAsync(Guid tenant, CancellationToken ct)
    {
        await using var app = await LedgerRows.OpenAsync(db, tenant, ct);
        var item = await LedgerRows.AddItemAsync(app, NewCode("I"), ct);
        var location = await LedgerRows.AddLocationAsync(app, NewCode("L"), ct);
        var supplier = await LedgerRows.LocationIdAsync(app, "SUP", ct);
        var document = await LedgerRows.AddDocumentAsync(app, ct);
        var movement = await LedgerRows.AddMovementAsync(app, document, 1, item, supplier, location, 10m, 1000m, ct);
        return (document, movement);
    }

    /// <summary>Reads back as <c>inventory_app</c>: both rows exist, the notes are still NULL and the cost is still 1000.0000.</summary>
    private async Task AssertUnchangedAsync(Guid tenant, Guid document, long movement, CancellationToken ct)
    {
        await using var app = await LedgerRows.OpenAsync(db, tenant, ct);
        (await ScalarAsync<int>(app, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE Id = @d AND Notes IS NULL", ct, ("@d", document)))
            .ShouldBe(1, "the document must exist with NULL notes");
        (await ScalarAsync<decimal>(app, "SELECT TotalCost FROM inventory.StockMovements WHERE Id = @m", ct, ("@m", movement)))
            .ShouldBe(1000.0000m);
    }

    private static SqlCommand Command(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new SqlParameter(name, value switch
            {
                Guid => SqlDbType.UniqueIdentifier,
                long => SqlDbType.BigInt,
                _ => SqlDbType.NVarChar,
            })
            { Value = value });
        }

        return command;
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }
}