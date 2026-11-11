using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>T5: tenants stay isolated across requests and scopes, with and without the EF tenant filter (spec §11).</summary>
public sealed class TenantIsolationTests(DatabaseFixture db)
{
    // T5 grows with every slice that adds a route (spec §11): each new /api route gets a [ProbesRoute] probe here
    // (a tenant B route id → 404, a tenant B body id → 400) or an exemption with its reason.
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["POST /api/auth/login"] = "anonymous; reads no tenant data",
        ["POST /api/auth/logout"] = "signs out; reads no tenant data",
    };

    private static readonly string[] SeededCodes = ["ADJ", "CUS", "SUP"];

    [Fact]
    [ProbesRoute("GET /api/auth/me")]
    public async Task Requests_alternate_tenants_in_one_host()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (a, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var clientA = app.NewClient();
        using var clientB = app.NewClient();
        (await Login(clientA, emailA)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Login(clientB, emailB)).StatusCode.ShouldBe(HttpStatusCode.OK);

        foreach (var (client, tenant) in new[] { (clientA, a), (clientB, b), (clientA, a) })
        {
            var response = await client.GetAsync("/api/auth/me", ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var me = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            me["tenantName"]!.GetValue<string>().ShouldBe(DatabaseFixture.TenantName(tenant));
        }
    }

    [Fact]
    public async Task Scopes_alternate_tenants_on_one_connection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = CreateSingleConnectionApp();
        var (a, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var scopes = app.Services.GetRequiredService<ITenantScopeFactory>();

        var spids = new List<short>();
        foreach (var tenant in new[] { a, b, a })
        {
            await using var scope = scopes.BeginScope(tenant);
            var inventory = scope.Services.GetRequiredService<InventoryDbContext>();

            spids.Add(await inventory.Database.SqlQuery<short>($"SELECT @@SPID AS [Value]").SingleAsync(ct));
            var locations = await inventory.Locations.ToListAsync(ct);

            locations.Count.ShouldBe(3);
            locations.ShouldAllBe(l => l.TenantId == tenant);
            locations.Select(l => l.Code).Order().ShouldBe(SeededCodes);
        }

        spids.Distinct().Count().ShouldBe(1, "the three scopes must share one physical connection");
    }

    [Fact]
    public async Task Rls_alone_isolates_when_the_tenant_filter_is_ignored()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (a, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(a);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
        var ignored = inventory.Locations.IgnoreQueryFilters([TenantSchema.FilterName]);

        // Only RLS may have filtered these rows: the EF predicate must be gone from the SQL.
        ignored.ToQueryString().ShouldNotContain("[TenantId] = @", Case.Insensitive);
        var locations = await ignored.ToListAsync(ct);

        locations.Count.ShouldBe(3);
        locations.ShouldAllBe(l => l.TenantId == a);
    }

    [Fact]
    public async Task Tenant_filter_is_in_the_generated_sql()
    {
        await using var app = db.CreateApp();
        var (a, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(a);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();

        inventory.Locations.ToQueryString().ShouldContain("[TenantId] = @", Case.Insensitive);
    }

    [Fact]
    public async Task Query_without_a_tenant_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        await using var scope = app.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        await Should.ThrowAsync<InvalidOperationException>(() => inventory.Locations.ToListAsync(ct));
    }

    [Fact]
    [ProbesRoute("GET /api/items/")]
    public async Task Each_tenant_lists_only_its_own_items()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (a, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        // The same SKU in both tenants proves the unique index is per tenant at the database.
        var idA = (await ItemsTests.AddItemsAsync(app, a, ("SAME-1", "Tenant A item", true)))[0];
        var idB = (await ItemsTests.AddItemsAsync(app, b, ("SAME-1", "Tenant B item", true)))[0];
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);

        foreach (var (client, expected) in new[] { (clientA, idA), (clientB, idB) })
        {
            var response = await client.GetAsync("/api/items", ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var list = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            list["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ShouldBe([expected]);
            list["total"]!.GetValue<int>().ShouldBe(1);
        }
    }

    [Fact]
    [ProbesRoute("GET /api/items/{id:guid}")]
    public async Task Another_tenants_item_id_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (a, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        var (b, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        var idB = (await ItemsTests.AddItemsAsync(app, b, ("B-ONLY", "Tenant B item", true)))[0];
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);

        var response = await clientA.GetAsync($"/api/items/{idB}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
    }

    [Fact]
    [ProbesRoute("POST /api/items/")]
    public async Task Skus_are_unique_per_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);

        var idA = (await ItemsTests.CreateAsync(clientA, "SAME-1", "Tenant A item", "pc", ct))["id"]!.GetValue<Guid>();
        var idB = (await ItemsTests.CreateAsync(clientB, "SAME-1", "Tenant B item", "pc", ct))["id"]!.GetValue<Guid>();

        var list = (await (await clientA.GetAsync("/api/items", ct)).Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var ids = list["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ToList();
        ids.ShouldBe([idA]);
        ids.ShouldNotContain(idB);
    }

    [Fact]
    [ProbesRoute("PUT /api/items/{id:guid}")]
    public async Task Another_tenants_item_cannot_be_updated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var itemB = await ItemsTests.CreateAsync(clientB, "B-ONLY", "Tenant B item", "pc", ct);
        var idB = itemB["id"]!.GetValue<Guid>();
        var versionB = itemB["version"]!.GetValue<string>();

        var response = await clientA.PutAsJsonAsync($"/api/items/{idB}", new { sku = "HIJACK", name = "Hijacked", baseUnit = "pc", version = versionB }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        var after = (await (await clientB.GetAsync($"/api/items/{idB}", ct)).Content.ReadFromJsonAsync<JsonNode>(ct))!;
        after["sku"]!.GetValue<string>().ShouldBe("B-ONLY");
        after["name"]!.GetValue<string>().ShouldBe("Tenant B item");
        after["version"]!.GetValue<string>().ShouldBe(versionB);
    }

    [Fact]
    [ProbesRoute("POST /api/items/{id:guid}/deactivate")]
    public async Task Another_tenants_item_cannot_be_deactivated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var idB = (await ItemsTests.CreateAsync(clientB, "B-ONLY", "Tenant B item", "pc", ct))["id"]!.GetValue<Guid>();

        var response = await clientA.PostAsync($"/api/items/{idB}/deactivate", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        var after = (await (await clientB.GetAsync($"/api/items/{idB}", ct)).Content.ReadFromJsonAsync<JsonNode>(ct))!;
        after["isActive"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    [ProbesRoute("GET /api/locations/")]
    public async Task Each_tenant_lists_only_its_own_locations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        // The same code in both tenants proves the unique index is per tenant at the database.
        var idA = (await LocationsTests.CreateAsync(clientA, "SAME-1", "Tenant A location", ct))["id"]!.GetValue<Guid>();
        var idB = (await LocationsTests.CreateAsync(clientB, "SAME-1", "Tenant B location", ct))["id"]!.GetValue<Guid>();

        var listA = await ListLocationIdsAsync(clientA, ct);
        var listB = await ListLocationIdsAsync(clientB, ct);

        // Its own physical location plus its 3 system locations, and nothing from the other tenant.
        listA.Count.ShouldBe(4);
        listA.ShouldContain(idA);
        listA.ShouldNotContain(idB);
        listB.Count.ShouldBe(4);
        listB.ShouldContain(idB);
        listB.ShouldNotContain(idA);
        listA.Intersect(listB).ShouldBeEmpty();
    }

    [Fact]
    [ProbesRoute("POST /api/locations/")]
    public async Task Location_codes_are_unique_per_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);

        var responseA = await clientA.PostAsJsonAsync("/api/locations", new { code = "SAME-1", name = "Tenant A location" }, ct);
        var responseB = await clientB.PostAsJsonAsync("/api/locations", new { code = "SAME-1", name = "Tenant B location" }, ct);

        responseA.StatusCode.ShouldBe(HttpStatusCode.Created);
        responseB.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    [ProbesRoute("PUT /api/locations/{id:guid}")]
    public async Task Another_tenants_location_cannot_be_updated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = await LocationsTests.CreateAsync(clientB, "B-ONLY", "Tenant B location", ct);
        var idB = locationB["id"]!.GetValue<Guid>();
        var versionB = locationB["version"]!.GetValue<string>();

        var response = await clientA.PutAsJsonAsync($"/api/locations/{idB}", new { code = "HIJACK", name = "Hijacked", version = versionB }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        var after = await GetLocationAsync(clientB, idB, ct);
        after["code"]!.GetValue<string>().ShouldBe("B-ONLY");
        after["name"]!.GetValue<string>().ShouldBe("Tenant B location");
        after["version"]!.GetValue<string>().ShouldBe(versionB);
    }

    [Fact]
    [ProbesRoute("POST /api/locations/{id:guid}/deactivate")]
    public async Task Another_tenants_location_cannot_be_deactivated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = await LocationsTests.CreateAsync(clientB, "B-ONLY", "Tenant B location", ct);
        var idB = locationB["id"]!.GetValue<Guid>();

        var response = await clientA.PostAsync($"/api/locations/{idB}/deactivate", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        var after = await GetLocationAsync(clientB, idB, ct);
        after["isActive"]!.GetValue<bool>().ShouldBeTrue();
        after["version"]!.GetValue<string>().ShouldBe(locationB["version"]!.GetValue<string>());
    }

    [Fact]
    [ProbesRoute("POST /api/documents/receipts")]
    public async Task Another_tenants_ids_in_a_receipt_body_are_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = (await LocationsTests.CreateAsync(clientB, "B-ONLY", "Tenant B location", ct))["id"]!.GetValue<Guid>();
        var itemB = (await ItemsTests.CreateAsync(clientB, "B-ONLY", "Tenant B item", "pc", ct))["id"]!.GetValue<Guid>();
        var locationA = (await LocationsTests.CreateAsync(clientA, "A-ONLY", "Tenant A location", ct))["id"]!.GetValue<Guid>();
        var itemA = (await ItemsTests.CreateAsync(clientA, "A-ONLY", "Tenant A item", "pc", ct))["id"]!.GetValue<Guid>();

        var intoB = await PostingTests.PostDocumentAsync(clientA, PostingTests.Receipts, Guid.NewGuid().ToString("N"),
            PostingTests.Receipt(locationB, PostingTests.Line(itemA, 1m, 1m)), ct);
        var ofB = await PostingTests.PostDocumentAsync(clientA, PostingTests.Receipts, Guid.NewGuid().ToString("N"),
            PostingTests.Receipt(locationA, PostingTests.Line(itemB, 1m, 1m)), ct);

        foreach (var (response, key) in new[] { (intoB, "locationId"), (ofB, "lines[0].itemId") })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            problem["type"]!.GetValue<string>().ShouldBe("/problems/validation");
            problem["errors"]!.AsObject().Select(e => e.Key).ShouldBe([key]);
        }

        await using var connection = await LedgerRows.OpenAsync(db, b, ct);
        await using var count = new SqlCommand("SELECT COUNT(*) FROM inventory.StockMovements WHERE ItemId = @item", connection);
        count.Parameters.Add(new SqlParameter("@item", SqlDbType.UniqueIdentifier) { Value = itemB });
        ((int)(await count.ExecuteScalarAsync(ct))!).ShouldBe(0);
    }

    [Fact]
    [ProbesRoute("POST /api/documents/issues")]
    public async Task Another_tenants_ids_in_an_issue_body_are_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = (await LocationsTests.CreateAsync(clientB, "B-ONLY", "Tenant B location", ct))["id"]!.GetValue<Guid>();
        var itemB = (await ItemsTests.CreateAsync(clientB, "B-ONLY", "Tenant B item", "pc", ct))["id"]!.GetValue<Guid>();
        var locationA = (await LocationsTests.CreateAsync(clientA, "A-ONLY", "Tenant A location", ct))["id"]!.GetValue<Guid>();
        var itemA = (await ItemsTests.CreateAsync(clientA, "A-ONLY", "Tenant A item", "pc", ct))["id"]!.GetValue<Guid>();

        // B's balance exists, so an issue that reached the engine would change it.
        (await PostingTests.PostDocumentAsync(clientB, PostingTests.Receipts, Guid.NewGuid().ToString("N"),
            PostingTests.Receipt(locationB, PostingTests.Line(itemB, 5m, 10m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var connection = await LedgerRows.OpenAsync(db, b, ct);
        await using var count = new SqlCommand("SELECT COUNT(*) FROM inventory.StockMovements", connection);
        var balanceBefore = await PostingTests.BalanceAsync(connection, itemB, locationB, ct);
        var movementsBefore = (int)(await count.ExecuteScalarAsync(ct))!;

        var fromB = await PostingTests.PostDocumentAsync(clientA, PostingTests.Issues, Guid.NewGuid().ToString("N"),
            PostingTests.Issue(locationB, PostingTests.IssueLine(itemA, 1m)), ct);
        var ofB = await PostingTests.PostDocumentAsync(clientA, PostingTests.Issues, Guid.NewGuid().ToString("N"),
            PostingTests.Issue(locationA, PostingTests.IssueLine(itemB, 1m)), ct);

        foreach (var (response, key) in new[] { (fromB, "locationId"), (ofB, "lines[0].itemId") })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            problem["type"]!.GetValue<string>().ShouldBe("/problems/validation");
            problem["errors"]!.AsObject().Select(e => e.Key).ShouldBe([key]);
        }

        balanceBefore.ShouldBe((5m, 50m));
        (await PostingTests.BalanceAsync(connection, itemB, locationB, ct)).ShouldBe(balanceBefore);
        ((int)(await count.ExecuteScalarAsync(ct))!).ShouldBe(movementsBefore);
    }

    [Fact]
    public void Every_api_route_is_probed_or_exempt()
    {
        using var app = db.CreateApp();

        var routes = app.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["(any)"])
                .Select(method => $"{method} {e.RoutePattern.RawText}"))
            .ToHashSet();

        // A route counts as probed only through a [ProbesRoute] on a real test in this class, never by a name in a list.
        var probed = typeof(TenantIsolationTests).GetMethods()
            .Where(m => m.IsDefined(typeof(FactAttribute), inherit: false))
            .SelectMany(m => m.GetCustomAttributes(typeof(ProbesRouteAttribute), inherit: false).Cast<ProbesRouteAttribute>())
            .Select(p => p.Route);
        var expected = probed.Concat(Exempt.Keys).ToHashSet();

        var unlisted = routes.Except(expected).Order().ToList();
        var stale = expected.Except(routes).Order().ToList();
        (unlisted.Count + stale.Count).ShouldBe(0,
            $"Routes without a T5 probe or exemption: [{string.Join(", ", unlisted)}]. Listed but not mapped: [{string.Join(", ", stale)}]");
    }

    private static async Task<List<Guid>> ListLocationIdsAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/locations", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        return list["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ToList();
    }

    private static async Task<JsonNode> GetLocationAsync(HttpClient client, Guid id, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/locations", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        return list["items"]!.AsArray().Single(i => i!["id"]!.GetValue<Guid>() == id)!;
    }

    private ApiFactory CreateSingleConnectionApp() => new(
        new SqlConnectionStringBuilder(db.AppConnectionString)
        {
            MaxPoolSize = 1,
            ApplicationName = "t5-" + Guid.NewGuid().ToString("N")[..8],
        }.ConnectionString);

    private static Task<HttpResponseMessage> Login(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password = DatabaseFixture.UserPassword }, TestContext.Current.CancellationToken);
}

/// <summary>Marks a T5 test as the probe for one <c>"METHOD /route"</c>; the route guard reads these.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProbesRouteAttribute(string route) : Attribute
{
    public string Route { get; } = route;
}