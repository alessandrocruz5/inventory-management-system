using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>T15 (spec §11): negative stock through the API: estimates, revaluations, reversals and the toggle.</summary>
public sealed class NegativeStockTests(DatabaseFixture db)
{
    private static async Task<JsonNode> PostOkAsync(HttpClient client, string route, object body, CancellationToken ct, string? key = null)
    {
        var response = await PostingTests.PostDocumentAsync(client, route, key ?? PostingTests.NewKey(), body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static Task<JsonNode> ReceiveAsync(HttpClient client, Guid location, Guid item, decimal quantity, decimal unitCost, CancellationToken ct, string? key = null) =>
        PostOkAsync(client, PostingTests.Receipts, PostingTests.Receipt(location, PostingTests.Line(item, quantity, unitCost)), ct, key);

    private static Task<JsonNode> IssueAsync(HttpClient client, Guid location, Guid item, decimal quantity, CancellationToken ct) =>
        PostOkAsync(client, PostingTests.Issues, PostingTests.Issue(location, PostingTests.IssueLine(item, quantity)), ct);

    private static void AssertMovement(JsonNode movement, int lineNo, Guid from, Guid to, decimal quantity, decimal totalCost)
    {
        movement["lineNo"]!.GetValue<int>().ShouldBe(lineNo);
        movement["fromLocationId"]!.GetValue<Guid>().ShouldBe(from);
        movement["toLocationId"]!.GetValue<Guid>().ShouldBe(to);
        movement["quantity"]!.GetValue<decimal>().ShouldBe(quantity);
        movement["totalCost"]!.GetValue<decimal>().ShouldBe(totalCost);
    }

    private static async Task AssertBalanceAsync(SqlConnection connection, Guid item, Guid location, decimal onHand, decimal stockValue, decimal? lastUnitCost, CancellationToken ct) =>
        (await ReversalTests.BalanceAsync(connection, item, location, ct)).ShouldBe((onHand, stockValue, lastUnitCost));

    private static async Task<int> ReversalCountAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM inventory.StockDocuments WHERE Type = 'Reversal'", connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    [Fact]
    public async Task Transfer_into_a_negative_balance_revalues_at_the_destination()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        await LocationsTests.AllowNegativeAsync(client, b, true, ct);
        var item = await PostingTests.NewItemAsync(client, "NEG-T", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var adj = await LedgerRows.LocationIdAsync(connection, "ADJ", ct);
        await ReceiveAsync(client, a, item, 10m, 60m, ct);
        await AssertBalanceAsync(connection, item, a, 10m, 600.0000m, 60.0000m, ct);
        await ReceiveAsync(client, b, item, 4m, 50m, ct);
        await IssueAsync(client, b, item, 6m, ct);
        await AssertBalanceAsync(connection, item, b, -2m, -100.0000m, 50.0000m, ct);

        var response = await PostingTests.PostDocumentAsync(client, TransferTests.Transfers, PostingTests.NewKey(),
            TransferTests.Transfer(a, b, TransferTests.TransferLine(item, 5m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var movements = document["movements"]!.AsArray();
        movements.Count.ShouldBe(2);
        AssertMovement(movements[0]!, 1, a, b, 5m, 300.0000m);
        AssertMovement(movements[1]!, 1001, b, adj, 0m, 20.0000m);
        var balances = document["balances"]!.AsArray();
        var balanceA = balances.Single(x => x!["locationId"]!.GetValue<Guid>() == a)!;
        balanceA["onHand"]!.GetValue<decimal>().ShouldBe(5m);
        balanceA["stockValue"]!.GetValue<decimal>().ShouldBe(300.0000m);
        var balanceB = balances.Single(x => x!["locationId"]!.GetValue<Guid>() == b)!;
        balanceB["onHand"]!.GetValue<decimal>().ShouldBe(3m);
        balanceB["stockValue"]!.GetValue<decimal>().ShouldBe(180.0000m);
        await AssertBalanceAsync(connection, item, b, 3m, 180.0000m, 60.0000m, ct);

        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Never_received_item_is_estimated_at_zero_and_shows_negative()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var k = await PostingTests.NewLocationAsync(client, ct, "K");
        await LocationsTests.AllowNegativeAsync(client, k, true, ct);
        var item = await PostingTests.NewItemAsync(client, "NEG-Z", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var adj = await LedgerRows.LocationIdAsync(connection, "ADJ", ct);

        var issue = await IssueAsync(client, k, item, 2m, ct);

        issue["movements"]!.AsArray().ShouldHaveSingleItem()!["totalCost"]!.GetValue<decimal>().ShouldBe(0.0000m);
        await AssertBalanceAsync(connection, item, k, -2m, 0.0000m, null, ct);
        var stock = await client.GetAsync($"/api/stock?locationId={k}", ct);
        stock.StatusCode.ShouldBe(HttpStatusCode.OK);
        var row = (await stock.Content.ReadFromJsonAsync<JsonNode>(ct))!["items"]!.AsArray()
            .Single(x => x!["itemId"]!.GetValue<Guid>() == item)!;
        row["onHand"]!.GetValue<decimal>().ShouldBe(-2m);
        row["stockValue"]!.GetValue<decimal>().ShouldBe(0m);
        row["averageCost"].ShouldBeNull();

        var receipt = await ReceiveAsync(client, k, item, 3m, 10m, ct);

        var movements = receipt["movements"]!.AsArray();
        movements.Count.ShouldBe(2);
        movements[0]!["totalCost"]!.GetValue<decimal>().ShouldBe(30.0000m);
        AssertMovement(movements[1]!, 1001, k, adj, 0m, 20.0000m);
        await AssertBalanceAsync(connection, item, k, 1m, 10.0000m, 10.0000m, ct);

        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Positive_adjustment_into_a_negative_balance_revalues()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var k = await PostingTests.NewLocationAsync(client, ct, "K");
        await LocationsTests.AllowNegativeAsync(client, k, true, ct);
        var item = await PostingTests.NewItemAsync(client, "NEG-A", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var adj = await LedgerRows.LocationIdAsync(connection, "ADJ", ct);
        await ReceiveAsync(client, k, item, 4m, 50m, ct);
        await IssueAsync(client, k, item, 6m, ct);
        await AssertBalanceAsync(connection, item, k, -2m, -100.0000m, 50.0000m, ct);

        // No stock to average, so a positive line needs a cost.
        var noCost = await PostingTests.PostDocumentAsync(client, AdjustmentTests.Adjustments, PostingTests.NewKey(),
            AdjustmentTests.Adjustment(k, "Found", AdjustmentTests.Delta(item, 1m)), ct);
        await PostingTests.AssertValidationAsync(noCost, "lines[0].unitCost", ct);

        var document = await PostOkAsync(client, AdjustmentTests.Adjustments,
            AdjustmentTests.Adjustment(k, "Found", AdjustmentTests.Delta(item, 5m, 40m)), ct);

        var movements = document["movements"]!.AsArray();
        movements.Count.ShouldBe(2);
        AssertMovement(movements[0]!, 1, adj, k, 5m, 200.0000m);
        AssertMovement(movements[1]!, 1001, adj, k, 0m, 20.0000m);
        await AssertBalanceAsync(connection, item, k, 3m, 120.0000m, 40.0000m, ct);

        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Issue_past_zero_at_a_location_with_the_toggle_off_is_still_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var k = await PostingTests.NewLocationAsync(client, ct, "K");
        var item = await PostingTests.NewItemAsync(client, "NEG-O", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await ReceiveAsync(client, k, item, 4m, 50m, ct);

        var response = await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(k, PostingTests.IssueLine(item, 6m)), ct);

        await PostingTests.AssertInsufficientStockAsync(response, item, k, 4m, 6m, ct);
        await AssertBalanceAsync(connection, item, k, 4m, 200.0000m, 50.0000m, ct);

        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Replay_of_a_revaluing_receipt_returns_the_same_movements()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var k = await PostingTests.NewLocationAsync(client, ct, "K");
        await LocationsTests.AllowNegativeAsync(client, k, true, ct);
        var item = await PostingTests.NewItemAsync(client, "NEG-R", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await ReceiveAsync(client, k, item, 4m, 50m, ct);
        await IssueAsync(client, k, item, 6m, ct);
        await IssueAsync(client, k, item, 1m, ct);
        var key = PostingTests.NewKey();
        var body = PostingTests.Receipt(k, PostingTests.Line(item, 10m, 56m));

        var first = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key, body, ct);
        var second = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key, body, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        var original = (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var replay = (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        replay["id"]!.GetValue<Guid>().ShouldBe(original["id"]!.GetValue<Guid>());
        var replayed = replay["movements"]!.AsArray();
        replayed.Select(m => m!["id"]!.GetValue<long>())
            .ShouldBe(original["movements"]!.AsArray().Select(m => m!["id"]!.GetValue<long>()));
        replayed.Select(m => m!["lineNo"]!.GetValue<int>()).ShouldBe([1, 1001]);
        replayed.Select(m => m!["totalCost"]!.GetValue<decimal>()).ShouldBe([560m, 18m]);
        (await PostingTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE IdempotencyKey = @key",
            new SqlParameter("@key", key), ct)).ShouldBe(1);

        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Reversal_restores_a_negative_balance_exactly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var k = await PostingTests.NewLocationAsync(client, ct, "K");
        await LocationsTests.AllowNegativeAsync(client, k, true, ct);
        var item = await PostingTests.NewItemAsync(client, "NEG-V", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var sup = await LedgerRows.LocationIdAsync(connection, "SUP", ct);
        var adj = await LedgerRows.LocationIdAsync(connection, "ADJ", ct);
        await ReceiveAsync(client, k, item, 4m, 50m, ct);
        await IssueAsync(client, k, item, 6m, ct);
        await AssertBalanceAsync(connection, item, k, -2m, -100.0000m, 50.0000m, ct);
        var receipt = await ReceiveAsync(client, k, item, 10m, 56m, ct);
        var receiptMovements = receipt["movements"]!.AsArray();
        AssertMovement(receiptMovements[1]!, 1001, k, adj, 0m, 12.0000m);
        await AssertBalanceAsync(connection, item, k, 8m, 448.0000m, 56.0000m, ct);

        var response = await ReversalTests.ReverseAsync(client, receipt["id"]!.GetValue<Guid>(), PostingTests.NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var movements = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["movements"]!.AsArray();
        movements.Count.ShouldBe(2);
        AssertMovement(movements[0]!, 1, k, sup, 10m, 560.0000m);
        movements[0]!["reversesMovementId"]!.GetValue<long>().ShouldBe(receiptMovements[0]!["id"]!.GetValue<long>());
        AssertMovement(movements[1]!, 1001, adj, k, 0m, 12.0000m);
        movements[1]!["reversesMovementId"]!.GetValue<long>().ShouldBe(receiptMovements[1]!["id"]!.GetValue<long>());
        await AssertBalanceAsync(connection, item, k, -2m, -100.0000m, 50.0000m, ct);
        (await ReversalTests.BalanceAsync(connection, item, adj, ct))!.Value.OnHand.ShouldBe(0m);

        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Reversal_after_the_toggle_is_turned_off_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var k = await PostingTests.NewLocationAsync(client, ct, "K");
        await LocationsTests.AllowNegativeAsync(client, k, true, ct);
        var item = await PostingTests.NewItemAsync(client, "NEG-D", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await ReceiveAsync(client, k, item, 4m, 50m, ct);
        await IssueAsync(client, k, item, 6m, ct);
        var receipt = await ReceiveAsync(client, k, item, 10m, 56m, ct);
        await AssertBalanceAsync(connection, item, k, 8m, 448.0000m, 56.0000m, ct);

        // 8 is not below zero, so the toggle can go off.
        await LocationsTests.AllowNegativeAsync(client, k, false, ct);
        var response = await ReversalTests.ReverseAsync(client, receipt["id"]!.GetValue<Guid>(), PostingTests.NewKey(), ct);

        await ReversalTests.AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "insufficient-stock", ct);
        var problem = await ReversalTests.ProblemAsync(response, ct);
        problem["itemId"]!.GetValue<Guid>().ShouldBe(item);
        problem["locationId"]!.GetValue<Guid>().ShouldBe(k);
        problem["available"]!.GetValue<decimal>().ShouldBe(8m);
        problem["requested"]!.GetValue<decimal>().ShouldBe(10m);
        (await ReversalCountAsync(connection, ct)).ShouldBe(0);
        await AssertBalanceAsync(connection, item, k, 8m, 448.0000m, 56.0000m, ct);

        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }
}