using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;

using Inventory.Contracts;

using Kernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// <see cref="IInventoryReader"/> returns what the HTTP read routes return for the caller's tenant, sees nothing of
/// another tenant, and exposes no user identity (plan D10).
/// </summary>
public sealed class InventoryReaderTests(DatabaseFixture db)
{
    private static async Task<JsonNode> GetAsync(HttpClient client, string url, CancellationToken ct)
    {
        var response = await client.GetAsync(url, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static decimal? Dec(JsonNode? node) => node is null ? null : node.GetValue<decimal>();

    private static async Task<Guid> PostAsync(HttpClient client, string route, object body, CancellationToken ct)
    {
        var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
    }

    private static async Task<(Guid Tenant, HttpClient Client)> StartAsync(ApiFactory app, string role = Roles.Admin)
    {
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, role);
        return (tenant, await DatabaseFixture.SignInAsync(app, email));
    }

    private static ITenantScope Scope(ApiFactory app, Guid tenant) =>
        app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);

    private static IInventoryReader Reader(ITenantScope scope) => scope.Services.GetRequiredService<IInventoryReader>();

    private static void ShouldMatchStockRow(StockRowSnapshot row, JsonNode json)
    {
        row.Sku.ShouldBe(json["sku"]!.GetValue<string>());
        row.LocationCode.ShouldBe(json["locationCode"]!.GetValue<string>());
        row.OnHand.ShouldBe(Dec(json["onHand"])!.Value);
        row.StockValue.ShouldBe(Dec(json["stockValue"])!.Value);
        row.AverageCost.ShouldBe(Dec(json["averageCost"]));
        row.LastUnitCost.ShouldBe(Dec(json["lastUnitCost"]));
    }

    private static void ShouldMatchStock(StockSnapshot stock, JsonNode json)
    {
        var rows = json["items"]!.AsArray();
        stock.Rows.Count.ShouldBe(rows.Count);
        stock.Total.ShouldBe(json["total"]!.GetValue<int>());
        stock.TotalStockValue.ShouldBe(Dec(json["totals"]!["stockValue"])!.Value);
        for (var i = 0; i < rows.Count; i++)
        {
            ShouldMatchStockRow(stock.Rows[i], rows[i]!);
        }
    }

    [Fact]
    public async Task Stock_matches_the_stock_route()
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
        await PostingTests.ReceiveAsync(client, b, x, 3m, 90m, ct);
        await PostingTests.ReceiveAsync(client, a, y, 7m, 12.5m, ct);
        await PostingTests.ReceiveAsync(client, b, y, 2m, 14m, ct);
        await PostAsync(client, PostingTests.Issues, PostingTests.Issue(a, PostingTests.IssueLine(x, 4m)), ct);

        await using var scope = Scope(app, tenant);
        var reader = Reader(scope);

