using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>GET /api/stock: physical balances with average cost and filter-wide totals (spec §9).</summary>
public sealed class StockTests(DatabaseFixture db)
{
    private static async Task<JsonNode> GetAsync(HttpClient client, string query, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/stock" + query, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private async Task<(ApiFactory App, Guid Tenant, HttpClient Client)> StartAsync()
    {
        var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        return (app, tenant, await DatabaseFixture.SignInAsync(app, email));
    }

    [Fact]
    public async Task Average_cost_is_value_over_on_hand_rounded_to_4dp()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "AVG-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 10m, 100m, ct);
        await PostingTests.ReceiveAsync(client, location, item, 3m, 33.3333m, ct);

        var list = await GetAsync(client, "", ct);

        var row = list["items"]!.AsArray().Single()!;
        row["onHand"]!.GetValue<decimal>().ShouldBe(13m);
        row["stockValue"]!.GetValue<decimal>().ShouldBe(1099.9999m);
        row["averageCost"]!.GetValue<decimal>().ShouldBe(84.6154m);
        row["lastUnitCost"].ShouldNotBeNull();
        row["sku"]!.GetValue<string>().ShouldBe("AVG-1");
        row["itemName"]!.GetValue<string>().ShouldBe("AVG-1");
        row["baseUnit"]!.GetValue<string>().ShouldBe("pc");
        row["locationCode"]!.GetValue<string>().ShouldBe("WH-1");
        row["locationName"]!.GetValue<string>().ShouldBe("Warehouse");
    }

    [Fact]
    public async Task Zero_balance_row_has_no_average_cost()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "ZERO-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 2m, 5m, ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(item, 2m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await GetAsync(client, "", ct);

        var row = list["items"]!.AsArray().Single()!;
        row["onHand"]!.GetValue<decimal>().ShouldBe(0m);
        row["stockValue"]!.GetValue<decimal>().ShouldBe(0m);
        row["averageCost"].ShouldBeNull();
    }

    [Fact]
    public async Task Virtual_locations_never_appear()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "VIRT-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 5m, 10m, ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(item, 1m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await GetAsync(client, "", ct);

        var rows = list["items"]!.AsArray();
        rows.Select(r => r!["locationCode"]!.GetValue<string>()).ShouldAllBe(c => c == "WH-1");
        list["total"]!.GetValue<int>().ShouldBe(rows.Count);
        rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Totals_cover_the_whole_filter_not_the_page()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var value = 0m;
        foreach (var (sku, qty, cost) in new[] { ("T-1", 1m, 10m), ("T-2", 2m, 20.5m), ("T-3", 3m, 7.25m) })
        {
            var item = await PostingTests.NewItemAsync(client, sku, ct);
            await PostingTests.ReceiveAsync(client, location, item, qty, cost, ct);
            value += qty * cost;
        }

        foreach (var query in new[] { "?pageSize=1", "?page=2&pageSize=1" })
        {
            var list = await GetAsync(client, query, ct);

            list["items"]!.AsArray().Count.ShouldBe(1);
            list["total"]!.GetValue<int>().ShouldBe(3);
            list["totals"]!["stockValue"]!.GetValue<decimal>().ShouldBe(value);
        }
    }

    [Fact]
    public async Task Filters_narrow_rows_and_totals()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var locA = await PostingTests.NewLocationAsync(client, ct, "LOC-A");
        var locB = await PostingTests.NewLocationAsync(client, ct, "LOC-B");
        var itemI = await PostingTests.NewItemAsync(client, "I-1", ct);
        var itemJ = await PostingTests.NewItemAsync(client, "J-1", ct);
        await PostingTests.ReceiveAsync(client, locA, itemI, 1m, 10m, ct);
        await PostingTests.ReceiveAsync(client, locA, itemJ, 2m, 20m, ct);
        await PostingTests.ReceiveAsync(client, locB, itemI, 3m, 30m, ct);
        await PostingTests.ReceiveAsync(client, locB, itemJ, 4m, 40m, ct);

        var byLocation = await GetAsync(client, $"?locationId={locA}", ct);
        byLocation["items"]!.AsArray().Select(r => r!["locationId"]!.GetValue<Guid>()).ShouldAllBe(l => l == locA);
        byLocation["total"]!.GetValue<int>().ShouldBe(2);
        byLocation["totals"]!["stockValue"]!.GetValue<decimal>().ShouldBe(50m);

        var byItem = await GetAsync(client, $"?itemId={itemI}", ct);
        byItem["items"]!.AsArray().Select(r => r!["itemId"]!.GetValue<Guid>()).ShouldAllBe(i => i == itemI);
        byItem["total"]!.GetValue<int>().ShouldBe(2);
        byItem["totals"]!["stockValue"]!.GetValue<decimal>().ShouldBe(100m);

        var both = await GetAsync(client, $"?itemId={itemI}&locationId={locB}", ct);
        both["total"]!.GetValue<int>().ShouldBe(1);
        both["totals"]!["stockValue"]!.GetValue<decimal>().ShouldBe(90m);
    }

    [Fact]
    public async Task Rows_are_ordered_by_sku_then_location_code()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var locB = await PostingTests.NewLocationAsync(client, ct, "LOC-B");
        var locA = await PostingTests.NewLocationAsync(client, ct, "LOC-A");
        var itemZ = await PostingTests.NewItemAsync(client, "SKU-Z", ct);
        var itemA = await PostingTests.NewItemAsync(client, "SKU-A", ct);
        foreach (var (loc, item) in new[] { (locB, itemZ), (locA, itemZ), (locB, itemA), (locA, itemA) })
        {
            await PostingTests.ReceiveAsync(client, loc, item, 1m, 1m, ct);
        }

        var list = await GetAsync(client, "", ct);

        list["items"]!.AsArray().Select(r => $"{r!["sku"]}|{r["locationCode"]}")
            .ShouldBe(["SKU-A|LOC-A", "SKU-A|LOC-B", "SKU-Z|LOC-A", "SKU-Z|LOC-B"]);
    }

    [Fact]
    public async Task Paging_is_validated()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;

        await PostingTests.AssertValidationAsync(await client.GetAsync("/api/stock?pageSize=101", ct), "pageSize", ct);
        await PostingTests.AssertValidationAsync(await client.GetAsync("/api/stock?page=0", ct), "page", ct);
    }

    [Fact]
    public async Task Staff_can_read_stock()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, tenant, admin) = await StartAsync();
        await using var _ = app;
        using var __ = admin;
        var (_, email) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, email);

        (await staff.GetAsync("/api/stock", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}