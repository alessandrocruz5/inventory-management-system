using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Inventory.Domain;
using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// S11's <see cref="LedgerProjection"/>: the stored balances next to the values derived from the ledger. A healthy
/// ledger projects to its stored rows, and its <c>LastUnitCost</c> to <see cref="CostingPolicy.LastUnitCost"/>; a
/// drifted or missing row shows as a difference.
/// </summary>
public sealed class LedgerProjectionTests(DatabaseFixture db)
{
    [Fact]
    public async Task Projection_matches_stored_balances_and_CostingPolicy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var k = await PostingTests.NewLocationAsync(client, ct, "K");
        await LocationsTests.AllowNegativeAsync(client, k, true, ct);
        var p = await PostingTests.NewItemAsync(client, "P", ct);
        var q = await PostingTests.NewItemAsync(client, "Q", ct);
        var r = await PostingTests.NewItemAsync(client, "R", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        // Item P, set V steps V1–V7 at A and B. V7 reverses V6, so B's LastUnitCost falls back to the V5 transfer-in.
        await ReceiveAsync(a, p, 10m, 100.00m);
        await ReceiveAsync(a, p, 10m, 120.00m);
        await IssueAsync(a, p, 15m);
        await ReceiveAsync(a, p, 5m, 130.00m);
        await PostAsync(TransferTests.Transfers, TransferTests.Transfer(a, b, TransferTests.TransferLine(p, 4m)));
        var v6 = await ReceiveAsync(b, p, 6m, 125.00m);
        (await ReversalTests.ReverseAsync(client, v6, PostingTests.NewKey(), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Item Q, set N steps N1–N9 at K: N4 and N7 settle their revaluations out to ADJ.
        await ReceiveAsync(k, q, 4m, 50.00m);
        await IssueAsync(k, q, 6m);
        await IssueAsync(k, q, 1m);
        await ReceiveAsync(k, q, 10m, 56.00m);
        await IssueAsync(k, q, 7m);
        await IssueAsync(k, q, 2m);
        await ReceiveAsync(k, q, 2m, 60.00m);
        await ReceiveAsync(k, q, 5m, 60.00m);
        await IssueAsync(k, q, 5m);

        // Item R, set X steps X1–X3 at K: X3's revaluation comes in from ADJ.
        await ReceiveAsync(k, r, 4m, 50.00m);
        await IssueAsync(k, r, 6m);
        await ReceiveAsync(k, r, 3m, 10.00m);

        // Item R at A: an adjustment +3 without a unit cost, costed at the average, is the latest inflow.
        await ReceiveAsync(a, r, 2m, 10.00m);
        await PostAsync(AdjustmentTests.Adjustments, AdjustmentTests.Adjustment(a, "Found", AdjustmentTests.Delta(r, 3m)));

        // Item R at B: 40.0002 / 4 = 10.00005 rounds away from zero to 10.0001; banker's rounding would give 10.0000.
        await ReceiveAsync(b, r, 2m, 10.0001m);
        await ReceiveAsync(b, r, 2m, 10.0000m);

        var projection = await AssertProjectionMatchesAsync(app, tenant, connection, ct);

        Single(projection, p, b).Ledger.ShouldBe(new BalanceValues(4m, 480.0000m, 120.0000m, Single(projection, p, b).Stored!.LastMovementId));
        Single(projection, q, k).Ledger.LastUnitCost.ShouldBe(60.0000m);
        Single(projection, r, k).Ledger.LastUnitCost.ShouldBe(10.0000m);
        Single(projection, r, b).Ledger.LastUnitCost.ShouldBe(10.0001m);

        // IMS-14: an (item, ADJ) row a receipt locked but never moved projects to 0 / 0 / null / null.
        var empty = new BalanceValues(0m, 0m, null, null);
        var idleAdjustments = projection.Where(x => x.LocationCode == "ADJ" && x.Ledger.LastMovementId is null).ToList();
        idleAdjustments.ShouldContain(x => x.ItemId == p);
        idleAdjustments.ShouldAllBe(x => x.Stored == empty && x.Ledger == empty);

        Task IssueAsync(Guid location, Guid item, decimal quantity) =>
            PostAsync(PostingTests.Issues, PostingTests.Issue(location, PostingTests.IssueLine(item, quantity)));

        Task<Guid> ReceiveAsync(Guid location, Guid item, decimal quantity, decimal unitCost) =>
            PostAsync(PostingTests.Receipts, PostingTests.Receipt(location, PostingTests.Line(item, quantity, unitCost)));

        async Task<Guid> PostAsync(string route, object body)
        {
            var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        }
    }

    [Fact]
    public async Task Projection_reports_a_drifted_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "D-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var lastMovementId = (await LedgerInvariants.MovementsAsync(connection, ct)).Max(m => m.Id);

        await using (var command = new SqlCommand("""
            UPDATE inventory.StockBalances SET OnHand = 6, StockValue = 61, LastUnitCost = 1.2345
            WHERE TenantId = @t AND ItemId = @i AND LocationId = @a
            """, connection))
        {
            command.Parameters.Add(new SqlParameter("@t", SqlDbType.UniqueIdentifier) { Value = tenant });
            command.Parameters.Add(new SqlParameter("@i", SqlDbType.UniqueIdentifier) { Value = item });
            command.Parameters.Add(new SqlParameter("@a", SqlDbType.UniqueIdentifier) { Value = a });
            (await command.ExecuteNonQueryAsync(ct)).ShouldBe(1);
        }

        var row = Single(await ReadProjectionAsync(app, tenant, ct), item, a);

        row.Differs.ShouldBeTrue();
        row.Stored.ShouldBe(new BalanceValues(6m, 61m, 1.2345m, lastMovementId));
        row.Ledger.ShouldBe(new BalanceValues(5m, 50m, 10.0000m, lastMovementId));
    }

    [Fact]
    public async Task Projection_reports_a_missing_balance_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "M-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        Guid supplier;
        await using (var connection = await LedgerRows.OpenAsync(db, tenant, ct))
        {
            supplier = await LedgerRows.LocationIdAsync(connection, "SUP", ct);
        }

        // As the owner: inventory_app can't delete. RLS applies to the owner too, so it sets the same session context.
        await using (var owner = new SqlConnection(new SqlConnectionStringBuilder(db.AdminConnectionString) { Pooling = false }.ConnectionString))
        {
            await owner.OpenAsync(ct);
            await using (var context = new SqlCommand("EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1;", owner))
            {
                context.Parameters.Add(new SqlParameter("@t", SqlDbType.UniqueIdentifier) { Value = tenant });
                await context.ExecuteNonQueryAsync(ct);
            }

            await using var delete = new SqlCommand(
                "DELETE FROM inventory.StockBalances WHERE TenantId = @t AND ItemId = @i AND LocationId = @l", owner);
            delete.Parameters.Add(new SqlParameter("@t", SqlDbType.UniqueIdentifier) { Value = tenant });
            delete.Parameters.Add(new SqlParameter("@i", SqlDbType.UniqueIdentifier) { Value = item });
            delete.Parameters.Add(new SqlParameter("@l", SqlDbType.UniqueIdentifier) { Value = supplier });
            (await delete.ExecuteNonQueryAsync(ct)).ShouldBe(1);
        }

        var row = Single(await ReadProjectionAsync(app, tenant, ct), item, supplier);

        row.Differs.ShouldBeTrue();
        row.Stored.ShouldBeNull();
        row.IsVirtual.ShouldBeTrue();
        row.Ledger.OnHand.ShouldBe(-5m);
    }

