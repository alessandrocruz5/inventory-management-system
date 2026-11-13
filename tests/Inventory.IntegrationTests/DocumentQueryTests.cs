using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Time.Testing;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>GET /api/documents: the tenant's documents, newest first, filtered and paged (spec §9).</summary>
public sealed class DocumentQueryTests(DatabaseFixture db)
{
    private static readonly DateTimeOffset T1 = new(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddHours(1);
    private static readonly DateTimeOffset T3 = T1.AddHours(2);

    private static string Stamp(DateTimeOffset t) => Uri.EscapeDataString(t.ToString("O"));

    private static async Task<JsonNode> GetAsync(HttpClient client, string query, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/documents" + query, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static List<Guid> Ids(JsonNode list) => list["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ToList();

    private static async Task<Guid> PostAsync(HttpClient client, string route, object body, CancellationToken ct)
    {
        var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
    }

    private async Task<(ApiFactory App, Guid Tenant, HttpClient Client, FakeTimeProvider Clock)> StartAsync()
    {
        var fake = new FakeTimeProvider(T1);
        var app = new ApiFactory(db.AppConnectionString) { Clock = fake };
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        return (app, tenant, await DatabaseFixture.SignInAsync(app, email), fake);
    }

    /// <summary>Three receipts posted at T1, T2 and T3; returns their ids in that order.</summary>
    private static async Task<(Guid Location, Guid Item, Guid[] Receipts)> PostThreeAsync(HttpClient client, FakeTimeProvider clock, CancellationToken ct)
    {
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var ids = new List<Guid>();
        foreach (var t in new[] { T1, T2, T3 })
        {
            clock.SetUtcNow(t);
            ids.Add(await PostAsync(client, PostingTests.Receipts, PostingTests.Receipt(location, PostingTests.Line(item, 1m, 10m)), ct));
        }

        return (location, item, [.. ids]);
    }

    [Fact]
    public async Task Lists_newest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, clock) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var (_, _, r) = await PostThreeAsync(client, clock, ct);

        var list = await GetAsync(client, "", ct);

        Ids(list).ShouldBe([r[2], r[1], r[0]]);
        list["total"]!.GetValue<int>().ShouldBe(3);
    }

