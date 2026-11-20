using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Assistant.Prompting;
using Assistant.Tools;

using Kernel.Tenancy;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>The assistant's read tools, called directly inside a tenant scope. Nothing here contacts a model.</summary>
public sealed class AssistantToolTests(DatabaseFixture db)
{
    private static async Task<(Guid Tenant, HttpClient Client)> StartAsync(ApiFactory app, string role = Roles.Admin)
    {
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, role);
        return (tenant, await DatabaseFixture.SignInAsync(app, email));
    }

    private static async Task<JsonNode> GetAsync(HttpClient client, string url, CancellationToken ct)
    {
        var response = await client.GetAsync(url, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static async Task<Guid> PostAsync(HttpClient client, string route, object body, CancellationToken ct)
    {
        var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
    }

    private static ITenantScope Scope(ApiFactory app, Guid tenant) =>
        app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);

    private static AIFunction Tool(ITenantScope scope, string role, string name) =>
        scope.Services.GetRequiredService<ToolCatalog>().For(role).OfType<AIFunction>().Single(f => f.Name == name);

    /// <summary>Calls a tool the way the chat pipeline will and returns its result as JSON.</summary>
    private static async Task<JsonNode> CallAsync(ITenantScope scope, string role, string name, CancellationToken ct, params (string Name, object? Value)[] arguments)
    {
        var args = new AIFunctionArguments();
        foreach (var (key, value) in arguments)
        {
            args[key] = value;
        }

        var result = await Tool(scope, role, name).InvokeAsync(args, ct);
        return result switch
        {
            JsonElement element => JsonNode.Parse(element.GetRawText())!,
            _ => JsonSerializer.SerializeToNode(result, ToolJson.Options)!,
        };
    }

    private static decimal? Dec(JsonNode? node) => node is null ? null : node.GetValue<decimal>();

    [Fact]
    public async Task Stock_tool_returns_the_callers_balances()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var x = await PostingTests.NewItemAsync(client, "X-1", ct);
        var y = await PostingTests.NewItemAsync(client, "Y-1", ct);
        await PostingTests.ReceiveAsync(client, a, x, 10m, 100m, ct);
        await PostingTests.ReceiveAsync(client, a, x, 5m, 130m, ct);
        await PostingTests.ReceiveAsync(client, b, y, 7m, 12.5m, ct);
        await PostAsync(client, PostingTests.Issues, PostingTests.Issue(a, PostingTests.IssueLine(x, 4m)), ct);
        var route = await GetAsync(client, "/api/stock?pageSize=100", ct);

        await using var scope = Scope(app, tenant);
        var stock = await CallAsync(scope, RoleNames.Staff, "get_stock", ct);

        var rows = stock["rows"]!.AsArray();
        var expected = route["items"]!.AsArray();
        rows.Count.ShouldBe(expected.Count);
        rows.Count.ShouldBe(2);
        foreach (var row in rows)
        {
            var match = expected.Single(e => e!["sku"]!.GetValue<string>() == row!["sku"]!.GetValue<string>() && e["locationCode"]!.GetValue<string>() == row["location"]!.GetValue<string>())!;
            Dec(row!["onHand"]).ShouldBe(Dec(match["onHand"]));
            Dec(row!["stockValue"]).ShouldBe(Dec(match["stockValue"]));
            Dec(row!["averageCost"]).ShouldBe(Dec(match["averageCost"]));
            row["unit"]!.GetValue<string>().ShouldBe("pc");
        }

        Dec(stock["totalValue"]).ShouldBe(Dec(route["totals"]!["stockValue"]));
        stock["total"]!.GetValue<int>().ShouldBe(2);
        stock["truncated"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Codes_match_case_insensitively()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var location = await PostingTests.NewLocationAsync(client, ct, "WH-1");
        var item = await PostingTests.NewItemAsync(client, "Rice-25", ct);
        await PostingTests.ReceiveAsync(client, location, item, 3m, 10m, ct);

        await using var scope = Scope(app, tenant);
        var stock = await CallAsync(scope, RoleNames.Staff, "get_stock", ct, ("sku", "rice-25"), ("location", "wh-1"));
        var one = await CallAsync(scope, RoleNames.Staff, "get_item", ct, ("sku", "RICE-25"));
        var history = await CallAsync(scope, RoleNames.Staff, "get_item_history", ct, ("sku", "rice-25"), ("location", "Wh-1"));

        stock["rows"]!.AsArray().ShouldHaveSingleItem()!["sku"]!.GetValue<string>().ShouldBe("Rice-25");
        one["sku"]!.GetValue<string>().ShouldBe("Rice-25");
        one["balances"]!.AsArray().ShouldHaveSingleItem()!["location"]!.GetValue<string>().ShouldBe("WH-1");
        history["movements"]!.AsArray().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Foreign_skus_and_documents_are_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenantA, clientA) = await StartAsync(app);
        using var _a = clientA;
        var (_, clientB) = await StartAsync(app);
        using var _b = clientB;
        var locationA = await PostingTests.NewLocationAsync(clientA, ct, "A-LOC");
        var itemA = await PostingTests.NewItemAsync(clientA, "A-ITEM", ct);
        await PostingTests.ReceiveAsync(clientA, locationA, itemA, 1m, 1m, ct);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct, "B-LOC");
        var itemB = (await ItemsTests.CreateAsync(clientB, "B-ITEM", "Zebra Secret Item", "pc", ct))["id"]!.GetValue<Guid>();
        var documentB = await PostAsync(clientB, PostingTests.Receipts,
            new { locationId = locationB, reference = "B-REF-1", notes = "Zebra notes", lines = new[] { PostingTests.Line(itemB, 5m, 9m) } }, ct);

        await using var scope = Scope(app, tenantA);
        var results = new[]
        {
            await CallAsync(scope, RoleNames.Admin, "get_item", ct, ("sku", "B-ITEM")),
            await CallAsync(scope, RoleNames.Admin, "get_stock", ct, ("sku", "B-ITEM")),
            await CallAsync(scope, RoleNames.Admin, "get_stock", ct, ("location", "B-LOC")),
            await CallAsync(scope, RoleNames.Admin, "get_item_history", ct, ("sku", "B-ITEM")),
            await CallAsync(scope, RoleNames.Admin, "get_document", ct, ("documentId", documentB.ToString())),
        };

        foreach (var result in results)
        {
            result["error"]!.GetValue<string>().ShouldBe("not-found");
        }

        var search = await CallAsync(scope, RoleNames.Admin, "find_items", ct, ("query", "Zebra"));
        search["items"]!.AsArray().ShouldBeEmpty();
        search["total"]!.GetValue<int>().ShouldBe(0);
        var documents = await CallAsync(scope, RoleNames.Admin, "list_documents", ct);
        documents["total"]!.GetValue<int>().ShouldBe(1);
        var locations = await CallAsync(scope, RoleNames.Admin, "list_locations", ct);

        // Not-found messages echo what the caller typed, so only the foreign names and reference must never appear.
        var all = string.Concat(results.Concat([search, documents, locations]).Select(r => r.ToJsonString()));
        all.ShouldNotContain("Zebra");
        all.ShouldNotContain("B-REF-1");

        // The data-carrying results hold nothing of B's.
        var data = string.Concat(new[] { search, documents, locations }.Select(r => r.ToJsonString()));
        data.ShouldNotContain("B-LOC");
        data.ShouldNotContain("B-ITEM");
        data.ShouldNotContain(documentB.ToString());
    }

    [Fact]
    public async Task History_is_newest_first_in_manila_time()
    {
        var ct = TestContext.Current.CancellationToken;
        var fake = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 1, 30, 0, TimeSpan.Zero));
        await using var app = new ApiFactory(db.AppConnectionString) { Clock = fake };
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var location = await PostingTests.NewLocationAsync(client, ct, "WH-1");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 10m, 20m, ct);
        fake.Advance(TimeSpan.FromMinutes(5));
        await PostAsync(client, PostingTests.Issues, PostingTests.Issue(location, PostingTests.IssueLine(item, 4m)), ct);

        await using var scope = Scope(app, tenant);
        var history = await CallAsync(scope, RoleNames.Staff, "get_item_history", ct, ("sku", "RICE-1"));

        var movements = history["movements"]!.AsArray();
        movements.Count.ShouldBe(2);
        movements[0]!["documentType"]!.GetValue<string>().ShouldBe("Issue");
        movements[0]!["postedAt"]!.GetValue<string>().ShouldBe("2026-11-20 09:35");
        Dec(movements[0]!["onHandAfter"]).ShouldBe(6m);
        movements[1]!["documentType"]!.GetValue<string>().ShouldBe("Receipt");
        movements[1]!["postedAt"]!.GetValue<string>().ShouldBe("2026-11-20 09:30");
        Dec(movements[1]!["onHandAfter"]).ShouldBe(10m);
        history["truncated"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Lists_are_capped_and_say_so()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        for (var i = 1; i <= 30; i++)
        {
            await PostingTests.NewItemAsync(client, $"ITM-{i:00}", ct);
        }

        await using var scope = Scope(app, tenant);
        var items = await CallAsync(scope, RoleNames.Staff, "find_items", ct);

        items["items"]!.AsArray().Count.ShouldBe(25);
        items["total"]!.GetValue<int>().ShouldBe(30);
        items["truncated"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Ledger_health_is_in_the_admin_catalog_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;

        await using var scope = Scope(app, tenant);
        var catalog = scope.Services.GetRequiredService<ToolCatalog>();
        var admin = catalog.For(RoleNames.Admin).Select(t => t.Name).ToList();
        var staff = catalog.For(RoleNames.Staff).Select(t => t.Name).ToList();

        admin.ShouldContain("get_ledger_health");
        staff.ShouldNotContain("get_ledger_health");
        staff.Count.ShouldBe(8);
        admin.Count.ShouldBe(9);
        catalog.For("anything-else").Select(t => t.Name).ShouldNotContain("get_ledger_health");

        var health = await CallAsync(scope, RoleNames.Admin, "get_ledger_health", ct);
        health["healthy"]!.GetValue<bool>().ShouldBeTrue();
        health["invariants"]!.AsArray().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Results_carry_no_user_identity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var userId = (await GetAsync(client, "/api/auth/me", ct))["id"]!.GetValue<Guid>();
        var location = await PostingTests.NewLocationAsync(client, ct, "WH-1");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var document = await PostAsync(client, PostingTests.Receipts,
            new { locationId = location, reference = "PO-1", notes = "first delivery", lines = new[] { PostingTests.Line(item, 3m, 10m) } }, ct);

        await using var scope = Scope(app, tenant);
        var results = new[]
        {
            await CallAsync(scope, RoleNames.Admin, "find_items", ct),
            await CallAsync(scope, RoleNames.Admin, "get_item", ct, ("sku", "RICE-1")),
            await CallAsync(scope, RoleNames.Admin, "list_locations", ct),
            await CallAsync(scope, RoleNames.Admin, "get_stock", ct),
            await CallAsync(scope, RoleNames.Admin, "get_item_history", ct, ("sku", "RICE-1")),
            await CallAsync(scope, RoleNames.Admin, "list_documents", ct),
            await CallAsync(scope, RoleNames.Admin, "get_document", ct, ("documentId", document.ToString())),
            await CallAsync(scope, RoleNames.Admin, "get_ledger_health", ct),
        };

        foreach (var result in results)
        {
            result["error"].ShouldBeNull();
            var json = result.ToJsonString();
            json.ShouldNotContain("postedBy", Case.Insensitive);
            json.ShouldNotContain("@");
            json.ShouldNotContain(userId.ToString(), Case.Insensitive);
        }
    }

    [Fact]
    public async Task Bad_arguments_are_tool_errors()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;

        await using var scope = Scope(app, tenant);
        var results = new[]
        {
            await CallAsync(scope, RoleNames.Admin, "list_documents", ct, ("type", "Sales")),
            await CallAsync(scope, RoleNames.Admin, "list_documents", ct, ("from", "20-11-2026")),
            await CallAsync(scope, RoleNames.Admin, "list_documents", ct, ("to", "tomorrow")),
            await CallAsync(scope, RoleNames.Admin, "get_document", ct, ("documentId", "abc")),
        };

        foreach (var result in results)
        {
            result["error"]!.GetValue<string>().ShouldBe("invalid-argument");
            result["message"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        }

        results[0]["message"]!.GetValue<string>().ShouldContain("Receipt");
    }

    [Fact]
    public void System_prompt_has_role_and_manila_date()
    {
        var prompt = SystemPrompt.Build("Staff", new DateTimeOffset(2026, 11, 19, 17, 0, 0, TimeSpan.Zero));

        prompt.ShouldContain("2026-11-20 (Friday)");
        prompt.ShouldContain("The user is Staff");
        prompt.ShouldNotContain("@");
        SystemPrompt.Build("Admin", DateTimeOffset.UnixEpoch).ShouldContain("The user is an Admin");
    }
}