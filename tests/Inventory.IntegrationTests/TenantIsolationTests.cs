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
        ["GET /api/assistant/status"] = "reads configuration only; no tenant data",
    };

    private static readonly string[] SeededCodes = ["ADJ", "CUS", "SUP"];

    private const string Transfers = "/api/documents/transfers";
    private const string Adjustments = "/api/documents/adjustments";

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
    [ProbesRoute("GET /api/stock/")]
    public async Task Each_tenant_lists_only_its_own_stock()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationA = await PostingTests.NewLocationAsync(clientA, ct);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct);
        var itemA = await PostingTests.NewItemAsync(clientA, "SAME-1", ct);
        var itemB = await PostingTests.NewItemAsync(clientB, "SAME-1", ct);
        await PostingTests.ReceiveAsync(clientA, locationA, itemA, 5m, 10m, ct);
        await PostingTests.ReceiveAsync(clientB, locationB, itemB, 5m, 10m, ct);

        foreach (var (client, expected) in new[] { (clientA, itemA), (clientB, itemB) })
        {
            var response = await client.GetAsync("/api/stock", ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var list = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            list["items"]!.AsArray().Select(i => i!["itemId"]!.GetValue<Guid>()).ShouldBe([expected]);
            list["total"]!.GetValue<int>().ShouldBe(1);
        }

        foreach (var query in new[] { $"?locationId={locationB}", $"?itemId={itemB}" })
        {
            var response = await clientA.GetAsync("/api/stock" + query, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var list = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            list["items"]!.AsArray().ShouldBeEmpty();
            list["total"]!.GetValue<int>().ShouldBe(0);
            list["totals"]!["stockValue"]!.GetValue<decimal>().ShouldBe(0m);
        }
    }

    [Fact]
    [ProbesRoute("GET /api/ledger/health")]
    public async Task Each_tenant_sees_only_its_own_ledger_health()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationA = await PostingTests.NewLocationAsync(clientA, ct);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct);
        var itemA = await PostingTests.NewItemAsync(clientA, "SAME-1", ct);
        var itemB = await PostingTests.NewItemAsync(clientB, "SAME-1", ct);
        await PostingTests.ReceiveAsync(clientA, locationA, itemA, 5m, 10m, ct);
        await PostingTests.ReceiveAsync(clientB, locationB, itemB, 5m, 10m, ct);

        // Drift tenant B's physical balance only; the CHECKs still hold.
        await using (var connection = await LedgerRows.OpenAsync(db, b, ct))
        await using (var drift = new SqlCommand("""
            UPDATE inventory.StockBalances SET OnHand = OnHand + 1, StockValue = StockValue + 10
            WHERE TenantId = @t AND ItemId = @i AND LocationId = @l
            """, connection))
        {
            drift.Parameters.Add(new SqlParameter("@t", SqlDbType.UniqueIdentifier) { Value = b });
            drift.Parameters.Add(new SqlParameter("@i", SqlDbType.UniqueIdentifier) { Value = itemB });
            drift.Parameters.Add(new SqlParameter("@l", SqlDbType.UniqueIdentifier) { Value = locationB });
            (await drift.ExecuteNonQueryAsync(ct)).ShouldBe(1);
        }

        var responseA = await clientA.GetAsync("/api/ledger/health", ct);

        responseA.StatusCode.ShouldBe(HttpStatusCode.OK);
        var healthA = (await responseA.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        healthA["healthy"]!.GetValue<bool>().ShouldBeTrue();
        var failuresA = healthA["invariants"]!.AsArray().SelectMany(i => i!["failures"]!.AsArray()).ToList();
        var idsA = failuresA
            .SelectMany(f => new[] { f!["itemId"], f["locationId"] })
            .OfType<JsonNode>()
            .Select(n => n.GetValue<Guid>())
            .ToList();
        idsA.ShouldNotContain(itemB);
        idsA.ShouldNotContain(locationB);
        failuresA.ShouldBeEmpty();

        var responseB = await clientB.GetAsync("/api/ledger/health", ct);

        responseB.StatusCode.ShouldBe(HttpStatusCode.OK);
        var healthB = (await responseB.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        healthB["healthy"]!.GetValue<bool>().ShouldBeFalse();
        var projectionB = healthB["invariants"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == "INV-1")!;
        projectionB["failures"]!.AsArray()
            .Where(f => f!["itemId"]!.GetValue<Guid>() == itemB && f["locationId"]!.GetValue<Guid>() == locationB)
            .Select(f => f!["check"]!.GetValue<string>())
            .ShouldBe(["OnHand", "StockValue"]);
    }

    [Fact]
    [ProbesRoute("POST /api/ledger/rebuild")]
    public async Task Each_tenant_rebuilds_only_its_own_ledger()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (a, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationA = await PostingTests.NewLocationAsync(clientA, ct);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct);
        var itemA = await PostingTests.NewItemAsync(clientA, "SAME-1", ct);
        var itemB = await PostingTests.NewItemAsync(clientB, "SAME-1", ct);
        await PostingTests.ReceiveAsync(clientA, locationA, itemA, 5m, 10m, ct);
        await PostingTests.ReceiveAsync(clientB, locationB, itemB, 5m, 10m, ct);
        await using var connectionA = await LedgerRows.OpenAsync(db, a, ct);
        await using var connectionB = await LedgerRows.OpenAsync(db, b, ct);
        var versionsA = await LedgerRebuildTests.RowVersionsAsync(connectionA, ct);

        // Drift tenant B's physical balance only; the CHECKs still hold.
        await LedgerRebuildTests.UpdateBalanceAsync(
            connectionB, "OnHand = OnHand + 1, StockValue = StockValue + 10", b, itemB, locationB, ct);

        var rebuildA = await LedgerRebuildTests.RebuildAsync(clientA, ct);

        rebuildA["differences"]!.AsArray().ShouldBeEmpty();
        (await PostingTests.BalanceAsync(connectionB, itemB, locationB, ct)).ShouldBe((6m, 60m));

        var rebuildB = await LedgerRebuildTests.RebuildAsync(clientB, ct);

        var difference = rebuildB["differences"]!.AsArray().ShouldHaveSingleItem()!;
        difference["itemId"]!.GetValue<Guid>().ShouldBe(itemB);
        difference["locationId"]!.GetValue<Guid>().ShouldBe(locationB);
        (await PostingTests.BalanceAsync(connectionB, itemB, locationB, ct)).ShouldBe((5m, 50m));
        await LedgerRebuildTests.AssertHealthyAsync(clientB, ct);
        (await LedgerRebuildTests.RowVersionsAsync(connectionA, ct)).ShouldBe(versionsA);
    }

    [Fact]
    [ProbesRoute("GET /api/documents/")]
    public async Task Each_tenant_lists_only_its_own_documents()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var documents = new List<(HttpClient Client, Guid Id)>();
        foreach (var client in new[] { clientA, clientB })
        {
            var location = await PostingTests.NewLocationAsync(client, ct);
            var item = await PostingTests.NewItemAsync(client, "SAME-1", ct);
            var posted = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, PostingTests.NewKey(),
                PostingTests.Receipt(location, PostingTests.Line(item, 5m, 10m)), ct);
            posted.StatusCode.ShouldBe(HttpStatusCode.Created);
            documents.Add((client, (await posted.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>()));
        }

        foreach (var (client, expected) in documents)
        {
            var response = await client.GetAsync("/api/documents", ct);

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
    [ProbesRoute("GET /api/items/{id:guid}/movements")]
    public async Task Another_tenants_item_history_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct);
        var itemB = await PostingTests.NewItemAsync(clientB, "SAME-1", ct);
        await PostingTests.ReceiveAsync(clientB, locationB, itemB, 1m, 10m, ct);

        var response = await clientA.GetAsync($"/api/items/{itemB}/movements", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");

        var locationA = await PostingTests.NewLocationAsync(clientA, ct);
        var itemA = await PostingTests.NewItemAsync(clientA, "SAME-1", ct);
        await PostingTests.ReceiveAsync(clientA, locationA, itemA, 2m, 10m, ct);

        var own = await clientA.GetAsync($"/api/items/{itemA}/movements", ct);

        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        var history = (await own.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        history["total"]!.GetValue<int>().ShouldBe(1);
        history["items"]!.AsArray()[0]!["locationId"]!.GetValue<Guid>().ShouldBe(locationA);
    }

    [Fact]
    [ProbesRoute("GET /api/documents/{id:guid}")]
    public async Task Another_tenants_document_id_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct);
        var itemB = await PostingTests.NewItemAsync(clientB, "B-ONLY", ct);
        var posted = await PostingTests.PostDocumentAsync(clientB, PostingTests.Receipts, PostingTests.NewKey(),
            PostingTests.Receipt(locationB, PostingTests.Line(itemB, 1m, 10m)), ct);
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var idB = (await posted.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();

        var response = await clientA.GetAsync($"/api/documents/{idB}", ct);

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
    [ProbesRoute("POST /api/items/{id:guid}/units")]
    public async Task Another_tenants_item_cannot_get_a_unit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var idB = (await ItemsTests.CreateAsync(clientB, "B-ONLY", "Tenant B item", "pc", ct))["id"]!.GetValue<Guid>();

        var response = await clientA.PostAsJsonAsync($"/api/items/{idB}/units", new { code = "case", factor = 12 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        var after = (await (await clientB.GetAsync($"/api/items/{idB}", ct)).Content.ReadFromJsonAsync<JsonNode>(ct))!;
        after["units"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    [ProbesRoute("POST /api/items/{id:guid}/units/{code}/deactivate")]
    public async Task Another_tenants_item_unit_cannot_be_deactivated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var idB = (await ItemsTests.CreateAsync(clientB, "B-ONLY", "Tenant B item", "pc", ct))["id"]!.GetValue<Guid>();
        (await clientB.PostAsJsonAsync($"/api/items/{idB}/units", new { code = "case", factor = 12 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await clientA.PostAsync($"/api/items/{idB}/units/case/deactivate", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        var after = (await (await clientB.GetAsync($"/api/items/{idB}", ct)).Content.ReadFromJsonAsync<JsonNode>(ct))!;
        after["units"]![0]!["isActive"]!.GetValue<bool>().ShouldBeTrue();
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
    [ProbesRoute("POST /api/documents/transfers")]
    public async Task Another_tenants_ids_in_a_transfer_body_are_400()
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
        var locationA2 = (await LocationsTests.CreateAsync(clientA, "A-TWO", "Tenant A second location", ct))["id"]!.GetValue<Guid>();
        var itemA = (await ItemsTests.CreateAsync(clientA, "A-ONLY", "Tenant A item", "pc", ct))["id"]!.GetValue<Guid>();

        // Both tenants hold stock, so a transfer that wasn't stopped at step 1 could succeed and change B's ledger.
        (await PostingTests.PostDocumentAsync(clientB, PostingTests.Receipts, Guid.NewGuid().ToString("N"),
            PostingTests.Receipt(locationB, PostingTests.Line(itemB, 5m, 10m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostingTests.PostDocumentAsync(clientA, PostingTests.Receipts, Guid.NewGuid().ToString("N"),
            PostingTests.Receipt(locationA, PostingTests.Line(itemA, 5m, 10m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var connection = await LedgerRows.OpenAsync(db, b, ct);
        await using var count = new SqlCommand("SELECT COUNT(*) FROM inventory.StockMovements", connection);
        var balanceBefore = await PostingTests.BalanceAsync(connection, itemB, locationB, ct);
        var movementsBefore = (int)(await count.ExecuteScalarAsync(ct))!;

        var fromB = await PostingTests.PostDocumentAsync(clientA, Transfers, Guid.NewGuid().ToString("N"), new
        {
            fromLocationId = locationB,
            toLocationId = locationA,
            reference = (string?)null,
            notes = (string?)null,
            lines = new[] { new { itemId = itemA, quantity = 1m } },
        }, ct);
        var toB = await PostingTests.PostDocumentAsync(clientA, Transfers, Guid.NewGuid().ToString("N"), new
        {
            fromLocationId = locationA,
            toLocationId = locationB,
            reference = (string?)null,
            notes = (string?)null,
            lines = new[] { new { itemId = itemA, quantity = 1m } },
        }, ct);
        var ofB = await PostingTests.PostDocumentAsync(clientA, Transfers, Guid.NewGuid().ToString("N"), new
        {
            fromLocationId = locationA,
            toLocationId = locationA2,
            reference = (string?)null,
            notes = (string?)null,
            lines = new[] { new { itemId = itemB, quantity = 1m } },
        }, ct);

        foreach (var (response, key) in new[] { (fromB, "fromLocationId"), (toB, "toLocationId"), (ofB, "lines[0].itemId") })
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
    [ProbesRoute("POST /api/documents/adjustments")]
    public async Task Another_tenants_ids_in_an_adjustment_body_are_400()
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

        // B's balance exists, so an adjustment that reached the engine would change it.
        (await PostingTests.PostDocumentAsync(clientB, PostingTests.Receipts, Guid.NewGuid().ToString("N"),
            PostingTests.Receipt(locationB, PostingTests.Line(itemB, 5m, 10m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var connection = await LedgerRows.OpenAsync(db, b, ct);
        await using var count = new SqlCommand("SELECT COUNT(*) FROM inventory.StockMovements", connection);
        var balanceBefore = await PostingTests.BalanceAsync(connection, itemB, locationB, ct);
        var movementsBefore = (int)(await count.ExecuteScalarAsync(ct))!;

        var atB = await PostingTests.PostDocumentAsync(clientA, Adjustments, Guid.NewGuid().ToString("N"), new
        {
            locationId = locationB,
            reasonCode = "Damaged",
            reference = (string?)null,
            notes = (string?)null,
            lines = new[] { new { itemId = itemA, quantityDelta = -1m, unitCost = (decimal?)null } },
        }, ct);
        var ofB = await PostingTests.PostDocumentAsync(clientA, Adjustments, Guid.NewGuid().ToString("N"), new
        {
            locationId = locationA,
            reasonCode = "Damaged",
            reference = (string?)null,
            notes = (string?)null,
            lines = new[] { new { itemId = itemB, quantityDelta = 1m, unitCost = (decimal?)1m } },
        }, ct);

        foreach (var (response, key) in new[] { (atB, "locationId"), (ofB, "lines[0].itemId") })
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
    [ProbesRoute("POST /api/documents/{id:guid}/reversal")]
    public async Task Another_tenants_document_cannot_be_reversed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = (await LocationsTests.CreateAsync(clientB, "B-ONLY", "Tenant B location", ct))["id"]!.GetValue<Guid>();
        var itemB = (await ItemsTests.CreateAsync(clientB, "B-ONLY", "Tenant B item", "pc", ct))["id"]!.GetValue<Guid>();
        var posted = await PostingTests.PostDocumentAsync(clientB, PostingTests.Receipts, PostingTests.NewKey(),
            PostingTests.Receipt(locationB, PostingTests.Line(itemB, 5m, 10m)), ct);
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var idB = (await posted.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/documents/{idB}/reversal");
        request.Headers.Add("Idempotency-Key", PostingTests.NewKey());
        var response = await clientA.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        await using var connection = await LedgerRows.OpenAsync(db, b, ct);
        (await PostingTests.BalanceAsync(connection, itemB, locationB, ct)).ShouldBe((5m, 50m));
        await using var reversals = new SqlCommand("SELECT COUNT(*) FROM inventory.StockDocuments WHERE Type = 'Reversal'", connection);
        ((int)(await reversals.ExecuteScalarAsync(ct))!).ShouldBe(0);
    }

    [Fact]
    [ProbesRoute("GET /api/stocktakes/")]
    public async Task Each_tenant_lists_only_its_own_stocktakes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var stocktakes = new List<(HttpClient Client, Guid Id)>();
        foreach (var client in new[] { clientA, clientB })
        {
            var location = await PostingTests.NewLocationAsync(client, ct, "SAME-1");
            stocktakes.Add((client, (await StocktakeTests.StartAsync(client, location, ct))["id"]!.GetValue<Guid>()));
        }

        foreach (var (client, expected) in stocktakes)
        {
            var response = await client.GetAsync(StocktakeTests.Stocktakes, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var list = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            list["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ShouldBe([expected]);
            list["total"]!.GetValue<int>().ShouldBe(1);
        }
    }

    [Fact]
    [ProbesRoute("POST /api/stocktakes/")]
    public async Task Another_tenants_location_cannot_start_a_stocktake()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct, "B-ONLY");

        var response = await clientA.PostAsJsonAsync(StocktakeTests.Stocktakes, new { locationId = locationB }, ct);

        await PostingTests.AssertValidationAsync(response, "locationId", ct);
        await using var connection = await LedgerRows.OpenAsync(db, b, ct);
        await using var count = new SqlCommand("SELECT COUNT(*) FROM inventory.Stocktakes", connection);
        ((int)(await count.ExecuteScalarAsync(ct))!).ShouldBe(0);
    }

    [Fact]
    [ProbesRoute("GET /api/stocktakes/{id:guid}")]
    public async Task Another_tenants_stocktake_id_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct, "B-ONLY");
        var idB = (await StocktakeTests.StartAsync(clientB, locationB, ct))["id"]!.GetValue<Guid>();

        var response = await clientA.GetAsync($"{StocktakeTests.Stocktakes}/{idB}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
    }

    [Fact]
    [ProbesRoute("PUT /api/stocktakes/{id:guid}/lines/{itemId:guid}")]
    public async Task Another_tenants_stocktake_or_item_id_cannot_be_counted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationA = await PostingTests.NewLocationAsync(clientA, ct, "A-ONLY");
        var locationB = await PostingTests.NewLocationAsync(clientB, ct, "B-ONLY");
        var itemA = await PostingTests.NewItemAsync(clientA, "A-ITEM", ct);
        var itemB = await PostingTests.NewItemAsync(clientB, "B-ITEM", ct);
        var idA = (await StocktakeTests.StartAsync(clientA, locationA, ct))["id"]!.GetValue<Guid>();
        var idB = (await StocktakeTests.StartAsync(clientB, locationB, ct))["id"]!.GetValue<Guid>();

        var foreignStocktake = await StocktakeTests.CountAsync(clientA, idB, itemA, new { quantity = 1 }, ct);
        var foreignItem = await StocktakeTests.CountAsync(clientA, idA, itemB, new { quantity = 1 }, ct);

        foreach (var response in new[] { foreignStocktake, foreignItem })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        }

        (await clientB.GetFromJsonAsync<JsonNode>($"{StocktakeTests.Stocktakes}/{idB}", ct))!["lines"]!.AsArray().ShouldBeEmpty();
        (await clientA.GetFromJsonAsync<JsonNode>($"{StocktakeTests.Stocktakes}/{idA}", ct))!["lines"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    [ProbesRoute("POST /api/stocktakes/{id:guid}/cancel")]
    public async Task Another_tenants_stocktake_id_cannot_be_cancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct, "B-ONLY");
        var idB = (await StocktakeTests.StartAsync(clientB, locationB, ct))["id"]!.GetValue<Guid>();

        var response = await clientA.PostAsync($"{StocktakeTests.Stocktakes}/{idB}/cancel", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        (await clientB.GetFromJsonAsync<JsonNode>($"{StocktakeTests.Stocktakes}/{idB}", ct))!["status"]!.GetValue<string>().ShouldBe("Draft");
    }

    [Fact]
    [ProbesRoute("POST /api/stocktakes/{id:guid}/post")]
    public async Task Another_tenants_stocktake_id_cannot_be_posted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct, "B-ONLY");
        var itemB = await PostingTests.NewItemAsync(clientB, "B-ITEM", ct);
        await PostingTests.ReceiveAsync(clientB, locationB, itemB, 5m, 10m, ct);
        var idB = (await StocktakeTests.StartAsync(clientB, locationB, ct))["id"]!.GetValue<Guid>();
        await StocktakeTests.CountOkAsync(clientB, idB, itemB, new { quantity = 3 }, ct);

        var response = await StocktakeTests.PostAsync(clientA, idB, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        (await clientB.GetFromJsonAsync<JsonNode>($"{StocktakeTests.Stocktakes}/{idB}", ct))!["status"]!.GetValue<string>().ShouldBe("Draft");
        await using var connection = await LedgerRows.OpenAsync(db, b, ct);
        (await PostingTests.BalanceAsync(connection, itemB, locationB, ct)).ShouldBe((5m, 50.0000m));
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

    [Fact]
    [ProbesRoute("POST /api/assistant/chat")]
    public async Task Assistant_tools_read_only_the_callers_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var bOnly = "B-ONLY-" + Guid.NewGuid().ToString("N")[..8];
        // A chat body carries no ids, so tenant B's ids can only reach the tools through the model: this script does that.
        // B's document id is only known after B posts, so the script is bound to the app late.
        var late = new LateBoundChatClient();
        await using var app = AssistantTests.CreateApp(db, late);
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var locationA = await PostingTests.NewLocationAsync(clientA, ct);
        var locationB = await PostingTests.NewLocationAsync(clientB, ct);
        var sameA = await PostingTests.NewItemAsync(clientA, "SAME-1", ct);
        var sameB = await PostingTests.NewItemAsync(clientB, "SAME-1", ct);
        var onlyB = (await ItemsTests.CreateAsync(clientB, bOnly, "B secret item", "pc", ct))["id"]!.GetValue<Guid>();
        await PostingTests.ReceiveAsync(clientA, locationA, sameA, 5m, 10m, ct);
        var posted = await PostingTests.PostDocumentAsync(clientB, PostingTests.Receipts, PostingTests.NewKey(),
            new { locationId = locationB, reference = "B-SECRET-REF", notes = (string?)null, lines = new[] { PostingTests.Line(sameB, 9m, 10m), PostingTests.Line(onlyB, 1m, 10m) } }, ct);
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var documentB = (await posted.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        var model = new ScriptedChatClient(
            new ScriptedChatClient.CallTools(
                new ScriptedChatClient.ToolCall("get_stock", new { sku = bOnly }),
                new ScriptedChatClient.ToolCall("get_item", new { sku = bOnly }),
                new ScriptedChatClient.ToolCall("get_document", new { documentId = documentB.ToString() }),
                new ScriptedChatClient.ToolCall("get_item_history", new { sku = bOnly }),
                new ScriptedChatClient.ToolCall("get_stock", new { sku = "SAME-1" })),
            new ScriptedChatClient.Reply("done"));
        late.Inner = model;

        var response = await AssistantTests.AskAsync(clientA, "Look everything up.", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        model.Turns.Count.ShouldBe(2);
        var results = AssistantTests.ToolResults(model.Turns[1]);
        results.Count.ShouldBe(5);
        foreach (var result in results.Take(4))
        {
            result!["error"]!.GetValue<string>().ShouldBe("not-found");
        }

        // A's own SAME-1 only: 5 on hand, never B's 9.
        var own = results[4]!["rows"]!.AsArray().ShouldHaveSingleItem()!;
        own["sku"]!.GetValue<string>().ShouldBe("SAME-1");
        own["onHand"]!.GetValue<decimal>().ShouldBe(5m);
        results[4]!["total"]!.GetValue<int>().ShouldBe(1);
        var everything = string.Concat(model.ToolResultsJson());
        everything.ShouldNotContain("B secret item");
        everything.ShouldNotContain("B-SECRET-REF");
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password = DatabaseFixture.UserPassword }, TestContext.Current.CancellationToken);
}

/// <summary>Marks a T5 test as the probe for one <c>"METHOD /route"</c>; the route guard reads these.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProbesRouteAttribute(string route) : Attribute
{
    public string Route { get; } = route;
}