    [Fact]
    public async Task Posted_range_is_half_open()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, clock) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var (_, _, r) = await PostThreeAsync(client, clock, ct);

        Ids(await GetAsync(client, $"?postedFrom={Stamp(T2)}", ct)).ShouldBe([r[2], r[1]]);
        Ids(await GetAsync(client, $"?postedTo={Stamp(T3)}", ct)).ShouldBe([r[1], r[0]]);
        Ids(await GetAsync(client, $"?postedFrom={Stamp(T2)}&postedTo={Stamp(T3)}", ct)).ShouldBe([r[1]]);
    }

    [Fact]
    public async Task Type_filter_matches_exact_names()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, clock) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostAsync(client, PostingTests.Receipts, PostingTests.Receipt(location, PostingTests.Line(item, 2m, 10m)), ct);
        clock.SetUtcNow(T2);
        var issue = await PostAsync(client, PostingTests.Issues, PostingTests.Issue(location, PostingTests.IssueLine(item, 1m)), ct);

        var list = await GetAsync(client, "?type=Issue", ct);

        Ids(list).ShouldBe([issue]);
        list["items"]![0]!["type"]!.GetValue<string>().ShouldBe("Issue");
        await PostingTests.AssertValidationAsync(await client.GetAsync("/api/documents?type=issue", ct), "type", ct);
        await PostingTests.AssertValidationAsync(await client.GetAsync("/api/documents?type=1", ct), "type", ct);
    }

    [Fact]
    public async Task Range_end_must_follow_start()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, _) = await StartAsync();
        await using var _ = app;
        using var __ = client;

        await PostingTests.AssertValidationAsync(
            await client.GetAsync($"/api/documents?postedFrom={Stamp(T2)}&postedTo={Stamp(T2)}", ct), "postedTo", ct);
    }

    [Fact]
    public async Task Line_count_and_reason_code_are_listed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, clock) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var rice = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var beans = await PostingTests.NewItemAsync(client, "BEANS-1", ct);
        var receipt = await PostAsync(client, PostingTests.Receipts,
            PostingTests.Receipt(location, PostingTests.Line(rice, 5m, 10m), PostingTests.Line(beans, 5m, 20m)), ct);
        clock.SetUtcNow(T2);
        var adjustment = await PostAsync(client, AdjustmentTests.Adjustments,
            AdjustmentTests.Adjustment(location, "Damaged", AdjustmentTests.Delta(rice, -1m)), ct);

        var items = (await GetAsync(client, "", ct))["items"]!.AsArray();

        var adjusted = items.Single(i => i!["id"]!.GetValue<Guid>() == adjustment)!;
        adjusted["type"]!.GetValue<string>().ShouldBe("Adjustment");
        adjusted["reasonCode"]!.GetValue<string>().ShouldBe("Damaged");
        adjusted["lineCount"]!.GetValue<int>().ShouldBe(1);
        var received = items.Single(i => i!["id"]!.GetValue<Guid>() == receipt)!;
        received["lineCount"]!.GetValue<int>().ShouldBe(2);
        received["reasonCode"].ShouldBeNull();
    }

    [Fact]
    public async Task Reversal_links_point_both_ways()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, tenant, client, clock) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var receipt = await PostAsync(client, PostingTests.Receipts, PostingTests.Receipt(location, PostingTests.Line(item, 2m, 10m)), ct);
        clock.SetUtcNow(T2);
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var reversal = await LedgerRows.AddDocumentAsync(c, ct, type: "Reversal", reverses: receipt);

        var items = (await GetAsync(client, "", ct))["items"]!.AsArray();

        var original = items.Single(i => i!["id"]!.GetValue<Guid>() == receipt)!;
        original["reversedByDocumentId"]!.GetValue<Guid>().ShouldBe(reversal);
        original["reversesDocumentId"].ShouldBeNull();
        var reverse = items.Single(i => i!["id"]!.GetValue<Guid>() == reversal)!;
        reverse["reversesDocumentId"]!.GetValue<Guid>().ShouldBe(receipt);
        reverse["reversedByDocumentId"].ShouldBeNull();
    }

    [Fact]
    public async Task Paging_is_validated()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, _) = await StartAsync();
        await using var _ = app;
        using var __ = client;

        await PostingTests.AssertValidationAsync(await client.GetAsync("/api/documents?pageSize=0", ct), "pageSize", ct);
    }

    [Fact]
    public async Task Staff_can_list_documents()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, tenant, admin, _) = await StartAsync();
        await using var _ = app;
        using var __ = admin;
        var (_, email) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, email);

        (await staff.GetAsync("/api/documents", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<JsonNode> GetDetailAsync(HttpClient client, Guid id, CancellationToken ct)
    {
        var response = await client.GetAsync($"/api/documents/{id}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    [Fact]
    public async Task Location_header_of_a_receipt_resolves()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, _) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var posted = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, PostingTests.NewKey(),
            PostingTests.Receipt(location, PostingTests.Line(item, 10m, 100m)), ct);
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await posted.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();

        var response = await client.GetAsync(posted.Headers.Location, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        detail["id"]!.GetValue<Guid>().ShouldBe(id);
        detail["type"]!.GetValue<string>().ShouldBe("Receipt");
        var line = detail["lines"]!.AsArray().Single()!;
        line["sku"]!.GetValue<string>().ShouldBe("RICE-1");
        line["fromLocationCode"]!.GetValue<string>().ShouldBe("SUP");
        line["toLocationCode"]!.GetValue<string>().ShouldBe("WH-1");
        line["quantity"]!.GetValue<decimal>().ShouldBe(10m);
        line["unitCost"]!.GetValue<decimal>().ShouldBe(100m);
        line["totalCost"]!.GetValue<decimal>().ShouldBe(1000m);
    }

    [Fact]
    public async Task Transfer_line_shows_from_and_to()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, _) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var a = await PostingTests.NewLocationAsync(client, ct, "WH-A");
        var b = await PostingTests.NewLocationAsync(client, ct, "WH-B");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 10m, 100m, ct);
        var transfer = await PostAsync(client, TransferTests.Transfers, TransferTests.Transfer(a, b, TransferTests.TransferLine(item, 4m)), ct);

        var line = (await GetDetailAsync(client, transfer, ct))["lines"]!.AsArray().Single()!;

        line["fromLocationCode"]!.GetValue<string>().ShouldBe("WH-A");
        line["toLocationCode"]!.GetValue<string>().ShouldBe("WH-B");
        line["totalCost"]!.GetValue<decimal>().ShouldBe(400m);
    }

    [Fact]
    public async Task Adjustment_lines_show_direction()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, _) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var item2 = await PostingTests.NewItemAsync(client, "BEANS-1", ct);
        await PostingTests.ReceiveAsync(client, location, item2, 5m, 10m, ct);
        var adjustment = await PostAsync(client, AdjustmentTests.Adjustments,
            AdjustmentTests.Adjustment(location, "Found", AdjustmentTests.Delta(item, 2m, 5m), AdjustmentTests.Delta(item2, -1m)), ct);

        var detail = await GetDetailAsync(client, adjustment, ct);

        detail["reasonCode"]!.GetValue<string>().ShouldBe("Found");
        var lines = detail["lines"]!.AsArray();
        lines.Count.ShouldBe(2);
        lines[0]!["fromLocationCode"]!.GetValue<string>().ShouldBe("ADJ");
        lines[0]!["toLocationCode"]!.GetValue<string>().ShouldBe("WH-1");
        lines[1]!["fromLocationCode"]!.GetValue<string>().ShouldBe("WH-1");
        lines[1]!["toLocationCode"]!.GetValue<string>().ShouldBe("ADJ");
    }

    [Fact]
    public async Task Reversal_links_point_both_ways_on_detail()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, tenant, client, clock) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var receipt = await PostAsync(client, PostingTests.Receipts, PostingTests.Receipt(location, PostingTests.Line(item, 2m, 10m)), ct);
        clock.SetUtcNow(T2);
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var reversal = await LedgerRows.AddDocumentAsync(c, ct, type: "Reversal", reverses: receipt);

        var original = await GetDetailAsync(client, receipt, ct);
        var reverse = await GetDetailAsync(client, reversal, ct);

        original["reversedByDocumentId"]!.GetValue<Guid>().ShouldBe(reversal);
        original["reversesDocumentId"].ShouldBeNull();
        reverse["reversesDocumentId"]!.GetValue<Guid>().ShouldBe(receipt);
        reverse["reversedByDocumentId"].ShouldBeNull();
    }

    [Fact]
    public async Task Revaluation_movements_are_not_counted_as_lines()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, tenant, client, _) = await StartAsync();
        await using var _ = app;
        using var __ = client;
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var item = await LedgerRows.AddItemAsync(c, "REVAL-1", ct);
        var location = await LedgerRows.AddLocationAsync(c, "WH-R", ct);
        var supplier = await LedgerRows.LocationIdAsync(c, "SUP", ct);
        var adjustments = await LedgerRows.LocationIdAsync(c, "ADJ", ct);
        var document = await LedgerRows.AddDocumentAsync(c, ct);
        await LedgerRows.AddMovementAsync(c, document, 1, item, supplier, location, 5m, 50m, ct);
        await LedgerRows.AddMovementAsync(c, document, 1001, item, adjustments, location, 0m, 3m, ct);

        var row = (await GetAsync(client, "", ct))["items"]!.AsArray().Single(i => i!["id"]!.GetValue<Guid>() == document)!;
        var lines = (await GetDetailAsync(client, document, ct))["lines"]!.AsArray();

        row["lineCount"]!.GetValue<int>().ShouldBe(1);
        lines.Select(l => l!["lineNo"]!.GetValue<int>()).ShouldBe([1, 1001]);
        lines[1]!["unitCost"].ShouldBeNull();
        lines[1]!["totalCost"]!.GetValue<decimal>().ShouldBe(3m);
    }

    [Fact]
    public async Task Unknown_document_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, _, client, _) = await StartAsync();
        await using var _ = app;
        using var __ = client;

        var response = await client.GetAsync($"/api/documents/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
    }

    [Fact]
    public async Task Staff_can_read_a_document()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, tenant, admin, _) = await StartAsync();
        await using var _ = app;
        using var __ = admin;
        var location = await PostingTests.NewLocationAsync(admin, ct);
        var item = await PostingTests.NewItemAsync(admin, "RICE-1", ct);
        var receipt = await PostAsync(admin, PostingTests.Receipts, PostingTests.Receipt(location, PostingTests.Line(item, 1m, 10m)), ct);
        var (_, email) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, email);

        (await staff.GetAsync($"/api/documents/{receipt}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}