    /// <summary>
    /// Asserts the projection matches the stored balances row for row, its physical <c>LastUnitCost</c> matches
    /// <see cref="CostingPolicy.LastUnitCost"/>, and it has one row per balance. Returns the projection.
    /// </summary>
    private static async Task<List<ProjectedBalance>> AssertProjectionMatchesAsync(
        ApiFactory app, Guid tenant, SqlConnection connection, CancellationToken ct)
    {
        var projection = await ReadProjectionAsync(app, tenant, ct);
        var movements = await LedgerInvariants.MovementsAsync(connection, ct);

        foreach (var row in projection)
        {
            row.Stored.ShouldBe(row.Ledger, $"{row.Sku} at {row.LocationCode}");
            if (row.IsVirtual)
            {
                continue;
            }

            // Only the item's movements that touch the location: CostingPolicy.LastUnitCost throws on any other.
            var touching = movements
                .Where(m => m.ItemId == row.ItemId && (m.FromLocationId == row.LocationId || m.ToLocationId == row.LocationId))
                .ToList();
            row.Ledger.LastUnitCost.ShouldBe(
                CostingPolicy.LastUnitCost(new BalanceKey(row.ItemId, row.LocationId), touching), $"{row.Sku} at {row.LocationCode}");
        }

        await using var count = new SqlCommand("SELECT COUNT(*) FROM inventory.StockBalances", connection);
        projection.Count.ShouldBe((int)(await count.ExecuteScalarAsync(ct))!);
        return projection;
    }

    private static async Task<List<ProjectedBalance>> ReadProjectionAsync(ApiFactory app, Guid tenant, CancellationToken ct)
    {
        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        return await LedgerProjection.ReadAsync(scope.Services.GetRequiredService<InventoryDbContext>(), tenant, ct);
    }

    private static ProjectedBalance Single(List<ProjectedBalance> projection, Guid item, Guid location) =>
        projection.Where(x => x.ItemId == item && x.LocationId == location).ShouldHaveSingleItem();
}