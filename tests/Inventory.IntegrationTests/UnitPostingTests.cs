using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// T16 (IMS-29): posting in an item's units through the API. Vector set U (spec §7), the unit and conversion 400s, the
/// 400-before-422 order, replays with a unit, and requests without <c>unit</c> posting exactly as before.
/// </summary>
public sealed class UnitPostingTests(DatabaseFixture db)
{
    [Fact]
    public async Task Vector_set_U_through_the_api()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var u = (await LocationsTests.CreateAsync(client, "U", "Location U", ct))["id"]!.GetValue<Guid>();
        var item = await PostingTests.NewItemAsync(client, "U-1", ct);
        await AddUnitAsync(client, item, "case", 12m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        (string Step, string Route, object Body, decimal Quantity, decimal Cost, decimal OnHand, decimal StockValue, decimal LastUnitCost)[] steps =
        [
            // 12 × Round4(500 / 12) would give 500.0004: the inflow is costed in the entered unit.
            ("U1", PostingTests.Receipts, PostingTests.Receipt(u, UnitLine(item, 1m, 500.00m, "case")), 12m, 500.0000m, 12m, 500.0000m, 41.6667m),
            ("U2", PostingTests.Receipts, PostingTests.Receipt(u, UnitLine(item, 2.5m, 480.00m, "case")), 30m, 1200.0000m, 42m, 1700.0000m, 40.4762m),
            ("U3", PostingTests.Issues, PostingTests.Issue(u, UnitIssueLine(item, 0.5m, "case")), 6m, 242.8571m, 36m, 1457.1429m, 40.4762m),
            ("U4", PostingTests.Issues, PostingTests.Issue(u, PostingTests.IssueLine(item, 36m)), 36m, 1457.1429m, 0m, 0.0000m, 40.4762m),
        ];

        var costs = new Dictionary<string, decimal>();
        foreach (var (step, route, body, quantity, cost, onHand, stockValue, lastUnitCost) in steps)
        {
            var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Created, step);
            var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            var movement = document["movements"]!.AsArray().ShouldHaveSingleItem()!;
            movement["quantity"]!.GetValue<decimal>().ShouldBe(quantity, step);
            movement["totalCost"]!.GetValue<decimal>().ShouldBe(cost, step);
            var balance = document["balances"]!.AsArray().ShouldHaveSingleItem()!;
            balance["locationId"]!.GetValue<Guid>().ShouldBe(u, step);
            balance["onHand"]!.GetValue<decimal>().ShouldBe(onHand, step);
            balance["stockValue"]!.GetValue<decimal>().ShouldBe(stockValue, step);
            balance["lastUnitCost"]!.GetValue<decimal>().ShouldBe(lastUnitCost, step);
            (await PostingTests.BalanceAsync(connection, item, u, ct)).ShouldBe((onHand, stockValue), step);
            costs[step] = movement["totalCost"]!.GetValue<decimal>();
        }

