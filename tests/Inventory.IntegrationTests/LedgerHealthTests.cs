using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// S11's <c>GET /api/ledger/health</c>: Admin only, all five invariants in order, healthy on a real ledger, and each
/// invariant's failure detected with its key and its expected and actual values.
/// </summary>
public sealed class LedgerHealthTests(DatabaseFixture db)
{
    private const string Health = "/api/ledger/health";

    private static readonly string[] InvariantIds = ["INV-1", "INV-2", "INV-3", "INV-4", "INV-5"];

    [Fact]
    public async Task New_tenant_is_healthy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var health = await HealthAsync(client, ct);

        health["healthy"]!.GetValue<bool>().ShouldBeTrue();
        var invariants = health["invariants"]!.AsArray();
        invariants.Select(i => i!["id"]!.GetValue<string>()).ShouldBe(InvariantIds);
        invariants.Select(i => i!["name"]!.GetValue<string>())
            .ShouldBe(["Projection", "Conservation", "Physical sanity", "Reversal mirror", "Last unit cost"]);
        foreach (var invariant in invariants)
        {
            invariant!["passed"]!.GetValue<bool>().ShouldBeTrue();
            invariant["failureCount"]!.GetValue<int>().ShouldBe(0);
            invariant["failures"]!.AsArray().ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Mixed_ledger_is_healthy()
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

        await PostAsync(PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(p, 10m, 100.00m)));
        await PostAsync(PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(p, 5m, 120.00m)));
        await PostAsync(PostingTests.Issues, PostingTests.Issue(a, PostingTests.IssueLine(p, 3m)));
        await PostAsync(TransferTests.Transfers, TransferTests.Transfer(a, b, TransferTests.TransferLine(p, 4m)));
        await PostAsync(AdjustmentTests.Adjustments, AdjustmentTests.Adjustment(a, "Found", AdjustmentTests.Delta(p, 2m)));
        await PostAsync(AdjustmentTests.Adjustments, AdjustmentTests.Adjustment(a, "Damaged", AdjustmentTests.Delta(p, -1m)));
        var receipt = await PostAsync(PostingTests.Receipts, PostingTests.Receipt(b, PostingTests.Line(p, 6m, 125.00m)));
        (await ReversalTests.ReverseAsync(client, receipt, PostingTests.NewKey(), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // K allows negative stock: the issue takes it below zero and the receipt settles it.
        await PostAsync(PostingTests.Receipts, PostingTests.Receipt(k, PostingTests.Line(q, 2m, 50.00m)));
        await PostAsync(PostingTests.Issues, PostingTests.Issue(k, PostingTests.IssueLine(q, 3m)));
        await PostAsync(PostingTests.Receipts, PostingTests.Receipt(k, PostingTests.Line(q, 5m, 50.00m)));

        var health = await HealthAsync(client, ct);

        health["healthy"]!.GetValue<bool>().ShouldBeTrue(health.ToJsonString());
        AssertPassed(health, InvariantIds);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await LedgerInvariants.AssertHoldAsync(connection, ct);

        async Task<Guid> PostAsync(string route, object body)
        {
            var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return await ReversalTests.IdAsync(response, ct);
        }
    }

    [Fact]
    public async Task Drifted_projection_fails_INV_1()
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

        // Both stay within the CHECKs; LastUnitCost is untouched.
        await UpdateBalanceAsync(connection, "OnHand = 6, StockValue = 61", tenant, item, a, ct);
        await UpdateBalanceAsync(connection, "LastMovementId = NULL", tenant, item, a, ct);

        var health = await HealthAsync(client, ct);

        health["healthy"]!.GetValue<bool>().ShouldBeFalse();
        var failures = Failures(health, "INV-1");
        failures.Select(f => f["check"]!.GetValue<string>()).ShouldBe(["LastMovementId", "OnHand", "StockValue"]);
        failures.ShouldAllBe(f => f["itemId"]!.GetValue<Guid>() == item && f["sku"]!.GetValue<string>() == "D-1"
            && f["locationId"]!.GetValue<Guid>() == a && f["locationCode"]!.GetValue<string>() == "A");
        Values(failures[0]).ShouldBe(((decimal?)lastMovementId, (decimal?)null));
        Values(failures[1]).ShouldBe(((decimal?)5m, (decimal?)6m));
        Values(failures[2]).ShouldBe(((decimal?)50m, (decimal?)61m));
        Invariant(health, "INV-1")["failureCount"]!.GetValue<int>().ShouldBe(3);
        AssertPassed(health, "INV-3", "INV-4", "INV-5");
    }

    [Fact]
    public async Task Virtual_drift_fails_INV_1_and_INV_2()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "V-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var supplier = await LedgerRows.LocationIdAsync(connection, "SUP", ct);

        await UpdateBalanceAsync(connection, "OnHand = OnHand + 1", tenant, item, supplier, ct);

        var health = await HealthAsync(client, ct);

        health["healthy"]!.GetValue<bool>().ShouldBeFalse();
        var projection = Failures(health, "INV-1").ShouldHaveSingleItem();
        projection["check"]!.GetValue<string>().ShouldBe("OnHand");
        projection["locationId"]!.GetValue<Guid>().ShouldBe(supplier);
        projection["locationCode"]!.GetValue<string>().ShouldBe("SUP");
        Values(projection).ShouldBe(((decimal?)-5m, (decimal?)-4m));
        var conservation = Failures(health, "INV-2").ShouldHaveSingleItem();
        conservation["check"]!.GetValue<string>().ShouldBe("OnHandSum");
        conservation["itemId"]!.GetValue<Guid>().ShouldBe(item);
        conservation["sku"]!.GetValue<string>().ShouldBe("V-1");
        conservation["locationId"].ShouldBeNull();
        Values(conservation).ShouldBe(((decimal?)0m, (decimal?)1m));
        AssertPassed(health, "INV-3", "INV-4", "INV-5");
    }

    [Fact]
    public async Task Mirror_drift_fails_INV_3()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "N-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        // The mirror says allowed, so CK_StockBalances_Physical accepts the row; the location still says no.
        await UpdateBalanceAsync(connection, "AllowNegative = 1, OnHand = -1, StockValue = -10", tenant, item, a, ct);

        var health = await HealthAsync(client, ct);

        health["healthy"]!.GetValue<bool>().ShouldBeFalse();
        var failure = Failures(health, "INV-3").ShouldHaveSingleItem();
        failure["check"]!.GetValue<string>().ShouldBe("NegativeNotAllowed");
        failure["itemId"]!.GetValue<Guid>().ShouldBe(item);
        failure["locationId"]!.GetValue<Guid>().ShouldBe(a);
        failure["locationCode"]!.GetValue<string>().ShouldBe("A");
        Values(failure).ShouldBe(((decimal?)null, (decimal?)-1m));
    }

    [Fact]
    public async Task Bad_reversal_fails_INV_4()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "R-1", ct);
        var posted = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, PostingTests.NewKey(),
            PostingTests.Receipt(a, PostingTests.Line(item, 5m, 10m)), ct);
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var receipt = await ReversalTests.IdAsync(posted, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var original = (await LedgerInvariants.MovementsAsync(connection, ct)).Where(m => m.DocumentId == receipt).ShouldHaveSingleItem();
        var supplier = await LedgerRows.LocationIdAsync(connection, "SUP", ct);

        // One reversal document with two lines: a second reversal document would hit UX_StockDocuments_Reverses.
        var reversal = await LedgerRows.AddDocumentAsync(connection, ct, type: "Reversal", reverses: receipt);
        var wrongCost = await LedgerRows.AddMovementAsync(connection, reversal, 1, item, from: a, to: supplier,
            original.Quantity, original.TotalCost + 1m, ct, reverses: original.Id);
        await LedgerRows.AddMovementAsync(connection, reversal, 2, item, from: a, to: supplier,
            original.Quantity, original.TotalCost, ct, reverses: original.Id);

        var health = await HealthAsync(client, ct);

        // The direct inserts leave the balances stale, so INV-1 fails too; only INV-4 is asserted here.
        health["healthy"]!.GetValue<bool>().ShouldBeFalse();
        var failures = Failures(health, "INV-4");
        failures.Select(f => (f["movementId"]!.GetValue<long>(), f["check"]!.GetValue<string>()))
            .ShouldBe([(original.Id, "ReversedTwice"), (wrongCost, "TotalCost")]);
        failures.ShouldAllBe(f => f["itemId"]!.GetValue<Guid>() == item && f["sku"]!.GetValue<string>() == "R-1"
            && f["locationId"] == null && f["locationCode"] == null);
        Values(failures[0]).ShouldBe(((decimal?)null, (decimal?)2m));
        Values(failures[1]).ShouldBe(((decimal?)original.TotalCost, (decimal?)(original.TotalCost + 1m)));
    }

    [Fact]
    public async Task Stale_last_unit_cost_fails_INV_5()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "L-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        await UpdateBalanceAsync(connection, "LastUnitCost = 1.2345", tenant, item, a, ct);

        var health = await HealthAsync(client, ct);

        health["healthy"]!.GetValue<bool>().ShouldBeFalse();
        var failure = Failures(health, "INV-5").ShouldHaveSingleItem();
        failure["check"]!.GetValue<string>().ShouldBe("LastUnitCost");
        failure["itemId"]!.GetValue<Guid>().ShouldBe(item);
        failure["locationCode"]!.GetValue<string>().ShouldBe("A");
        Values(failure).ShouldBe(((decimal?)10m, (decimal?)1.2345m));
        AssertPassed(health, "INV-1", "INV-2", "INV-3", "INV-4");
    }

    [Fact]
    public async Task Staff_gets_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var response = await staff.GetAsync(Health, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<JsonNode> HealthAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync(Health, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static JsonNode Invariant(JsonNode health, string id) =>
        health["invariants"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == id)!;

    private static List<JsonNode> Failures(JsonNode health, string id) =>
        [.. Invariant(health, id)["failures"]!.AsArray().Select(f => f!)];

    private static (decimal? Expected, decimal? Actual) Values(JsonNode failure) =>
        (failure["expected"]?.GetValue<decimal>(), failure["actual"]?.GetValue<decimal>());

    private static void AssertPassed(JsonNode health, params string[] ids)
    {
        foreach (var id in ids)
        {
            var invariant = Invariant(health, id);
            invariant["passed"]!.GetValue<bool>().ShouldBeTrue($"{id}: {invariant.ToJsonString()}");
            invariant["failureCount"]!.GetValue<int>().ShouldBe(0);
        }
    }

    /// <summary>Sets <paramref name="assignments"/> on one balance row through the tenant's raw connection.</summary>
    private static async Task UpdateBalanceAsync(
        SqlConnection connection, string assignments, Guid tenant, Guid item, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            $"UPDATE inventory.StockBalances SET {assignments} WHERE TenantId = @t AND ItemId = @i AND LocationId = @l", connection);
        command.Parameters.Add(new SqlParameter("@t", SqlDbType.UniqueIdentifier) { Value = tenant });
        command.Parameters.Add(new SqlParameter("@i", SqlDbType.UniqueIdentifier) { Value = item });
        command.Parameters.Add(new SqlParameter("@l", SqlDbType.UniqueIdentifier) { Value = location });
        (await command.ExecuteNonQueryAsync(ct)).ShouldBe(1);
    }
}