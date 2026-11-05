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