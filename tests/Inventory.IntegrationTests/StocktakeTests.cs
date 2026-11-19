using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Inventory.Features.Posting;
using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>Stocktakes API (IMS-30): start with a prefill (plan D11–D14), list and detail, count with a snapshot and cancel; post builds on these helpers.</summary>
public sealed class StocktakeTests(DatabaseFixture db)
{
    internal const string Stocktakes = "/api/stocktakes";

    [Fact]
    public async Task Start_prefills_items_with_stock_and_orders_by_sku()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (userId, email) = await PostingTests.AddUserAsync(app, tenant, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var other = await PostingTests.NewLocationAsync(client, ct, "OTHER");
        var b = await PostingTests.NewItemAsync(client, "B-ITEM", ct);
        var a = await PostingTests.NewItemAsync(client, "A-ITEM", ct);
        var c = await PostingTests.NewItemAsync(client, "C-ITEM", ct);
        var d = await PostingTests.NewItemAsync(client, "D-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, b, 5m, 10m, ct);
        await PostingTests.ReceiveAsync(client, location, a, 3m, 20m, ct);
        // C-ITEM has stock elsewhere only; D-ITEM has a zero balance here and is inactive.
        await PostingTests.ReceiveAsync(client, other, c, 2m, 10m, ct);
        await PostingTests.ReceiveAsync(client, location, d, 1m, 10m, ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(d, 1m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PostAsync($"/api/items/{d}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await client.PostAsJsonAsync(Stocktakes, new { locationId = location, notes = "  month end  " }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var stocktake = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var id = stocktake["id"]!.GetValue<Guid>();
        response.Headers.Location!.OriginalString.ShouldBe($"/api/stocktakes/{id}");
        stocktake["status"]!.GetValue<string>().ShouldBe("Draft");
        stocktake["notes"]!.GetValue<string>().ShouldBe("month end");
        stocktake["locationId"]!.GetValue<Guid>().ShouldBe(location);
        stocktake["locationCode"]!.GetValue<string>().ShouldBe("L");
        stocktake["createdBy"]!.GetValue<Guid>().ShouldBe(userId);
        stocktake["postedDocumentId"].ShouldBeNull();
        var lines = stocktake["lines"]!.AsArray().Select(l => l!).ToList();
        lines.Select(l => l["sku"]!.GetValue<string>()).ShouldBe(["A-ITEM", "B-ITEM"]);
        lines.Select(l => l["itemId"]!.GetValue<Guid>()).ShouldBe([a, b]);
        lines.Select(l => l["currentOnHand"]!.GetValue<decimal>()).ShouldBe([3m, 5m]);
        lines.Select(l => l["averageCost"]!.GetValue<decimal>()).ShouldBe([20m, 10m]);
        foreach (var line in lines)
        {
            line["baseUnit"]!.GetValue<string>().ShouldBe("pc");
            line["itemIsActive"]!.GetValue<bool>().ShouldBeTrue();
            line["expectedQuantity"].ShouldBeNull();
            line["countedQuantity"].ShouldBeNull();
            line["difference"].ShouldBeNull();
            line["unitCost"].ShouldBeNull();
            line["countedAt"].ShouldBeNull();
            line["countedBy"].ShouldBeNull();
            line["movedSinceCount"]!.GetValue<bool>().ShouldBeFalse();
        }

        var fetched = await GetJsonAsync(client, $"{Stocktakes}/{id}", ct);
        fetched.ToJsonString().ShouldBe(stocktake.ToJsonString());
    }

    [Fact]
    public async Task Staff_can_start_and_view()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (staffId, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var location = await PostingTests.NewLocationAsync(admin, ct);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var started = await StartAsync(staff, location, ct);

        var id = started["id"]!.GetValue<Guid>();
        started["createdBy"]!.GetValue<Guid>().ShouldBe(staffId);
        started["lines"]!.AsArray().ShouldBeEmpty();
        (await GetJsonAsync(staff, $"{Stocktakes}/{id}", ct))["id"]!.GetValue<Guid>().ShouldBe(id);
        (await GetJsonAsync(staff, Stocktakes, ct))["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ShouldBe([id]);
    }

    [Fact]
    public async Task Second_open_stocktake_is_409_with_its_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var first = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        var second = await client.PostAsJsonAsync(Stocktakes, new { locationId = location }, ct);

        await AssertOpenExistsAsync(second, first, ct);

        // Two racing starts on a fresh location: the open index lets exactly one in.
        var fresh = await PostingTests.NewLocationAsync(client, ct, "FRESH");
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(Stocktakes, new { locationId = fresh }, ct),
            client.PostAsJsonAsync(Stocktakes, new { locationId = fresh }, ct));

        responses.Select(r => r.StatusCode).Order().ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        var winner = responses.Single(r => r.StatusCode == HttpStatusCode.Created);
        var winnerId = (await winner.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        await AssertOpenExistsAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict), winnerId, ct);
        var list = await GetJsonAsync(client, $"{Stocktakes}?locationId={fresh}", ct);
        list["total"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task Start_validates_location()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var supplier = (await GetJsonAsync(client, "/api/locations?page=1&pageSize=100", ct))["items"]!.AsArray()
            .Single(l => l!["code"]!.GetValue<string>() == "SUP")!["id"]!.GetValue<Guid>();
        var inactive = await PostingTests.NewLocationAsync(client, ct, "GONE");
        (await client.PostAsync($"/api/locations/{inactive}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await PostingTests.AssertValidationAsync(await client.PostAsJsonAsync(Stocktakes, new { notes = "x" }, ct), "locationId", ct);
        await PostingTests.AssertValidationAsync(await client.PostAsJsonAsync(Stocktakes, new { locationId = supplier }, ct), "locationId", ct);
        await PostingTests.AssertValidationAsync(
            await client.PostAsJsonAsync(Stocktakes, new { locationId = Guid.NewGuid() }, ct), "locationId", ct);
        await PostingTests.AssertValidationAsync(
            await client.PostAsJsonAsync(Stocktakes, new { locationId = inactive, notes = new string('n', 501) }, ct), "notes", ct);

        var response = await client.PostAsJsonAsync(Stocktakes, new { locationId = inactive }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-location");
        problem["locationId"]!.GetValue<Guid>().ShouldBe(inactive);
        (await GetJsonAsync(client, Stocktakes, ct))["total"]!.GetValue<int>().ShouldBe(0);
    }

    [Fact]
    public async Task List_filters_and_pages()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var one = await PostingTests.NewLocationAsync(client, ct, "ONE");
        var two = await PostingTests.NewLocationAsync(client, ct, "TWO");
        var item = await PostingTests.NewItemAsync(client, "ITEM-1", ct);
        await PostingTests.ReceiveAsync(client, one, item, 4m, 10m, ct);
        // Cancel and posting arrive in later prompts: close the first stocktake at ONE directly, so ONE can open another.
        var oldest = (await StartAsync(client, one, ct))["id"]!.GetValue<Guid>();
        await SetStatusAsync(tenant, oldest, "Cancelled", ct);
        var middle = (await StartAsync(client, two, ct))["id"]!.GetValue<Guid>();
        var newest = (await StartAsync(client, one, ct))["id"]!.GetValue<Guid>();

        var all = await GetJsonAsync(client, Stocktakes, ct);
        var rows = all["items"]!.AsArray().Select(i => i!).ToList();
        rows.Select(r => r["id"]!.GetValue<Guid>()).ShouldBe([newest, middle, oldest]);
        all["total"]!.GetValue<int>().ShouldBe(3);
        rows[0]["locationCode"]!.GetValue<string>().ShouldBe("ONE");
        rows[0]["locationName"]!.GetValue<string>().ShouldBe("Warehouse");
        rows[0]["lineCount"]!.GetValue<int>().ShouldBe(1);
        rows[0]["countedCount"]!.GetValue<int>().ShouldBe(0);
        rows[1]["lineCount"]!.GetValue<int>().ShouldBe(0);
        rows[2]["status"]!.GetValue<string>().ShouldBe("Cancelled");

        var atOne = await GetJsonAsync(client, $"{Stocktakes}?locationId={one}", ct);
        atOne["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ShouldBe([newest, oldest]);
        var drafts = await GetJsonAsync(client, $"{Stocktakes}?status=Draft", ct);
        drafts["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ShouldBe([newest, middle]);
        var draftsAtOne = await GetJsonAsync(client, $"{Stocktakes}?status=Draft&locationId={one}", ct);
        draftsAtOne["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ShouldBe([newest]);

        var paged = await GetJsonAsync(client, $"{Stocktakes}?page=2&pageSize=2", ct);
        paged["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ShouldBe([oldest]);
        paged["page"]!.GetValue<int>().ShouldBe(2);
        paged["pageSize"]!.GetValue<int>().ShouldBe(2);
        paged["total"]!.GetValue<int>().ShouldBe(3);

        await PostingTests.AssertValidationAsync(await client.GetAsync($"{Stocktakes}?status=draft", ct), "status", ct);
        await PostingTests.AssertValidationAsync(await client.GetAsync($"{Stocktakes}?status=1", ct), "status", ct);
        await PostingTests.AssertValidationAsync(await client.GetAsync($"{Stocktakes}?pageSize=101", ct), "pageSize", ct);
    }

    [Fact]
    public async Task Unknown_id_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var response = await client.GetAsync($"{Stocktakes}/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
    }

    [Fact]
    public async Task Count_records_the_snapshot_and_difference()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (userId, email) = await PostingTests.AddUserAsync(app, tenant, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var item = await PostingTests.NewItemAsync(client, "ITEM-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        var line = OnlyLine(await CountOkAsync(client, id, item, new { quantity = 8 }, ct));

        line["expectedQuantity"]!.GetValue<decimal>().ShouldBe(10m);
        line["countedQuantity"]!.GetValue<decimal>().ShouldBe(8m);
        line["difference"]!.GetValue<decimal>().ShouldBe(-2m);
        line["countedBy"]!.GetValue<Guid>().ShouldBe(userId);
        line["countedAt"].ShouldNotBeNull();
        line["movedSinceCount"]!.GetValue<bool>().ShouldBeFalse();

        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(item, 1m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var moved = OnlyLine(await GetJsonAsync(client, $"{Stocktakes}/{id}", ct));
        moved["movedSinceCount"]!.GetValue<bool>().ShouldBeTrue();
        moved["currentOnHand"]!.GetValue<decimal>().ShouldBe(9m);
        moved["expectedQuantity"]!.GetValue<decimal>().ShouldBe(10m);
    }

    [Fact]
    public async Task Recount_takes_a_new_snapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var item = await PostingTests.NewItemAsync(client, "ITEM-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, item, new { quantity = 8 }, ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(item, 1m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var line = OnlyLine(await CountOkAsync(client, id, item, new { quantity = 7 }, ct));

        line["expectedQuantity"]!.GetValue<decimal>().ShouldBe(9m);
        line["countedQuantity"]!.GetValue<decimal>().ShouldBe(7m);
        line["difference"]!.GetValue<decimal>().ShouldBe(-2m);
        line["movedSinceCount"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Count_in_a_unit_converts_to_base()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var item = await PostingTests.NewItemAsync(client, "ITEM-1", ct);
        (await client.PostAsJsonAsync($"/api/items/{item}/units", new { code = "case", factor = 12 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        var line = OnlyLine(await CountOkAsync(client, id, item, new { quantity = 1.5m, unit = " Case " }, ct));
        line["countedQuantity"]!.GetValue<decimal>().ShouldBe(18m);

        // The base unit, named or not, converts by 1.
        OnlyLine(await CountOkAsync(client, id, item, new { quantity = 3, unit = "pc" }, ct))["countedQuantity"]!.GetValue<decimal>().ShouldBe(3m);

        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { quantity = 0.00001m }, ct), "quantity", ct);
        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { quantity = 0.00001m, unit = "case" }, ct), "quantity", ct);
        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { quantity = 1, unit = "box" }, ct), "unit", ct);
        // The failed counts left the last good count in place.
        OnlyLine(await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["countedQuantity"]!.GetValue<decimal>().ShouldBe(3m);
    }

    [Fact]
    public async Task Count_validates_quantity_unit_and_unit_cost()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var item = await PostingTests.NewItemAsync(client, "ITEM-1", ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { unit = "pc" }, ct), "quantity", ct);
        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { quantity = -1 }, ct), "quantity", ct);
        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { quantity = 1.00001m }, ct), "quantity", ct);
        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { quantity = 1_000_000_000m }, ct), "quantity", ct);
        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { quantity = 1, unitCost = -1 }, ct), "unitCost", ct);
        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { quantity = 1, unitCost = 1.00001m }, ct), "unitCost", ct);
        await PostingTests.AssertValidationAsync(await CountAsync(client, id, item, new { quantity = 1, unit = new string('u', 17) }, ct), "unit", ct);

        var line = OnlyLine(await CountOkAsync(client, id, item, new { quantity = 0, unitCost = 2.5m }, ct));
        line["countedQuantity"]!.GetValue<decimal>().ShouldBe(0m);
        line["unitCost"]!.GetValue<decimal>().ShouldBe(2.5m);
    }

    [Fact]
    public async Task Count_adds_an_item_not_on_the_sheet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var item = await PostingTests.NewItemAsync(client, "NEW-ITEM", ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        var line = OnlyLine(await CountOkAsync(client, id, item, new { quantity = 4 }, ct));

        line["itemId"]!.GetValue<Guid>().ShouldBe(item);
        line["expectedQuantity"]!.GetValue<decimal>().ShouldBe(0m);
        line["difference"]!.GetValue<decimal>().ShouldBe(4m);
        var row = await ReadLineAsync(tenant, id, item, ct);
        row.Expected.ShouldBe(0m);
        row.ExpectedLastMovementId.ShouldBeNull();
        row.Counted.ShouldBe(4m);
    }

    [Fact]
    public async Task Staff_can_count()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (staffId, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var location = await PostingTests.NewLocationAsync(admin, ct, "L");
        var item = await PostingTests.NewItemAsync(admin, "ITEM-1", ct);
        var id = (await StartAsync(admin, location, ct))["id"]!.GetValue<Guid>();
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var line = OnlyLine(await CountOkAsync(staff, id, item, new { quantity = 2 }, ct));

        line["countedBy"]!.GetValue<Guid>().ShouldBe(staffId);
    }

    [Fact]
    public async Task Count_on_an_inactive_item_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var item = await PostingTests.NewItemAsync(client, "ITEM-1", ct);
        (await client.PostAsync($"/api/items/{item}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        var response = await CountAsync(client, id, item, new { quantity = 1 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-item");
        problem["itemId"]!.GetValue<Guid>().ShouldBe(item);
        (await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["lines"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Count_on_unknown_ids_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var item = await PostingTests.NewItemAsync(client, "ITEM-1", ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        (await CountAsync(client, Guid.NewGuid(), item, new { quantity = 1 }, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CountAsync(client, id, Guid.NewGuid(), new { quantity = 1 }, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsync($"{Stocktakes}/{Guid.NewGuid()}/cancel", null, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Count_after_cancel_is_409_stocktake_not_open()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var item = await PostingTests.NewItemAsync(client, "ITEM-1", ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        (await client.PostAsync($"{Stocktakes}/{id}/cancel", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await CountAsync(client, id, item, new { quantity = 1 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/stocktake-not-open");
        problem["stocktakeStatus"]!.GetValue<string>().ShouldBe("Cancelled");
    }

    [Fact]
    public async Task Cancel_is_idempotent_and_frees_the_location()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        var first = await client.PostAsync($"{Stocktakes}/{id}/cancel", null, ct);
        var second = await client.PostAsync($"{Stocktakes}/{id}/cancel", null, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        body["status"]!.GetValue<string>().ShouldBe("Cancelled");
        (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!.ToJsonString().ShouldBe(body.ToJsonString());
        (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>().ShouldNotBe(id);
        (await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["status"]!.GetValue<string>().ShouldBe("Cancelled");
    }

    [Fact]
    public async Task Staff_gets_403_on_cancel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var location = await PostingTests.NewLocationAsync(admin, ct, "L");
        var id = (await StartAsync(admin, location, ct))["id"]!.GetValue<Guid>();
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var response = await staff.PostAsync($"{Stocktakes}/{id}/cancel", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Location.ShouldBeNull();
        (await GetJsonAsync(admin, $"{Stocktakes}/{id}", ct))["status"]!.GetValue<string>().ShouldBe("Draft");
    }

    [Fact]
    public async Task Concurrent_counts_on_one_line_leave_one_consistent_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var item = await PostingTests.NewItemAsync(client, "ITEM-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 20m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        var responses = await Task.WhenAll(Enumerable.Range(1, 10).Select(q => CountAsync(client, id, item, new { quantity = q }, ct)));

        responses.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.OK);
        var row = await ReadLineAsync(tenant, id, item, ct);
        row.Counted.ShouldNotBeNull();
        row.Counted!.Value.ShouldBeInRange(1m, 10m);
        row.Expected.ShouldBe(20m);
        row.ExpectedLastMovementId.ShouldNotBeNull();
        (await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["lines"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Post_creates_one_count_correction_with_the_deltas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (adminId, email) = await PostingTests.AddUserAsync(app, tenant, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        var y = await PostingTests.NewItemAsync(client, "Y-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        await PostingTests.ReceiveAsync(client, location, y, 4m, 2m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 8 }, ct);
        await CountOkAsync(client, id, y, new { quantity = 4 }, ct);

        var posted = await PostOkAsync(client, id, ct);

        posted["status"]!.GetValue<string>().ShouldBe("Posted");
        var documentId = posted["postedDocumentId"]!.GetValue<Guid>();
        var document = await GetJsonAsync(client, $"/api/documents/{documentId}", ct);
        document["type"]!.GetValue<string>().ShouldBe("Adjustment");
        document["reasonCode"]!.GetValue<string>().ShouldBe("CountCorrection");
        document["reference"]!.GetValue<string>().ShouldBe($"Stocktake {id.ToString("N")[..8].ToUpperInvariant()}");
        document["postedBy"]!.GetValue<Guid>().ShouldBe(adminId);
        var line = document["lines"]!.AsArray().ShouldHaveSingleItem()!;
        line["lineNo"]!.GetValue<int>().ShouldBe(1);
        line["itemId"]!.GetValue<Guid>().ShouldBe(x);
        line["fromLocationId"]!.GetValue<Guid>().ShouldBe(location);
        line["quantity"]!.GetValue<decimal>().ShouldBe(2m);
        line["totalCost"]!.GetValue<decimal>().ShouldBe(10.0000m);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((8m, 40.0000m));
        (await PostingTests.BalanceAsync(connection, y, location, ct)).ShouldBe((4m, 8.0000m));
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Delta_is_measured_against_the_count_snapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 8 }, ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(x, 3m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await PostOkAsync(client, id, ct);

        // The count was 2 short of the 10 it saw; the later issue of 3 stands, so 7 - 2.
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((5m, 25.0000m));
    }

    [Fact]
    public async Task Positive_delta_on_an_empty_balance_needs_a_unit_cost()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "A-X", ct);
        var y = await PostingTests.NewItemAsync(client, "B-Y", ct);
        await PostingTests.ReceiveAsync(client, location, x, 5m, 5m, ct);
        await PostingTests.ReceiveAsync(client, location, y, 2m, 3m, ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(y, 2m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 5 }, ct);
        await CountOkAsync(client, id, y, new { quantity = 2 }, ct);

        // Y is the document's only line (index 0) but the sheet's second.
        await PostingTests.AssertValidationAsync(await PostAsync(client, id, ct), "lines[1].unitCost", ct);

        (await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["status"]!.GetValue<string>().ShouldBe("Draft");
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(0);

        await CountOkAsync(client, id, y, new { quantity = 2, unitCost = 3 }, ct);
        await PostOkAsync(client, id, ct);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, y, location, ct)).ShouldBe((2m, 6.0000m));
        (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((5m, 25.0000m));
    }

    [Fact]
    public async Task Uncounted_line_blocks_post()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "A-X", ct);
        var y = await PostingTests.NewItemAsync(client, "B-Y", ct);
        await PostingTests.ReceiveAsync(client, location, x, 5m, 5m, ct);
        await PostingTests.ReceiveAsync(client, location, y, 2m, 3m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 4 }, ct);

        await PostingTests.AssertValidationAsync(await PostAsync(client, id, ct), "lines[1].quantity", ct);

        (await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["status"]!.GetValue<string>().ShouldBe("Draft");
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(0);

        // A sheet with no lines has nothing to post.
        var empty = (await StartAsync(client, await PostingTests.NewLocationAsync(client, ct, "EMPTY"), ct))["id"]!.GetValue<Guid>();
        await PostingTests.AssertValidationAsync(await PostAsync(client, empty, ct), "lines", ct);
    }

    [Fact]
    public async Task No_differences_posts_no_document()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 10 }, ct);

        var posted = await PostOkAsync(client, id, ct);

        posted["status"]!.GetValue<string>().ShouldBe("Posted");
        posted["postedDocumentId"].ShouldBeNull();
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Insufficient_stock_reverts_to_draft()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 0 }, ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(x, 10m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await PostingTests.AssertInsufficientStockAsync(await PostAsync(client, id, ct), x, location, 0m, 10m, ct);

        (await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["status"]!.GetValue<string>().ShouldBe("Draft");
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Posting_twice_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 7 }, ct);

        var first = await PostOkAsync(client, id, ct);
        var second = await PostOkAsync(client, id, ct);

        second["postedDocumentId"]!.GetValue<Guid>().ShouldBe(first["postedDocumentId"]!.GetValue<Guid>());
        second.ToJsonString().ShouldBe(first.ToJsonString());
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(1);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((7m, 35.0000m));
    }

    [Fact]
    public async Task Parallel_posts_make_one_document()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 6 }, ct);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAsync(client, id, ct)));

        responses.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.OK);
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<JsonNode>(ct)));
        bodies.Select(b => b!["status"]!.GetValue<string>()).ShouldAllBe(s => s == "Posted");
        bodies.Select(b => b!["postedDocumentId"]!.GetValue<Guid>()).Distinct().ShouldHaveSingleItem();
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(1);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((6m, 30.0000m));
    }

    [Fact]
    public async Task Crash_between_post_and_finish_heals_on_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 9 }, ct);
        var documentId = (await PostOkAsync(client, id, ct))["postedDocumentId"]!.GetValue<Guid>();

        // The adjustment committed, then the process died before step 4 recorded it.
        await SetStatusAsync(tenant, id, "Posting", ct);

        var healed = await PostOkAsync(client, id, ct);

        healed["status"]!.GetValue<string>().ShouldBe("Posted");
        healed["postedDocumentId"]!.GetValue<Guid>().ShouldBe(documentId);
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(1);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((9m, 45.0000m));
    }

    [Fact]
    public async Task Key_held_by_another_sheet_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 9 }, ct);
        await PostOkAsync(client, id, ct);

        // A state plan D17 rules out, made by hand: Posting, with lines that no longer hash like the document under the key.
        await SetStatusAsync(tenant, id, "Posting", ct);
        await using (var connection = await LedgerRows.OpenAsync(db, tenant, ct))
        {
            await using var recount = new SqlCommand(
                "UPDATE inventory.StocktakeLines SET CountedQuantity = 7 WHERE StocktakeId = @id", connection);
            recount.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = id });
            (await recount.ExecuteNonQueryAsync(ct)).ShouldBe(1);
        }

        var response = await PostAsync(client, id, ct);

        // The handler throws rather than record another sheet's adjustment; the exception handler makes it a 500.
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var stocktake = await GetJsonAsync(client, $"{Stocktakes}/{id}", ct);
        stocktake["status"]!.GetValue<string>().ShouldBe("Posting");
        stocktake["postedDocumentId"].ShouldBeNull();
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(1);
        await using var ledger = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(ledger, x, location, ct)).ShouldBe((9m, 45.0000m));
    }

    [Fact]
    public async Task Stale_sheet_is_refused_after_an_unfreeze_and_recount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 9 }, ct);

        // The interleaving plan D17 closes: B freezes the sheet counted 9 and waits at the ledger lock; C fails before the
        // engine and unfreezes; X is recounted to 7. Before D17, B then posted the 9 and finished: a Posted stocktake whose
        // sheet says 7 over a balance of 9.
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await TakeLedgerLockAsync(holder, tx, tenant, ct);
        var b = PostAsync(client, id, ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        await UnfreezeAndRecountAsync(client, tenant, id, x, 7m, ct);

        tx.Rollback();

        await ReversalTests.AssertProblemAsync(await b, HttpStatusCode.Conflict, "concurrency-conflict", ct);
        var stocktake = await GetJsonAsync(client, $"{Stocktakes}/{id}", ct);
        stocktake["status"]!.GetValue<string>().ShouldBe("Draft");
        OnlyLine(stocktake)["countedQuantity"]!.GetValue<decimal>().ShouldBe(7m);
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(0);
        await using (var connection = await LedgerRows.OpenAsync(db, tenant, ct))
        {
            (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((10m, 50.0000m));
        }

        // Posting again posts the sheet as it is now.
        (await PostOkAsync(client, id, ct))["status"]!.GetValue<string>().ShouldBe("Posted");
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(1);
        await using var ledger = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(ledger, x, location, ct)).ShouldBe((7m, 35.0000m));
    }

    [Fact]
    public async Task Stale_attempt_is_refused_when_the_recount_is_frozen_again()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 9 }, ct);

        // As above, B holds the sheet counted 9 at the ledger lock while the recount to 7 lands.
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await TakeLedgerLockAsync(holder, tx, tenant, ct);
        var b = PostAsync(client, id, ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        await UnfreezeAndRecountAsync(client, tenant, id, x, 7m, ct);

        // Another session queues Exclusive behind B, then D freezes the recount and queues behind it. Locks are granted in
        // queue order, so the release lets B's guard run alone while D is frozen and in flight: the order in which an
        // unfreeze on B's 409 would undo D's freeze.
        await using var queue = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var queueTx = queue.BeginTransaction();
        var queued = TakeLedgerLockAsync(queue, queueTx, tenant, ct);
        (await WaitUntilQueuedAsync(holder, tx, 2, ct)).ShouldBe(2);
        var d = PostAsync(client, id, ct);
        (await WaitUntilQueuedAsync(holder, tx, 3, ct)).ShouldBe(3);

        tx.Rollback();

        await ReversalTests.AssertProblemAsync(await b, HttpStatusCode.Conflict, "concurrency-conflict", ct);
        await queued;
        // B left D's freeze as it was.
        (await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["status"]!.GetValue<string>().ShouldBe("Posting");
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(0);

        queueTx.Rollback();

        var response = await d;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        var posted = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        posted["status"]!.GetValue<string>().ShouldBe("Posted");
        posted["postedDocumentId"]!.GetValue<Guid>().ShouldBe(await KeyHolderAsync(tenant, id, ct));
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(1);
        await using var ledger = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(ledger, x, location, ct)).ShouldBe((7m, 35.0000m));
    }

    [Fact]
    public async Task Parallel_post_that_reaches_the_guard_after_the_finish_replays()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, id, x, new { quantity = 9 }, ct);

        // A double post of one sheet: the first queues at the ledger lock, an Exclusive session queues behind it, and the
        // second passes step 2 (no document yet) and queues behind that. The release lets the first post and finish alone.
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await TakeLedgerLockAsync(holder, tx, tenant, ct);
        var first = PostAsync(client, id, ct);
        (await WaitUntilQueuedAsync(holder, tx, 1, ct)).ShouldBe(1);
        await using var queue = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var queueTx = queue.BeginTransaction();
        var queued = TakeLedgerLockAsync(queue, queueTx, tenant, ct);
        (await WaitUntilQueuedAsync(holder, tx, 2, ct)).ShouldBe(2);
        var second = PostAsync(client, id, ct);
        (await WaitUntilQueuedAsync(holder, tx, 3, ct)).ShouldBe(3);

        tx.Rollback();

        var firstResponse = await first;
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await firstResponse.Content.ReadAsStringAsync(ct));
        var posted = (await firstResponse.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        posted["status"]!.GetValue<string>().ShouldBe("Posted");
        await queued;
        queueTx.Rollback();

        // The guard sees Posted with the same sheet, so step 4 meets the first's document and replays it.
        var secondResponse = await second;
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await secondResponse.Content.ReadAsStringAsync(ct));
        (await secondResponse.Content.ReadFromJsonAsync<JsonNode>(ct))!.ToJsonString().ShouldBe(posted.ToJsonString());
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(1);
        await using var ledger = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(ledger, x, location, ct)).ShouldBe((9m, 45.0000m));
    }

    [Fact]
    public async Task Count_while_posting_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "X-ITEM", ct);
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await SetStatusAsync(tenant, id, "Posting", ct);

        var response = await CountAsync(client, id, x, new { quantity = 1 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/stocktake-not-open");
        problem["stocktakeStatus"]!.GetValue<string>().ShouldBe("Posting");
    }

    [Fact]
    public async Task Over_100_differences_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (userId, email) = await PostingTests.AddUserAsync(app, tenant, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var id = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();

        await using (var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant))
        {
            var context = scope.Services.GetRequiredService<InventoryDbContext>();
            var now = DateTimeOffset.UtcNow;
            var items = Enumerable.Range(0, PostingValidation.MaxLines + 1)
                .Select(i => new Item { Sku = $"BULK-{i:D3}", Name = $"Bulk {i}", BaseUnit = "pc", IsActive = true, CreatedAt = now })
                .ToList();
            context.Items.AddRange(items);
            await context.SaveChangesAsync(ct);
            context.StocktakeLines.AddRange(items.Select(item => new StocktakeLine
            {
                StocktakeId = id,
                ItemId = item.Id,
                ExpectedQuantity = 0m,
                CountedQuantity = 1m,
                UnitCost = 1m,
                CountedAt = now,
                CountedBy = userId,
            }));
            await context.SaveChangesAsync(ct);
        }

        await PostingTests.AssertValidationAsync(await PostAsync(client, id, ct), "lines", ct);

        (await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["status"]!.GetValue<string>().ShouldBe("Draft");
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Staff_gets_403_on_post()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var location = await PostingTests.NewLocationAsync(admin, ct, "L");
        var x = await PostingTests.NewItemAsync(admin, "X-ITEM", ct);
        await PostingTests.ReceiveAsync(admin, location, x, 10m, 5m, ct);
        var id = (await StartAsync(admin, location, ct))["id"]!.GetValue<Guid>();
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);
        await CountOkAsync(staff, id, x, new { quantity = 8 }, ct);

        var response = await PostAsync(staff, id, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Location.ShouldBeNull();
        (await GetJsonAsync(admin, $"{Stocktakes}/{id}", ct))["status"]!.GetValue<string>().ShouldBe("Draft");
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Ledger_health_is_clean_after_stocktakes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct, "L");
        var x = await PostingTests.NewItemAsync(client, "A-X", ct);
        var y = await PostingTests.NewItemAsync(client, "B-Y", ct);
        await PostingTests.ReceiveAsync(client, location, x, 10m, 5m, ct);
        await PostingTests.ReceiveAsync(client, location, y, 4m, 2m, ct);

        // A loss and a find with its own cost, then a crash healed by a retry.
        var first = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, first, x, new { quantity = 8 }, ct);
        await CountOkAsync(client, first, y, new { quantity = 6, unitCost = 3 }, ct);
        await PostOkAsync(client, first, ct);
        await SetStatusAsync(tenant, first, "Posting", ct);
        await PostOkAsync(client, first, ct);

        // A refused post, then a fixed count posted.
        var second = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, second, x, new { quantity = 0 }, ct);
        await CountOkAsync(client, second, y, new { quantity = 6 }, ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(x, 8m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(client, second, ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await CountOkAsync(client, second, x, new { quantity = 1, unitCost = 4 }, ct);
        await PostOkAsync(client, second, ct);

        // Nothing differs.
        var third = (await StartAsync(client, location, ct))["id"]!.GetValue<Guid>();
        await CountOkAsync(client, third, x, new { quantity = 1 }, ct);
        await CountOkAsync(client, third, y, new { quantity = 6 }, ct);
        (await PostOkAsync(client, third, ct))["postedDocumentId"].ShouldBeNull();

        var health = await GetJsonAsync(client, "/api/ledger/health", ct);

        health["healthy"]!.GetValue<bool>().ShouldBeTrue(health.ToJsonString());
        foreach (var invariant in health["invariants"]!.AsArray())
        {
            invariant!["passed"]!.GetValue<bool>().ShouldBeTrue(invariant.ToJsonString());
        }

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
        (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((1m, 4.0000m));
        (await PostingTests.BalanceAsync(connection, y, location, ct)).ShouldBe((6m, 14.0000m));
    }

    /// <summary>POST a stocktake's post; the response is returned as is.</summary>
    internal static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid stocktake, CancellationToken ct) =>
        client.PostAsync($"{Stocktakes}/{stocktake}/post", null, ct);

    /// <summary>POST a stocktake's post; asserts 200 and returns the stocktake detail.</summary>
    private static async Task<JsonNode> PostOkAsync(HttpClient client, Guid stocktake, CancellationToken ct)
    {
        var response = await PostAsync(client, stocktake, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    /// <summary>How many documents hold the stocktake's server key (plan D10).</summary>
    private async Task<int> KeyHoldersAsync(Guid tenant, Guid stocktake, CancellationToken ct)
    {
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        return await PostingTests.ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM inventory.StockDocuments WHERE IdempotencyKey = @key",
            new SqlParameter("@key", SqlDbType.VarChar, 64) { Value = $"stocktake:{stocktake:D}" },
            ct);
    }

    /// <summary>The id of the document that holds the stocktake's server key; there must be one.</summary>
    private async Task<Guid> KeyHolderAsync(Guid tenant, Guid stocktake, CancellationToken ct)
    {
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        return await PostingTests.ScalarAsync<Guid>(
            connection,
            "SELECT Id FROM inventory.StockDocuments WHERE IdempotencyKey = @key",
            new SqlParameter("@key", SqlDbType.VarChar, 64) { Value = $"stocktake:{stocktake:D}" },
            ct);
    }

    /// <summary>
    /// With a post frozen and held at the ledger lock: deactivates the item by SQL (the API refuses while it has stock), so
    /// a second post fails before the engine and unfreezes, with no document under the key; then reactivates and recounts it.
    /// </summary>
    private async Task UnfreezeAndRecountAsync(HttpClient client, Guid tenant, Guid id, Guid item, decimal quantity, CancellationToken ct)
    {
        await SetItemActiveAsync(tenant, item, false, ct);
        await ReversalTests.AssertProblemAsync(await PostAsync(client, id, ct), HttpStatusCode.UnprocessableEntity, "inactive-item", ct);
        (await GetJsonAsync(client, $"{Stocktakes}/{id}", ct))["status"]!.GetValue<string>().ShouldBe("Draft");
        (await KeyHoldersAsync(tenant, id, ct)).ShouldBe(0);
        await SetItemActiveAsync(tenant, item, true, ct);
        await CountOkAsync(client, id, item, new { quantity }, ct);
    }

    /// <summary>Sets an item's <c>IsActive</c> as <c>inventory_app</c> in the tenant's session.</summary>
    private async Task SetItemActiveAsync(Guid tenant, Guid item, bool active, CancellationToken ct)
    {
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var command = new SqlCommand("UPDATE inventory.Items SET IsActive = @active WHERE Id = @x", connection);
        command.Parameters.Add(new SqlParameter("@active", SqlDbType.Bit) { Value = active });
        command.Parameters.Add(new SqlParameter("@x", SqlDbType.UniqueIdentifier) { Value = item });
        (await command.ExecuteNonQueryAsync(ct)).ShouldBe(1);
    }

    /// <summary>Takes the tenant's ledger applock Exclusive, owned by <paramref name="tx"/>, as S11's rebuild does.</summary>
    private static async Task TakeLedgerLockAsync(SqlConnection holder, SqlTransaction tx, Guid tenant, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            DECLARE @r int;
            EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction';
            SELECT @r;
            """, holder, tx);
        command.Parameters.Add(new SqlParameter("@res", SqlDbType.NVarChar, 255) { Value = $"ledger:{tenant:D}" });
        ((int)(await command.ExecuteScalarAsync(ct))!).ShouldBeGreaterThanOrEqualTo(0);
    }

    /// <summary>
    /// Waits (10 s at most) until <paramref name="count"/> requests wait on <paramref name="holder"/>, directly or behind
    /// another waiter, polling sys.dm_exec_requests as sa; returns the last count seen (as in PostingConcurrencyTests).
    /// </summary>
    private async Task<int> WaitUntilQueuedAsync(SqlConnection holder, SqlTransaction tx, int count, CancellationToken ct)
    {
        int spid;
        await using (var command = new SqlCommand("SELECT @@SPID", holder, tx))
        {
            spid = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }

        await using var admin = new SqlConnection(db.AdminConnectionString);
        await admin.OpenAsync(ct);
        await using var poll = new SqlCommand("""
            WITH waiters AS (
                SELECT session_id FROM sys.dm_exec_requests WHERE blocking_session_id = @spid
                UNION ALL
                SELECT r.session_id FROM sys.dm_exec_requests AS r JOIN waiters AS w ON r.blocking_session_id = w.session_id)
            SELECT COUNT(DISTINCT session_id) FROM waiters;
            """, admin);
        poll.Parameters.Add(new SqlParameter("@spid", SqlDbType.Int) { Value = spid });

        var waited = Stopwatch.StartNew();
        var queued = 0;
        while (waited.Elapsed < TimeSpan.FromSeconds(10) && queued < count)
        {
            queued = (int)(await poll.ExecuteScalarAsync(ct))!;
            if (queued < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
            }
        }

        return queued;
    }

    /// <summary>PUT a count; the response is returned as is.</summary>
    internal static Task<HttpResponseMessage> CountAsync(HttpClient client, Guid stocktake, Guid item, object body, CancellationToken ct) =>
        client.PutAsJsonAsync($"{Stocktakes}/{stocktake}/lines/{item}", body, ct);

    /// <summary>PUT a count; asserts 200 and returns the stocktake detail.</summary>
    internal static async Task<JsonNode> CountOkAsync(HttpClient client, Guid stocktake, Guid item, object body, CancellationToken ct)
    {
        var response = await CountAsync(client, stocktake, item, body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static JsonNode OnlyLine(JsonNode detail) => detail["lines"]!.AsArray().Single()!;

    /// <summary>A line's stored count fields, read as <c>inventory_app</c> in the tenant's session.</summary>
    private async Task<(decimal? Counted, decimal? Expected, long? ExpectedLastMovementId)> ReadLineAsync(
        Guid tenant, Guid stocktake, Guid item, CancellationToken ct)
    {
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var command = new SqlCommand(
            "SELECT CountedQuantity, ExpectedQuantity, ExpectedLastMovementId FROM inventory.StocktakeLines WHERE StocktakeId = @s AND ItemId = @i",
            connection);
        command.Parameters.Add(new SqlParameter("@s", SqlDbType.UniqueIdentifier) { Value = stocktake });
        command.Parameters.Add(new SqlParameter("@i", SqlDbType.UniqueIdentifier) { Value = item });
        await using var reader = await command.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).ShouldBeTrue();
        return (
            reader.IsDBNull(0) ? null : reader.GetDecimal(0),
            reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    /// <summary>Starts a stocktake at the location; asserts 201 and returns the detail body.</summary>
    internal static async Task<JsonNode> StartAsync(HttpClient client, Guid locationId, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(Stocktakes, new { locationId }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static async Task AssertOpenExistsAsync(HttpResponseMessage response, Guid openId, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/stocktake-open-exists");
        problem["stocktakeId"]!.GetValue<Guid>().ShouldBe(openId);
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        var response = await client.GetAsync(path, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    /// <summary>
    /// Sets a stocktake's status as <c>inventory_app</c> in the tenant's session (the stocktake tables have no triggers),
    /// clearing <c>PostedDocumentId</c> as <c>CK_Stocktakes_PostedDocument</c> requires off Posted: Posting simulates a crash
    /// between the post and the finish.
    /// </summary>
    private async Task SetStatusAsync(Guid tenant, Guid id, string status, CancellationToken ct)
    {
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var command = new SqlCommand(
            "UPDATE inventory.Stocktakes SET Status = @status, PostedDocumentId = NULL WHERE Id = @id", connection);
        command.Parameters.Add(new SqlParameter("@status", SqlDbType.VarChar, 16) { Value = status });
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = id });
        (await command.ExecuteNonQueryAsync(ct)).ShouldBe(1);
    }
}