        // Spec §7: received ₱1,700.0000 = issued ₱242.8571 + ₱1,457.1429.
        (costs["U3"] + costs["U4"]).ShouldBe(costs["U1"] + costs["U2"]);
    }

    [Fact]
    public async Task Base_unit_name_is_factor_1()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "PC-1", ct);

        var response = await PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, PostingTests.NewKey(), PostingTests.Receipt(location, UnitLine(item, 7m, 10.00m, " PC ")), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var movement = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["movements"]!.AsArray().ShouldHaveSingleItem()!;
        movement["quantity"]!.GetValue<decimal>().ShouldBe(7m);
        movement["totalCost"]!.GetValue<decimal>().ShouldBe(70.0000m);
    }

    [Fact]
    public async Task Unknown_or_inactive_unit_is_400_on_unit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "BOX-1", ct);
        await AddUnitAsync(client, item, "case", 12m, ct);

        var unknown = await PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, PostingTests.NewKey(), PostingTests.Receipt(location, UnitLine(item, 1m, 10m, "box")), ct);
        await PostingTests.AssertValidationAsync(unknown, "lines[0].unit", ct);

        (await client.PostAsync($"/api/items/{item}/units/case/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var inactive = await PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, PostingTests.NewKey(), PostingTests.Receipt(location, UnitLine(item, 1m, 10m, "case")), ct);
        await PostingTests.AssertValidationAsync(inactive, "lines[0].unit", ct);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await DocumentCountAsync(connection, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Non_exact_conversion_is_400_on_quantity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = (await ItemsTests.CreateAsync(client, "FLOUR-1", "FLOUR-1", "kg", ct))["id"]!.GetValue<Guid>();
        await AddUnitAsync(client, item, "g", 0.001m, ct);
        await AddUnitAsync(client, item, "ton", 1000m, ct);

        // 0.5 g = 0.0005 kg: exact, and costed per gram.
        var exact = await PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, PostingTests.NewKey(), PostingTests.Receipt(location, UnitLine(item, 0.5m, 10m, "g")), ct);
        exact.StatusCode.ShouldBe(HttpStatusCode.Created);
        var movement = (await exact.Content.ReadFromJsonAsync<JsonNode>(ct))!["movements"]!.AsArray().ShouldHaveSingleItem()!;
        movement["quantity"]!.GetValue<decimal>().ShouldBe(0.0005m);
        movement["totalCost"]!.GetValue<decimal>().ShouldBe(5.0000m);

        // 0.25 g = 0.00025 kg: never rounded (D4).
        var receipt = await PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, PostingTests.NewKey(), PostingTests.Receipt(location, UnitLine(item, 0.25m, 10m, "g")), ct);
        await PostingTests.AssertValidationAsync(receipt, "lines[0].quantity", ct);

        var adjustment = await PostingTests.PostDocumentAsync(
            client, AdjustmentTests.Adjustments, PostingTests.NewKey(),
            AdjustmentTests.Adjustment(location, "Damaged", new { itemId = item, quantityDelta = -0.25m, unit = "g" }), ct);
        await PostingTests.AssertValidationAsync(adjustment, "lines[0].quantityDelta", ct);

        // 1,000,000 ton = 1,000,000,000 kg, past MaxAmount once converted.
        var tooLarge = await PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, PostingTests.NewKey(), PostingTests.Receipt(location, UnitLine(item, 1_000_000m, 0m, "ton")), ct);
        await PostingTests.AssertValidationAsync(tooLarge, "lines[0].quantity", ct);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await DocumentCountAsync(connection, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Unit_errors_come_before_422s()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "ORDER-1", ct);
        var inactive = await PostingTests.NewItemAsync(client, "ORDER-2", ct);
        (await client.PostAsync($"/api/items/{inactive}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, PostingTests.NewKey(),
            PostingTests.Receipt(location, UnitLine(item, 1m, 10m, "box"), PostingTests.Line(inactive, 1m, 10m)), ct);

        await PostingTests.AssertValidationAsync(response, "lines[0].unit", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await DocumentCountAsync(connection, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Replay_with_unit_is_200_and_a_changed_unit_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "REPLAY-1", ct);
        await AddUnitAsync(client, item, "case", 12m, ct);
        var key = PostingTests.NewKey();
        var body = PostingTests.Receipt(location, UnitLine(item, 1m, 500m, "case"));

        var first = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key, body, ct);
        var replay = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key, body, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var originalId = (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await replay.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>().ShouldBe(originalId);

        // The same 12 pc, entered in the base unit: a different body under the same key.
        var changed = await PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, key, PostingTests.Receipt(location, UnitLine(item, 12m, 500m, "pc")), ct);

        changed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await changed.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/idempotency-key-reused");
        problem["originalDocumentId"]!.GetValue<Guid>().ShouldBe(originalId);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await DocumentCountAsync(connection, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Transfer_and_adjustment_accept_units()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "MOVE-1", ct);
        await AddUnitAsync(client, item, "case", 12m, ct);
        await PostingTests.ReceiveAsync(client, a, item, 24m, 10.00m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        var transfer = await PostingTests.PostDocumentAsync(
            client, TransferTests.Transfers, PostingTests.NewKey(),
            TransferTests.Transfer(a, b, new { itemId = item, quantity = 1m, unit = "case" }), ct);

        transfer.StatusCode.ShouldBe(HttpStatusCode.Created);
        var moved = (await transfer.Content.ReadFromJsonAsync<JsonNode>(ct))!["movements"]!.AsArray().ShouldHaveSingleItem()!;
        moved["quantity"]!.GetValue<decimal>().ShouldBe(12m);
        moved["totalCost"]!.GetValue<decimal>().ShouldBe(120.0000m);
        (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBe((12m, 120.0000m));
        (await PostingTests.BalanceAsync(connection, item, b, ct)).ShouldBe((12m, 120.0000m));

        var adjustment = await PostingTests.PostDocumentAsync(
            client, AdjustmentTests.Adjustments, PostingTests.NewKey(),
            AdjustmentTests.Adjustment(a, "Found", new { itemId = item, quantityDelta = 1m, unitCost = 600.00m, unit = "case" }), ct);

        adjustment.StatusCode.ShouldBe(HttpStatusCode.Created);
        var found = (await adjustment.Content.ReadFromJsonAsync<JsonNode>(ct))!["movements"]!.AsArray().ShouldHaveSingleItem()!;
        found["quantity"]!.GetValue<decimal>().ShouldBe(12m);
        found["totalCost"]!.GetValue<decimal>().ShouldBe(600.0000m);
        (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBe((24m, 720.0000m));
    }

    [Fact]
    public async Task Old_requests_without_unit_post_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "V-1", ct);
        await AddUnitAsync(client, item, "case", 12m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        // Bodies with no "unit" property at all, as every pre-Sprint-3 client sends them.
        (string Step, string Route, object Body, decimal Cost, decimal OnHand, decimal StockValue)[] steps =
        [
            ("V1", PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 10m, 100.00m)), 1000.0000m, 10m, 1000.0000m),
            ("V2", PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 10m, 120.00m)), 1200.0000m, 20m, 2200.0000m),
            ("V3", PostingTests.Issues, PostingTests.Issue(a, PostingTests.IssueLine(item, 15m)), 1650.0000m, 5m, 550.0000m),
        ];

        foreach (var (step, route, body, cost, onHand, stockValue) in steps)
        {
            var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Created, step);
            var movement = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["movements"]!.AsArray().ShouldHaveSingleItem()!;
            movement["totalCost"]!.GetValue<decimal>().ShouldBe(cost, step);
            (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBe((onHand, stockValue), step);
        }
    }

    private static object UnitLine(Guid itemId, decimal quantity, decimal unitCost, string unit) => new { itemId, quantity, unitCost, unit };

    private static object UnitIssueLine(Guid itemId, decimal quantity, string unit) => new { itemId, quantity, unit };

    private static async Task AddUnitAsync(HttpClient client, Guid item, string code, decimal factor, CancellationToken ct) =>
        (await client.PostAsJsonAsync($"/api/items/{item}/units", new { code, factor }, ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

    /// <summary>The tenant's documents: RLS limits the connection to the tenant in its session context.</summary>
    private static async Task<int> DocumentCountAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM inventory.StockDocuments", connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }
}