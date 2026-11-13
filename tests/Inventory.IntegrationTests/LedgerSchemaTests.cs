using System.Data;

using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// The S5a schema: spec §5 CHECKs, keys and composite FKs; decimal(19,4); the sequence; triggers matching <c>HasTrigger</c>.
/// </summary>
public sealed class LedgerSchemaTests(DatabaseFixture db)
{
    private const int ConstraintViolation = 547;
    private const int DuplicateKey = 2601;

    [Theory]
    [InlineData("document-type", "CK_StockDocuments_Type")]
    [InlineData("reversal-without-target", "CK_StockDocuments_Reversal")]
    [InlineData("receipt-with-target", "CK_StockDocuments_Reversal")]
    [InlineData("adjustment-without-reason", "CK_StockDocuments_ReasonCode")]
    [InlineData("adjustment-bogus-reason", "CK_StockDocuments_ReasonCode")]
    [InlineData("receipt-with-reason", "CK_StockDocuments_ReasonCode")]
    [InlineData("movement-same-location", "CK_StockMovements_Locations")]
    [InlineData("movement-zero", "CK_StockMovements_Quantity")]
    [InlineData("movement-negative-quantity", "CK_StockMovements_Quantity")]
    [InlineData("movement-negative-cost", "CK_StockMovements_TotalCost")]
    [InlineData("physical-negative", "CK_StockBalances_Physical")]
    [InlineData("physical-value-without-stock", "CK_StockBalances_Physical")]
    [InlineData("physical-negative-value", "CK_StockBalances_Physical")]
    [InlineData("virtual-value", "CK_StockBalances_VirtualValue")]
    public async Task Checks_reject_bad_rows(string @case, string check)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await CreateTenantAsync();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);

        // Every row satisfies every FK, so the CHECK is the only constraint it breaks (FK and CHECK violations are both 547).
        var item = await LedgerRows.AddItemAsync(c, NewCode("I"), ct);
        var physical = await LedgerRows.AddLocationAsync(c, NewCode("L"), ct);
        var supplier = await LedgerRows.LocationIdAsync(c, "SUP", ct);
        var receipt = await LedgerRows.AddDocumentAsync(c, ct);

        Func<Task> insert = @case switch
        {
            "document-type" => () => LedgerRows.AddDocumentAsync(c, ct, type: "Bogus"),
            "reversal-without-target" => () => LedgerRows.AddDocumentAsync(c, ct, type: "Reversal"),
            "receipt-with-target" => () => LedgerRows.AddDocumentAsync(c, ct, type: "Receipt", reverses: receipt),
            "adjustment-without-reason" => () => LedgerRows.AddDocumentAsync(c, ct, type: "Adjustment"),
            "adjustment-bogus-reason" => () => LedgerRows.AddDocumentAsync(c, ct, type: "Adjustment", reasonCode: "Bogus"),
            "receipt-with-reason" => () => LedgerRows.AddDocumentAsync(c, ct, type: "Receipt", reasonCode: "Damaged"),
            "movement-same-location" => () => LedgerRows.AddMovementAsync(c, receipt, 1, item, physical, physical, 1m, 10m, ct),
            "movement-zero" => () => LedgerRows.AddMovementAsync(c, receipt, 1, item, supplier, physical, 0m, 0m, ct),
            "movement-negative-quantity" => () => LedgerRows.AddMovementAsync(c, receipt, 1, item, supplier, physical, -1m, 10m, ct),
            "movement-negative-cost" => () => LedgerRows.AddMovementAsync(c, receipt, 1, item, supplier, physical, 1m, -1m, ct),
            "physical-negative" => () => LedgerRows.AddBalanceAsync(c, item, physical, isVirtual: false, allowNegative: false, -1m, 0m, ct),
            "physical-value-without-stock" => () => LedgerRows.AddBalanceAsync(c, item, physical, isVirtual: false, allowNegative: false, 0m, 5m, ct),
            "physical-negative-value" => () => LedgerRows.AddBalanceAsync(c, item, physical, isVirtual: false, allowNegative: false, 1m, -1m, ct),
            "virtual-value" => () => LedgerRows.AddBalanceAsync(c, item, supplier, isVirtual: true, allowNegative: false, -5m, 1m, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };

        var ex = await Should.ThrowAsync<SqlException>(insert);
        ex.Number.ShouldBe(ConstraintViolation);
        ex.Message.ShouldContain(check);
    }

    [Fact]
    public async Task Checks_accept_the_edge_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await CreateTenantAsync();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var item = await LedgerRows.AddItemAsync(c, NewCode("I"), ct);
        var physical = await LedgerRows.AddLocationAsync(c, NewCode("L"), ct);
        var negative = await LedgerRows.AddLocationAsync(c, NewCode("N"), ct, allowNegative: true);
        var supplier = await LedgerRows.LocationIdAsync(c, "SUP", ct);
        var receipt = await LedgerRows.AddDocumentAsync(c, ct);

        // A revaluation movement: no quantity, only cost (spec §7).
        await LedgerRows.AddMovementAsync(c, receipt, 1001, item, supplier, physical, 0m, 18.0000m, ct);
        await LedgerRows.AddBalanceAsync(c, item, negative, isVirtual: false, allowNegative: true, -2m, -100m, ct);
        await LedgerRows.AddBalanceAsync(c, item, supplier, isVirtual: true, allowNegative: false, -10m, 0m, ct);
        await LedgerRows.AddDocumentAsync(c, ct, type: "Adjustment", reasonCode: "CountCorrection");
        await LedgerRows.AddDocumentAsync(c, ct, type: "Reversal", reverses: receipt);

        (await ScalarAsync<int>(c, "SELECT COUNT(*) FROM inventory.StockDocuments", ct)).ShouldBe(3);
        (await ScalarAsync<int>(c, "SELECT COUNT(*) FROM inventory.StockMovements", ct)).ShouldBe(1);
        (await ScalarAsync<int>(c, "SELECT COUNT(*) FROM inventory.StockBalances", ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Unique_keys_hold()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await CreateTenantAsync();
        var b = await CreateTenantAsync();
        var key = "K-" + Guid.NewGuid().ToString("N");

        await using (var asB = await LedgerRows.OpenAsync(db, b, ct))
        {
            await LedgerRows.AddDocumentAsync(asB, ct, idempotencyKey: key);
        }

        await using var asA = await LedgerRows.OpenAsync(db, a, ct);
        var item = await LedgerRows.AddItemAsync(asA, NewCode("I"), ct);
        var physical = await LedgerRows.AddLocationAsync(asA, NewCode("L"), ct);
        var supplier = await LedgerRows.LocationIdAsync(asA, "SUP", ct);

        // The same key in another tenant is a different key: B's document went in first.
        var receipt = await LedgerRows.AddDocumentAsync(asA, ct, idempotencyKey: key);
        var sameKey = await Should.ThrowAsync<SqlException>(LedgerRows.AddDocumentAsync(asA, ct, idempotencyKey: key));
        sameKey.Number.ShouldBe(DuplicateKey);
        sameKey.Message.ShouldContain("UX_StockDocuments_IdempotencyKey");

        await LedgerRows.AddDocumentAsync(asA, ct, type: "Reversal", reverses: receipt);
        var secondReversal = await Should.ThrowAsync<SqlException>(LedgerRows.AddDocumentAsync(asA, ct, type: "Reversal", reverses: receipt));
        secondReversal.Number.ShouldBe(DuplicateKey);
        secondReversal.Message.ShouldContain("UX_StockDocuments_Reverses");

        await LedgerRows.AddMovementAsync(asA, receipt, 1, item, supplier, physical, 1m, 10m, ct);
        var sameLine = await Should.ThrowAsync<SqlException>(LedgerRows.AddMovementAsync(asA, receipt, 1, item, supplier, physical, 2m, 20m, ct));
        sameLine.Number.ShouldBe(DuplicateKey);
        sameLine.Message.ShouldContain("UX_StockMovements_DocumentLine");
    }

    [Fact]
    public async Task Foreign_keys_include_the_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await CreateTenantAsync();
        var b = await CreateTenantAsync();

        Guid itemOfB;
        Guid locationOfB;
        await using (var asB = await LedgerRows.OpenAsync(db, b, ct))
        {
            itemOfB = await LedgerRows.AddItemAsync(asB, NewCode("I"), ct);
            locationOfB = await LedgerRows.AddLocationAsync(asB, NewCode("L"), ct);
        }

        await using var asA = await LedgerRows.OpenAsync(db, a, ct);
        var item = await LedgerRows.AddItemAsync(asA, NewCode("I"), ct);
        var physical = await LedgerRows.AddLocationAsync(asA, NewCode("L"), ct);
        var supplier = await LedgerRows.LocationIdAsync(asA, "SUP", ct);
        var receipt = await LedgerRows.AddDocumentAsync(asA, ct);

        var movement = await Should.ThrowAsync<SqlException>(
            LedgerRows.AddMovementAsync(asA, receipt, 1, itemOfB, supplier, physical, 1m, 10m, ct));
        movement.Number.ShouldBe(ConstraintViolation);
        movement.Message.ShouldContain("FK_StockMovements_Items_TenantId_ItemId");

        var balance = await Should.ThrowAsync<SqlException>(
            LedgerRows.AddBalanceAsync(asA, item, locationOfB, isVirtual: false, allowNegative: false, 0m, 0m, ct));
        balance.Number.ShouldBe(ConstraintViolation);
        balance.Message.ShouldContain("FK_StockBalances_Locations_TenantId_LocationId");
    }

    [Fact]
    public async Task Every_inventory_decimal_is_19_4()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var admin = await OpenAdminAsync(ct);
        const string Columns =
            "SELECT SCHEMA_NAME(t.schema_id) + N'.' + t.name + N'.' + c.name " +
            "FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id ";

        var wrongScale = await ReadStringsAsync(admin, ct, Columns +
            "WHERE SCHEMA_NAME(t.schema_id) = N'inventory' AND c.system_type_id = TYPE_ID(N'decimal') AND (c.precision <> 19 OR c.scale <> 4)");
        wrongScale.ShouldBeEmpty($"decimal columns that are not decimal(19,4): {string.Join(", ", wrongScale)}");

        var floating = await ReadStringsAsync(admin, ct, Columns +
            "WHERE SCHEMA_NAME(t.schema_id) IN (N'inventory', N'platform') AND c.system_type_id IN (TYPE_ID(N'float'), TYPE_ID(N'real'))");
        floating.ShouldBeEmpty($"float or real columns: {string.Join(", ", floating)}");

        var decimals = await ReadStringsAsync(admin, ct, Columns +
            "WHERE SCHEMA_NAME(t.schema_id) = N'inventory' AND c.system_type_id = TYPE_ID(N'decimal')");
        decimals.Count.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task Movement_ids_come_from_a_bigint_sequence()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var admin = await OpenAdminAsync(ct);

        var sequence = await ReadStringsAsync(admin, ct,
            "SELECT TYPE_NAME(system_type_id) + N'|' + CAST(CAST(start_value AS bigint) AS nvarchar(20)) + N'|' + CAST(CAST(increment AS bigint) AS nvarchar(20)) " +
            "FROM sys.sequences WHERE object_id = OBJECT_ID(N'inventory.MovementIds')");

        sequence.ShouldBe(["bigint|1|1"]);
    }

    [Fact]
    public async Task Documents_posted_at_index_leads_with_TenantId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var admin = await OpenAdminAsync(ct);

        var unique = await ReadStringsAsync(admin, ct,
            "SELECT CAST(i.is_unique AS nvarchar(1)) FROM sys.indexes i " +
            "WHERE i.object_id = OBJECT_ID(N'inventory.StockDocuments') AND i.name = N'IX_StockDocuments_PostedAt'");
        unique.ShouldBe(["0"], "IX_StockDocuments_PostedAt must exist and be non-unique");

        var keys = await ReadStringsAsync(admin, ct,
            "SELECT c.name FROM sys.indexes i " +
            "JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id " +
            "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "WHERE i.object_id = OBJECT_ID(N'inventory.StockDocuments') AND i.name = N'IX_StockDocuments_PostedAt' AND ic.is_included_column = 0 " +
            "ORDER BY ic.key_ordinal");
        keys.ShouldBe(["TenantId", "PostedAt"]);
    }

    [Fact]
    public async Task Declared_triggers_match_the_database()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        await using var scope = app.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // The design-time model: the runtime model may drop design-time annotations.
        var model = ((IInfrastructure<IServiceProvider>)inventory).Instance.GetRequiredService<IDesignTimeModel>().Model;
        var declared = model.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredTriggers().Select(t => $"{e.GetTableName()}|{t.GetDatabaseName()}"))
            .Order()
            .ToList();

        await using var admin = await OpenAdminAsync(ct);
        var triggers = await ReadStringsAsync(admin, ct,
            "SELECT t.name COLLATE DATABASE_DEFAULT + N'|' + tr.name COLLATE DATABASE_DEFAULT + N'|' + CAST(tr.is_instead_of_trigger AS nvarchar(1)) + N'|' + " +
            "ISNULL((SELECT STRING_AGG(e.type_desc COLLATE DATABASE_DEFAULT, N',') WITHIN GROUP (ORDER BY e.type_desc) " +
            "FROM sys.trigger_events e WHERE e.object_id = tr.object_id), N'') " +
            "FROM sys.triggers tr JOIN sys.tables t ON t.object_id = tr.parent_id " +
            "WHERE SCHEMA_NAME(t.schema_id) = N'inventory'");
        var rows = triggers.Select(t => t.Split('|')).ToList();

        foreach (var row in rows)
        {
            row[2].ShouldBe("1", $"{row[1]} must be INSTEAD OF");
            row[3].ShouldBe("DELETE,UPDATE", $"{row[1]} must fire on exactly UPDATE and DELETE");
        }

        var actual = rows.Select(r => $"{r[0]}|{r[1]}").Order().ToList();
        actual.ShouldBe(declared);
        actual.ShouldBe(["StockDocuments|TR_StockDocuments_AppendOnly", "StockMovements|TR_StockMovements_AppendOnly"]);
    }

    [Fact]
    public async Task Ef_inserts_ledger_rows_as_inventory_app()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var tenant = await CreateTenantAsync();
        var scopes = app.Services.GetRequiredService<ITenantScopeFactory>();
        Guid documentId;

        await using (var scope = scopes.BeginScope(tenant))
        {
            var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
            // Not SingleAsync: composing wraps the SQL in a derived table, where NEXT VALUE FOR is not allowed (error 11719).
            var first = (await inventory.Database.SqlQuery<long>($"SELECT NEXT VALUE FOR [inventory].[MovementIds] AS [Value]").ToListAsync(ct)).Single();
            var second = (await inventory.Database.SqlQuery<long>($"SELECT NEXT VALUE FOR [inventory].[MovementIds] AS [Value]").ToListAsync(ct)).Single();

            var item = new Item { Sku = NewCode("I"), Name = "Ledger", BaseUnit = "pc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            var location = new Location { Code = NewCode("L"), Name = "Ledger", Kind = LocationKind.Physical, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            inventory.Items.Add(item);
            inventory.Locations.Add(location);
            var supplier = await inventory.Locations.Where(l => l.Code == "SUP").Select(l => l.Id).SingleAsync(ct);

            var document = new StockDocument
            {
                Type = StockDocumentType.Receipt,
                IdempotencyKey = Guid.NewGuid().ToString(),
                RequestHash = new byte[32],
                PostedAt = DateTimeOffset.UtcNow,
                PostedBy = Guid.NewGuid(),
            };
            inventory.StockDocuments.Add(document);
            documentId = document.Id;

            foreach (var (id, line) in new[] { (first, 1), (second, 2) })
            {
                inventory.StockMovements.Add(new StockMovement
                {
                    Id = id,
                    DocumentId = document.Id,
                    LineNo = line,
                    ItemId = item.Id,
                    FromLocationId = supplier,
                    ToLocationId = location.Id,
                    Quantity = 5m,
                    TotalCost = 500m,
                    PostedAt = document.PostedAt,
                    PostedBy = document.PostedBy,
                });
            }

            inventory.StockBalances.Add(new StockBalance
            {
                ItemId = item.Id,
                LocationId = location.Id,
                OnHand = 10m,
                StockValue = 1000m,
                LastUnitCost = 100m,
                LastMovementId = second,
            });

            // No user transaction: the retrying strategy forbids one outside ExecuteAsync.
            await inventory.SaveChangesAsync(ct);
        }

        await using (var scope = scopes.BeginScope(tenant))
        {
            var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
            var documents = await inventory.StockDocuments.Where(d => d.Id == documentId).ToListAsync(ct);
            var movements = await inventory.StockMovements.Where(m => m.DocumentId == documentId).ToListAsync(ct);
            var movementItem = movements.Select(m => m.ItemId).Distinct().Single();
            var balances = await inventory.StockBalances.Where(b => b.ItemId == movementItem).ToListAsync(ct);

            documents.Count.ShouldBe(1);
            movements.Count.ShouldBe(2);
            balances.Count.ShouldBe(1);
            documents.ShouldAllBe(d => d.TenantId == tenant);
            movements.ShouldAllBe(m => m.TenantId == tenant);
            balances.ShouldAllBe(b => b.TenantId == tenant);
        }
    }

    private static string NewCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    private async Task<Guid> CreateTenantAsync()
    {
        await using var app = db.CreateApp();
        var (tenant, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        return tenant;
    }

    private async Task<SqlConnection> OpenAdminAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(db.AdminConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<List<string>> ReadStringsAsync(SqlConnection connection, CancellationToken ct, string sql)
    {
        var values = new List<string>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}