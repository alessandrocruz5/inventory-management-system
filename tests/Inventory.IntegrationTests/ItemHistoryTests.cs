using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>GET /api/items/{id} balances and GET /api/items/{id}/movements running balance (spec §9, vectors V1–V6 of §7).</summary>
public sealed class ItemHistoryTests(DatabaseFixture db)
{
    /// <summary>Spec §7 vector set V through the API, steps V1..upTo (5 or 6). Returns the locations, the item and each step's document id.</summary>
    internal static async Task<(Guid A, Guid B, Guid Item, Guid[] Documents)> PostVAsync(HttpClient client, int upTo, CancellationToken ct)
    {
        var a = await PostingTests.NewLocationAsync(client, ct, "LOC-A");
        var b = await PostingTests.NewLocationAsync(client, ct, "LOC-B");
        var item = await PostingTests.NewItemAsync(client, "V-ITEM", ct);

        var steps = new List<(string Route, object Body)>
        {
            (PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 10m, 100m))),
            (PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 10m, 120m))),
            (PostingTests.Issues, PostingTests.Issue(a, PostingTests.IssueLine(item, 15m))),
            (PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 5m, 130m))),
            (TransferTests.Transfers, TransferTests.Transfer(a, b, TransferTests.TransferLine(item, 4m))),
        };
        if (upTo == 6)
        {
            steps.Add((PostingTests.Receipts, PostingTests.Receipt(b, PostingTests.Line(item, 6m, 125m))));
        }

        var documents = new List<Guid>();
        foreach (var (route, body) in steps)
        {
            var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            documents.Add((await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>());
        }

        return (a, b, item, [.. documents]);
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        var response = await client.GetAsync(path, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    [Fact]
    public async Task Item_lists_its_balance_at_each_physical_location()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var (_, _, item, _) = await PostVAsync(client, 5, ct);

        var body = await GetJsonAsync(client, $"/api/items/{item}", ct);

        body["sku"]!.GetValue<string>().ShouldBe("V-ITEM");
        Convert.FromBase64String(body["version"]!.GetValue<string>()).ShouldNotBeEmpty();
        var balances = body["balances"]!.AsArray();
        balances.Count.ShouldBe(2);
        AssertBalance(balances[0]!, "LOC-A", 6m, 720.0000m, 120.0000m, 120.0000m);
        AssertBalance(balances[1]!, "LOC-B", 4m, 480.0000m, 120.0000m, 120.0000m);
        balances.Select(x => x!["locationCode"]!.GetValue<string>()).ShouldNotContain(c => c == "SUP" || c == "CUS" || c == "ADJ");
    }

    private static void AssertBalance(JsonNode balance, string code, decimal onHand, decimal stockValue, decimal averageCost, decimal lastUnitCost)
    {
        balance["locationCode"]!.GetValue<string>().ShouldBe(code);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(onHand);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(stockValue);
        balance["averageCost"]!.GetValue<decimal>().ShouldBe(averageCost);
        balance["lastUnitCost"]!.GetValue<decimal>().ShouldBe(lastUnitCost);
    }

    [Fact]
    public async Task Item_with_no_stock_has_no_balances()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var item = await PostingTests.NewItemAsync(client, "EMPTY-1", ct);

        var body = await GetJsonAsync(client, $"/api/items/{item}", ct);

        body["balances"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Item_issued_to_zero_shows_a_zero_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "ZERO-1", ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, PostingTests.NewKey(),
            PostingTests.Receipt(location, PostingTests.Line(item, 2m, 10m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(item, 2m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await GetJsonAsync(client, $"/api/items/{item}", ct);

        var balances = body["balances"]!.AsArray();
        balances.Count.ShouldBe(1);
        balances[0]!["onHand"]!.GetValue<decimal>().ShouldBe(0m);
        balances[0]!["stockValue"]!.GetValue<decimal>().ShouldBe(0m);
        balances[0]!["averageCost"].ShouldBeNull();
    }

    [Fact]
    public async Task Staff_can_read_an_item_with_balances()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, email);
        var (_, _, item, _) = await PostVAsync(admin, 5, ct);
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var adminBody = await GetJsonAsync(admin, $"/api/items/{item}", ct);
        var staffBody = await GetJsonAsync(staff, $"/api/items/{item}", ct);

        staffBody["balances"]!.AsArray().Count.ShouldBe(adminBody["balances"]!.AsArray().Count);
    }

    private static async Task<JsonNode> MovementsAsync(HttpClient client, Guid item, string query, CancellationToken ct) =>
        await GetJsonAsync(client, $"/api/items/{item}/movements{query}", ct);

    private static void AssertRow(JsonNode row, string location, string direction, decimal quantity, decimal value,
        decimal runningOnHand, decimal runningValue, string counterpart, Guid document, string documentType)
    {
        row["locationCode"]!.GetValue<string>().ShouldBe(location);
        row["direction"]!.GetValue<string>().ShouldBe(direction);
        row["quantityChange"]!.GetValue<decimal>().ShouldBe(quantity);
        row["valueChange"]!.GetValue<decimal>().ShouldBe(value);
        row["runningOnHand"]!.GetValue<decimal>().ShouldBe(runningOnHand);
        row["runningValue"]!.GetValue<decimal>().ShouldBe(runningValue);
        row["counterpartLocationCode"]!.GetValue<string>().ShouldBe(counterpart);
        row["documentId"]!.GetValue<Guid>().ShouldBe(document);
        row["documentType"]!.GetValue<string>().ShouldBe(documentType);
    }

    [Fact]
    public async Task Running_balance_matches_V1_to_V6()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var (_, _, item, v) = await PostVAsync(client, 6, ct);

        var body = await MovementsAsync(client, item, "?pageSize=100", ct);

        body["total"]!.GetValue<int>().ShouldBe(7);
        var rows = body["items"]!.AsArray();
        rows.Count.ShouldBe(7);
        AssertRow(rows[0]!, "LOC-B", "In", 6m, 750m, 10m, 1230m, "SUP", v[5], "Receipt");
        AssertRow(rows[1]!, "LOC-B", "In", 4m, 480m, 4m, 480m, "LOC-A", v[4], "Transfer");
        AssertRow(rows[2]!, "LOC-A", "Out", -4m, -480m, 6m, 720m, "LOC-B", v[4], "Transfer");
        AssertRow(rows[3]!, "LOC-A", "In", 5m, 650m, 10m, 1200m, "SUP", v[3], "Receipt");
        AssertRow(rows[4]!, "LOC-A", "Out", -15m, -1650m, 5m, 550m, "CUS", v[2], "Issue");
        AssertRow(rows[5]!, "LOC-A", "In", 10m, 1200m, 20m, 2200m, "SUP", v[1], "Receipt");
        AssertRow(rows[6]!, "LOC-A", "In", 10m, 1000m, 10m, 1000m, "SUP", v[0], "Receipt");
    }

    [Fact]
    public async Task Newest_row_per_location_equals_the_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var (_, _, item, _) = await PostVAsync(client, 6, ct);

        var rows = (await MovementsAsync(client, item, "?pageSize=100", ct))["items"]!.AsArray();
        var balances = (await GetJsonAsync(client, $"/api/items/{item}", ct))["balances"]!.AsArray();

        balances.Count.ShouldBe(2);
        foreach (var balance in balances)
        {
            var location = balance!["locationId"]!.GetValue<Guid>();
            var newest = rows.First(r => r!["locationId"]!.GetValue<Guid>() == location)!;
            newest["runningOnHand"]!.GetValue<decimal>().ShouldBe(balance["onHand"]!.GetValue<decimal>());
            newest["runningValue"]!.GetValue<decimal>().ShouldBe(balance["stockValue"]!.GetValue<decimal>());
        }
    }

    [Fact]
    public async Task Running_values_span_the_whole_history_across_pages()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var (_, _, item, v) = await PostVAsync(client, 6, ct);

        var body = await MovementsAsync(client, item, "?page=2&pageSize=2", ct);

        body["total"]!.GetValue<int>().ShouldBe(7);
        body["page"]!.GetValue<int>().ShouldBe(2);
        var rows = body["items"]!.AsArray();
        rows.Count.ShouldBe(2);
        AssertRow(rows[0]!, "LOC-A", "Out", -4m, -480m, 6m, 720m, "LOC-B", v[4], "Transfer");
        AssertRow(rows[1]!, "LOC-A", "In", 5m, 650m, 10m, 1200m, "SUP", v[3], "Receipt");
    }

    [Fact]
    public async Task Revaluation_row_moves_value_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var locR = await PostingTests.NewLocationAsync(client, ct, "LOC-R");
        var item = await PostingTests.NewItemAsync(client, "REVAL-1", ct);
        await PostingTests.ReceiveAsync(client, locR, item, 7m, 56m, ct);
        await using (var c = await LedgerRows.OpenAsync(db, tenant, ct))
        {
            var adj = await LedgerRows.LocationIdAsync(c, "ADJ", ct);
            var doc = await LedgerRows.AddDocumentAsync(c, ct);
            await LedgerRows.AddMovementAsync(c, doc, 1001, item, locR, adj, 0m, 18m, ct);
        }

        var body = await MovementsAsync(client, item, "", ct);

        body["total"]!.GetValue<int>().ShouldBe(2);
        var first = body["items"]!.AsArray()[0]!;
        first["locationCode"]!.GetValue<string>().ShouldBe("LOC-R");
        first["direction"]!.GetValue<string>().ShouldBe("Out");
        first["quantityChange"]!.GetValue<decimal>().ShouldBe(0m);
        first["valueChange"]!.GetValue<decimal>().ShouldBe(-18m);
        first["runningOnHand"]!.GetValue<decimal>().ShouldBe(7m);
        first["runningValue"]!.GetValue<decimal>().ShouldBe(374m);
        first["counterpartLocationCode"]!.GetValue<string>().ShouldBe("ADJ");
    }

    [Fact]
    public async Task Item_without_movements_has_an_empty_history()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var item = await PostingTests.NewItemAsync(client, "NOMOVE-1", ct);

        var body = await MovementsAsync(client, item, "", ct);

        body["total"]!.GetValue<int>().ShouldBe(0);
        body["items"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_item_history_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var response = await client.GetAsync($"/api/items/{Guid.NewGuid()}/movements", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
    }

    [Fact]
    public async Task History_page_size_over_100_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var item = await PostingTests.NewItemAsync(client, "PAGE-1", ct);

        var response = await client.GetAsync($"/api/items/{item}/movements?pageSize=101", ct);

        await PostingTests.AssertValidationAsync(response, "pageSize", ct);
    }

    [Fact]
    public async Task Staff_can_read_item_history()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, email);
        var (_, _, item, _) = await PostVAsync(admin, 5, ct);
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var body = await MovementsAsync(staff, item, "?pageSize=100", ct);

        body["total"]!.GetValue<int>().ShouldBe(6);
    }
}