        ShouldMatchStock(await reader.GetStockAsync(null, null, 100, ct), await GetAsync(client, "/api/stock?pageSize=100", ct));
        ShouldMatchStock(await reader.GetStockAsync(x, null, 100, ct), await GetAsync(client, $"/api/stock?pageSize=100&itemId={x}", ct));
        ShouldMatchStock(await reader.GetStockAsync(null, b, 100, ct), await GetAsync(client, $"/api/stock?pageSize=100&locationId={b}", ct));
        (await reader.GetStockAsync(null, null, 100, ct)).Rows.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Item_and_history_match_their_routes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "Rice-Mixed", ct);
        await PostingTests.ReceiveAsync(client, a, item, 10m, 100m, ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 120m, ct);
        await PostAsync(client, PostingTests.Issues, PostingTests.Issue(a, PostingTests.IssueLine(item, 3m)), ct);
        await PostAsync(client, TransferTests.Transfers, TransferTests.Transfer(a, b, TransferTests.TransferLine(item, 4m)), ct);

        await using var scope = Scope(app, tenant);
        var reader = Reader(scope);

        var route = await GetAsync(client, $"/api/items/{item}", ct);
        var snapshot = (await reader.GetItemAsync(item, ct))!;
        snapshot.Sku.ShouldBe(route["sku"]!.GetValue<string>());
        snapshot.Name.ShouldBe(route["name"]!.GetValue<string>());
        snapshot.BaseUnit.ShouldBe(route["baseUnit"]!.GetValue<string>());
        var balances = route["balances"]!.AsArray();
        snapshot.Balances.Count.ShouldBe(balances.Count);
        snapshot.Balances.Count.ShouldBe(2);
        for (var i = 0; i < balances.Count; i++)
        {
            var b0 = snapshot.Balances[i];
            var j = balances[i]!;
            b0.LocationId.ShouldBe(j["locationId"]!.GetValue<Guid>());
            b0.LocationCode.ShouldBe(j["locationCode"]!.GetValue<string>());
            b0.OnHand.ShouldBe(Dec(j["onHand"])!.Value);
            b0.StockValue.ShouldBe(Dec(j["stockValue"])!.Value);
            b0.AverageCost.ShouldBe(Dec(j["averageCost"]));
            b0.LastUnitCost.ShouldBe(Dec(j["lastUnitCost"]));
        }

        snapshot.Units.Select(u => u.Code).ShouldBe(route["units"]!.AsArray().Select(u => u!["code"]!.GetValue<string>()));

        (await reader.GetItemBySkuAsync("rice-mixed", ct))!.Id.ShouldBe(item);
        (await reader.GetItemBySkuAsync("NO-SUCH", ct)).ShouldBeNull();

        var history = await GetAsync(client, $"/api/items/{item}/movements?pageSize=25", ct);
        var movements = (await reader.GetItemMovementsAsync(item, 25, ct))!;
        var rows = history["items"]!.AsArray();
        movements.Total.ShouldBe(history["total"]!.GetValue<int>());
        movements.Movements.Count.ShouldBe(rows.Count);
        movements.Movements.Count.ShouldBeGreaterThan(2);
        for (var i = 0; i < rows.Count; i++)
        {
            var m = movements.Movements[i];
            var j = rows[i]!;
            m.MovementId.ShouldBe(j["movementId"]!.GetValue<long>());
            m.DocumentId.ShouldBe(j["documentId"]!.GetValue<Guid>());
            m.DocumentType.ShouldBe(j["documentType"]!.GetValue<string>());
            m.PostedAt.ShouldBe(j["postedAt"]!.GetValue<DateTimeOffset>());
            m.Direction.ShouldBe(j["direction"]!.GetValue<string>());
            m.LocationCode.ShouldBe(j["locationCode"]!.GetValue<string>());
            m.QuantityChange.ShouldBe(Dec(j["quantityChange"])!.Value);
            m.ValueChange.ShouldBe(Dec(j["valueChange"])!.Value);
            m.RunningOnHand.ShouldBe(Dec(j["runningOnHand"])!.Value);
            m.RunningValue.ShouldBe(Dec(j["runningValue"])!.Value);
        }

        movements.Movements.Select(m => m.MovementId).ShouldBe(movements.Movements.Select(m => m.MovementId).OrderDescending());
    }

    [Fact]
    public async Task Documents_match_their_routes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "DOC-1", ct);
        var receipt = await PostAsync(client, PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 10m, 100m)), ct);
        await PostAsync(client, PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 2m, 50m)), ct);
        await PostAsync(client, TransferTests.Transfers, TransferTests.Transfer(a, b, TransferTests.TransferLine(item, 3m)), ct);

        await using var scope = Scope(app, tenant);
        var reader = Reader(scope);
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);

        var list = await reader.ListDocumentsAsync("Receipt", from, to, 25, ct);
        var route = await GetAsync(client,
            $"/api/documents?type=Receipt&postedFrom={Uri.EscapeDataString(from.ToString("O"))}&postedTo={Uri.EscapeDataString(to.ToString("O"))}", ct);
        list.Total.ShouldBe(route["total"]!.GetValue<int>());
        list.Total.ShouldBe(2);
        list.Documents.Select(d => d.Id).ShouldBe(route["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()));
        list.Documents.ShouldAllBe(d => d.Type == "Receipt");

        var doc = (await reader.GetDocumentAsync(receipt, ct))!;
        var docRoute = await GetAsync(client, $"/api/documents/{receipt}", ct);
        doc.Type.ShouldBe(docRoute["type"]!.GetValue<string>());
        var lines = docRoute["lines"]!.AsArray();
        doc.Lines.Count.ShouldBe(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            doc.Lines[i].ItemId.ShouldBe(lines[i]!["itemId"]!.GetValue<Guid>());
            doc.Lines[i].Quantity.ShouldBe(Dec(lines[i]!["quantity"])!.Value);
            doc.Lines[i].UnitCost.ShouldBe(Dec(lines[i]!["unitCost"]));
            doc.Lines[i].TotalCost.ShouldBe(Dec(lines[i]!["totalCost"])!.Value);
        }

        var bad = await Should.ThrowAsync<ArgumentException>(() => reader.ListDocumentsAsync("receipt", null, null, 25, ct));
        bad.ParamName.ShouldBe("type");
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => reader.ListDocumentsAsync(null, null, null, 0, ct));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => reader.ListDocumentsAsync(null, null, null, 101, ct));
    }

    [Fact]
    public async Task Locations_flag_physical_and_virtual()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var wh = await PostingTests.NewLocationAsync(client, ct, "WH-9");

        await using var scope = Scope(app, tenant);
        var locations = await Reader(scope).ListLocationsAsync(ct);

        foreach (var code in new[] { "SUP", "CUS", "ADJ" })
        {
            locations.Single(l => l.Code == code).IsPhysical.ShouldBeFalse();
        }

        var physical = locations.Single(l => l.Id == wh);
        physical.IsPhysical.ShouldBeTrue();
        physical.Code.ShouldBe("WH-9");
    }

    [Fact]
    public async Task Ledger_health_matches_the_route()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "H-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);

        await using var scope = Scope(app, tenant);
        var health = await Reader(scope).GetLedgerHealthAsync(ct);
        var route = await GetAsync(client, "/api/ledger/health", ct);

        health.Healthy.ShouldBe(route["healthy"]!.GetValue<bool>());
        health.Healthy.ShouldBeTrue();
        var invariants = route["invariants"]!.AsArray();
        health.Invariants.Count.ShouldBe(invariants.Count);
        for (var i = 0; i < invariants.Count; i++)
        {
            health.Invariants[i].Id.ShouldBe(invariants[i]!["id"]!.GetValue<string>());
            health.Invariants[i].Passed.ShouldBe(invariants[i]!["passed"]!.GetValue<bool>());
            health.Invariants[i].FailureCount.ShouldBe(invariants[i]!["failureCount"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task Another_tenants_rows_are_invisible()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenantA, clientA) = await StartAsync(app);
        var (_, clientB) = await StartAsync(app);
        using var __ = clientA;
        using var ___ = clientB;

        var locA = await PostingTests.NewLocationAsync(clientA, ct, "A");
        var itemA = await PostingTests.NewItemAsync(clientA, "A-ONLY", ct);
        await PostingTests.ReceiveAsync(clientA, locA, itemA, 5m, 10m, ct);

        var locB = await PostingTests.NewLocationAsync(clientB, ct, "B");
        var itemB = await PostingTests.NewItemAsync(clientB, "B-ONLY", ct);
        await PostingTests.ReceiveAsync(clientB, locB, itemB, 8m, 20m, ct);
        var docB = await PostAsync(clientB, PostingTests.Receipts, PostingTests.Receipt(locB, PostingTests.Line(itemB, 1m, 1m)), ct);

        await using var scope = Scope(app, tenantA);
        var reader = Reader(scope);

        (await reader.GetItemAsync(itemB, ct)).ShouldBeNull();
        (await reader.GetItemBySkuAsync("B-ONLY", ct)).ShouldBeNull();
        (await reader.GetDocumentAsync(docB, ct)).ShouldBeNull();
        (await reader.GetItemMovementsAsync(itemB, 25, ct)).ShouldBeNull();

        (await reader.GetStockAsync(itemB, null, 100, ct)).Rows.ShouldBeEmpty();
        var stock = await reader.GetStockAsync(null, null, 100, ct);
        stock.Rows.ShouldAllBe(r => r.ItemId != itemB && r.LocationId != locB);
        stock.Rows.Count.ShouldBe(1);
        (await reader.ListDocumentsAsync(null, null, null, 100, ct)).Documents.ShouldNotContain(d => d.Id == docB);
        (await reader.ListLocationsAsync(ct)).ShouldNotContain(l => l.Id == locB);
        (await reader.SearchItemsAsync("B-ONLY", true, 100, ct)).Items.ShouldBeEmpty();
    }

    [Fact]
    public void Contract_types_carry_no_user_identity()
    {
        string[] banned = ["PostedBy", "CreatedBy", "UpdatedBy", "Email", "Version", "UserId"];
        var types = typeof(IInventoryReader).Assembly.GetExportedTypes();
        types.ShouldNotBeEmpty();
        foreach (var type in types)
        {
            foreach (var name in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(p => p.Name))
            {
                banned.ShouldAllBe(b => !name.Contains(b, StringComparison.OrdinalIgnoreCase), $"{type.Name}.{name}");
            }
        }
    }
}