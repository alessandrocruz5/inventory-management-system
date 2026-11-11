using System.Globalization;

using Api.Setup;

using Inventory.Contracts;
using Inventory.Persistence;
using Inventory.Seeding;

using Kernel.Tenancy;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;
using Platform.Persistence;
using Platform.Seeding;

using Shouldly;

namespace Platform.IntegrationTests;

public sealed class SetupTests(DatabaseFixture db)
{
    [Fact]
    public async Task Api_connects_as_inventory_app()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var login = await context.Database.SqlQuery<string>($"SELECT SUSER_NAME() AS [Value]").SingleAsync(ct);

        login.ShouldBe("inventory_app");
    }

    [Fact]
    public async Task Read_committed_snapshot_is_on()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(db.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'inventory'", connection);

        var on = await command.ExecuteScalarAsync(ct);

        Convert.ToInt32(on).ShouldBe(1);
    }

    [Fact]
    public async Task Migrate_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var admin = db.CreateAdminApp();

        await MigrateCommand.RunAsync(admin.Services, admin.Services.GetRequiredService<IConfiguration>(), ct);

        await using var scope = admin.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
        (await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Inventory_app_has_exactly_its_grants()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(db.AdminConnectionString);
        await connection.OpenAsync(ct);

        var schemaGrants = await ReadStrings(connection, ct, """
            SELECT p.state_desc COLLATE DATABASE_DEFAULT + N':' + s.name COLLATE DATABASE_DEFAULT + N':' + p.permission_name COLLATE DATABASE_DEFAULT
            FROM sys.database_permissions p
            JOIN sys.schemas s ON s.schema_id = p.major_id
            WHERE p.class = 3 AND p.grantee_principal_id = USER_ID(N'inventory_app')
            """);
        schemaGrants.Order().ToList().ShouldBe(
        [
            "GRANT:identity:DELETE",
            "GRANT:identity:INSERT",
            "GRANT:identity:SELECT",
            "GRANT:identity:UPDATE",
            "GRANT:inventory:INSERT",
            "GRANT:inventory:SELECT",
            "GRANT:inventory:UPDATE",
            "GRANT:platform:INSERT",
            "GRANT:platform:SELECT",
            "GRANT:platform:UPDATE",
        ]);

        var objectGrants = await ReadStrings(connection, ct, """
            SELECT p.state_desc COLLATE DATABASE_DEFAULT + N':' + SCHEMA_NAME(o.schema_id) COLLATE DATABASE_DEFAULT + N'.' + o.name COLLATE DATABASE_DEFAULT + N':' + p.permission_name COLLATE DATABASE_DEFAULT
            FROM sys.database_permissions p
            JOIN sys.objects o ON o.object_id = p.major_id
            WHERE p.class = 1 AND p.grantee_principal_id = USER_ID(N'inventory_app')
            """);
        objectGrants.Order().ToList().ShouldBe(
        [
            "DENY:inventory.StockDocuments:DELETE",
            "DENY:inventory.StockDocuments:UPDATE",
            "DENY:inventory.StockMovements:DELETE",
            "DENY:inventory.StockMovements:UPDATE",
            "GRANT:inventory.MovementIds:UPDATE",
        ]);

        var roles = await ReadStrings(connection, ct, """
            SELECT r.name
            FROM sys.database_role_members m
            JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
            WHERE m.member_principal_id = USER_ID(N'inventory_app')
            """);
        roles.ShouldBeEmpty();

        var databaseLevel = await ReadStrings(connection, ct, """
            SELECT permission_name
            FROM sys.database_permissions
            WHERE class = 0 AND grantee_principal_id = USER_ID(N'inventory_app')
              AND (permission_name LIKE N'ALTER%' OR permission_name = N'CONTROL')
            """);
        databaseLevel.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProvisionTenant_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenantId, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var scopes = app.Services.GetRequiredService<ITenantScopeFactory>();

        for (var i = 0; i < 2; i++)
        {
            await using var scope = scopes.BeginScope(tenantId);
            await scope.Services.GetRequiredService<IInventoryModule>().ProvisionTenantAsync(ct);
        }

        await using var read = scopes.BeginScope(tenantId);
        var locations = await read.Services.GetRequiredService<InventoryDbContext>().Locations
            .Where(l => l.TenantId == tenantId)
            .OrderBy(l => l.Code)
            .ToListAsync(ct);
        locations.Select(l => (l.Code, l.Name)).ShouldBe([("ADJ", "Adjustment"), ("CUS", "Customer"), ("SUP", "Supplier")]);
        locations.ShouldAllBe(l => l.IsActive && !l.AllowNegativeStock);
    }

    [Fact]
    public async Task BeginScope_acts_as_the_tenant_in_a_fresh_scope()
    {
        await using var app = db.CreateApp();
        var scopes = app.Services.GetRequiredService<ITenantScopeFactory>();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await using (var scope = scopes.BeginScope(first))
        {
            scope.Services.GetRequiredService<ITenantContext>().TenantId.ShouldBe(first);
        }

        await using (var scope = scopes.BeginScope(second))
        {
            scope.Services.GetRequiredService<ITenantContext>().TenantId.ShouldBe(second);
        }
    }

    [Fact]
    public async Task Seed_twice_changes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp(("Seed:Password", "Seed!Passw0rd-1"));
        var tenantIds = new[] { PlatformSeeder.DemoTenantId, PlatformSeeder.IsolationTenantId };

        await SeedCommand.RunAsync(app.Services, ct);
        var before = await Snapshot(app, tenantIds, ct);
        await SeedCommand.RunAsync(app.Services, ct);
        var after = await Snapshot(app, tenantIds, ct);

        after.ShouldBe(before);
        after.Tenants.ShouldBe(2);
        after.Users.ShouldBe(4);
        after.Locations.ShouldBe(8);
        after.Items.ShouldBe(20);
        after.Documents.ShouldBe(31);
        after.Movements.ShouldBe(31);
        after.BalanceRows.ShouldBe(50); // 31 branch balances and 19 Supplier balances

        await using var scope = app.Services.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        (await platform.Tenants.Where(t => tenantIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct))
            .Order().ToList().ShouldBe([PlatformSeeder.DemoTenantId, PlatformSeeder.IsolationTenantId]);

        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var expected = new (string Email, string Role)[]
        {
            ("admin@demo.test", Roles.Admin),
            ("staff@demo.test", Roles.Staff),
            ("admin@isolation.test", Roles.Admin),
            ("staff@isolation.test", Roles.Staff),
        };
        foreach (var (email, role) in expected)
        {
            var user = await users.FindByEmailAsync(email);
            user.ShouldNotBeNull();
            (await users.GetRolesAsync(user)).ShouldBe([role]);
        }

        var scopes = app.Services.GetRequiredService<ITenantScopeFactory>();
        foreach (var tenantId in tenantIds)
        {
            await using var tenantScope = scopes.BeginScope(tenantId);
            var inventory = tenantScope.Services.GetRequiredService<InventoryDbContext>();
            var locations = await inventory.Locations.Where(l => l.TenantId == tenantId).ToListAsync(ct);
            var items = await inventory.Items.Where(i => i.TenantId == tenantId).ToListAsync(ct);

            if (tenantId == PlatformSeeder.DemoTenantId)
            {
                locations.Count.ShouldBe(5);
                locations.Where(l => l.Kind == LocationKind.Physical).Select(l => l.Code).Order().ToList()
                    .ShouldBe(["MKT", "QC"]);
                items.Count.ShouldBe(20);
                items.ShouldAllBe(i => i.IsActive);
                items.Single(i => i.Sku == "RICE-WHT").BaseUnit.ShouldBe("kg");

                var documents = await inventory.StockDocuments.Where(d => d.TenantId == tenantId).ToListAsync(ct);
                documents.ShouldAllBe(d => d.Type == StockDocumentType.Receipt
                    && d.Reference == InventorySeeder.OpeningStockReference
                    && d.PostedBy == InventorySeeder.SystemPrincipal);

                // Sorted in C# only: IdempotencyKey is varchar under the server's collation, which orders differently.
                documents.Select(d => d.IdempotencyKey).Order(StringComparer.Ordinal).ToList().ShouldBe(
                    InventorySeeder.OpeningStock
                        .Select(r => InventorySeeder.OpeningStockKey(r.Sku, r.Code))
                        .Order(StringComparer.Ordinal)
                        .ToList());
            }
            else
            {
                locations.Count.ShouldBe(3);
                locations.Select(l => l.Kind).ShouldNotContain(LocationKind.Physical);
                items.ShouldBeEmpty();
                (await inventory.StockDocuments.Where(d => d.TenantId == tenantId).ToListAsync(ct)).ShouldBeEmpty();
            }
        }
    }

    private static async Task<(int Tenants, int Users, int Locations, int Items, int Documents, int Movements, int BalanceRows, string Balances)> Snapshot(ApiFactory app, Guid[] tenantIds, CancellationToken ct)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        // Locations sit under RLS, so each tenant's rows are counted from that tenant's scope.
        var locations = 0;
        var items = 0;
        var documents = 0;
        var movements = 0;
        var balanceRows = new List<string>();
        var scopes = app.Services.GetRequiredService<ITenantScopeFactory>();
        foreach (var tenantId in tenantIds)
        {
            await using var tenantScope = scopes.BeginScope(tenantId);
            var inventory = tenantScope.Services.GetRequiredService<InventoryDbContext>();
            locations += await inventory.Locations.CountAsync(l => l.TenantId == tenantId, ct);
            items += await inventory.Items.CountAsync(i => i.TenantId == tenantId, ct);
            documents += await inventory.StockDocuments.CountAsync(d => d.TenantId == tenantId, ct);
            movements += await inventory.StockMovements.CountAsync(m => m.TenantId == tenantId, ct);

            var balances = await inventory.StockBalances
                .Where(b => b.TenantId == tenantId)
                .OrderBy(b => b.ItemId)
                .ThenBy(b => b.LocationId)
                .ToListAsync(ct);
            balanceRows.AddRange(balances.Select(b => string.Create(
                CultureInfo.InvariantCulture,
                $"{b.ItemId}|{b.LocationId}|{b.OnHand:F4}|{b.StockValue:F4}|{b.LastUnitCost:F4}|{b.LastMovementId}")));
        }

        return (
            await platform.Tenants.CountAsync(t => tenantIds.Contains(t.Id), ct),
            await platform.Users.CountAsync(u => tenantIds.Contains(u.TenantId), ct),
            locations,
            items,
            documents,
            movements,
            balanceRows.Count,
            string.Join("\n", balanceRows));
    }

    private static async Task<List<string>> ReadStrings(SqlConnection connection, CancellationToken ct, string sql)
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