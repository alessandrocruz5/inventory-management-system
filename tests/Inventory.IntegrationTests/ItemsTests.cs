using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Api.Setup;

using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>Items API (S4): the read routes (Staff may read, R5) and the Admin-only create, update and deactivate routes.</summary>
public sealed class ItemsTests(DatabaseFixture db)
{
    [Fact]
    public async Task List_pages_and_orders_by_sku()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        await AddItemsAsync(app, tenant, ("C-1", "Third", true), ("A-1", "First", true), ("B-1", "Second", true));
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var first = await GetJsonAsync(client, "/api/items?pageSize=2", ct);
        var second = await GetJsonAsync(client, "/api/items?page=2&pageSize=2", ct);

        Skus(first).ShouldBe(["A-1", "B-1"]);
        first["page"]!.GetValue<int>().ShouldBe(1);
        first["pageSize"]!.GetValue<int>().ShouldBe(2);
        first["total"]!.GetValue<int>().ShouldBe(3);
        Skus(second).ShouldBe(["C-1"]);
    }

    [Fact]
    public async Task List_defaults_to_page_1_of_25()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var list = await GetJsonAsync(client, "/api/items", ct);

        list["page"]!.GetValue<int>().ShouldBe(1);
        list["pageSize"]!.GetValue<int>().ShouldBe(25);
    }

    [Fact]
    public async Task List_searches_sku_and_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        await AddItemsAsync(app, tenant, ("RICE-1", "White rice", true), ("OIL-1", "Palm oil", true));
        using var client = await DatabaseFixture.SignInAsync(app, email);

        Skus(await GetJsonAsync(client, "/api/items?search=rice", ct)).ShouldBe(["RICE-1"]);
        Skus(await GetJsonAsync(client, "/api/items?search=palm", ct)).ShouldBe(["OIL-1"]);
    }

    [Fact]
    public async Task List_filters_by_active()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        await AddItemsAsync(app, tenant, ("ON-1", "Active", true), ("OFF-1", "Inactive", false));
        using var client = await DatabaseFixture.SignInAsync(app, email);

        Skus(await GetJsonAsync(client, "/api/items?active=true", ct)).ShouldBe(["ON-1"]);
        Skus(await GetJsonAsync(client, "/api/items?active=false", ct)).ShouldBe(["OFF-1"]);
        Skus(await GetJsonAsync(client, "/api/items", ct)).ShouldBe(["OFF-1", "ON-1"]);
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    public async Task List_rejects_out_of_range_paging(string query, string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var response = await client.GetAsync($"/api/items?{query}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/validation");
        problem["errors"]!.AsObject().ContainsKey(key).ShouldBeTrue();
    }

    [Fact]
    public async Task Get_returns_the_item_with_its_version()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        var id = (await AddItemsAsync(app, tenant, ("RICE-1", "White rice", true)))[0];
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var item = await GetJsonAsync(client, $"/api/items/{id}", ct);

        item["id"]!.GetValue<Guid>().ShouldBe(id);
        item["sku"]!.GetValue<string>().ShouldBe("RICE-1");
        item["name"]!.GetValue<string>().ShouldBe("White rice");
        item["baseUnit"]!.GetValue<string>().ShouldBe("pc");
        item["isActive"]!.GetValue<bool>().ShouldBeTrue();
        Convert.FromBase64String(item["version"]!.GetValue<string>()).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Get_unknown_id_is_404_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var response = await client.GetAsync($"/api/items/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
    }

    [Fact]
    public async Task Create_returns_201_with_location_and_normalized_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var response = await client.PostAsJsonAsync("/api/items", new { sku = "  AB-1 ", name = " Rice ", baseUnit = " KG " }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var item = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var id = item["id"]!.GetValue<Guid>();
        response.Headers.Location!.OriginalString.ShouldBe($"/api/items/{id}");
        item["sku"]!.GetValue<string>().ShouldBe("AB-1");
        item["name"]!.GetValue<string>().ShouldBe("Rice");
        item["baseUnit"]!.GetValue<string>().ShouldBe("kg");
        item["isActive"]!.GetValue<bool>().ShouldBeTrue();
        Convert.FromBase64String(item["version"]!.GetValue<string>()).ShouldNotBeEmpty();
        var fetched = await GetJsonAsync(client, response.Headers.Location.OriginalString, ct);
        fetched["id"]!.GetValue<Guid>().ShouldBe(id);
    }

    [Fact]
    public async Task Create_records_the_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var userId = (await GetJsonAsync(client, "/api/auth/me", ct))["id"]!.GetValue<Guid>();

        var created = await CreateAsync(client, "AB-1", "Rice", "kg", ct);

        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
        var row = await inventory.Items.SingleAsync(i => i.Id == created["id"]!.GetValue<Guid>(), ct);
        row.CreatedBy.ShouldBe(userId);
        row.CreatedAt.ShouldNotBe(default);
    }

    [Theory]
    [InlineData(null, "N", "pc", "sku")]
    [InlineData("TOO-LONG-SKU", "N", "pc", "sku")]
    [InlineData("S", "", "pc", "name")]
    [InlineData("S", "TOO-LONG-NAME", "pc", "name")]
    [InlineData("S", "N", " ", "baseUnit")]
    [InlineData("S", "N", "TOO-LONG-UNIT", "baseUnit")]
    public async Task Create_validates_fields(string? sku, string name, string baseUnit, string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        // The "TOO-LONG-*" markers stand for a value one past the column limit.
        var response = await client.PostAsJsonAsync("/api/items", new { sku = Expand(sku), name = Expand(name), baseUnit = Expand(baseUnit) }, ct);

        await AssertValidationAsync(response, key, ct);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_sku_ignoring_case()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        await CreateAsync(client, "AB-1", "Rice", "kg", ct);

        var response = await client.PostAsJsonAsync("/api/items", new { sku = "ab-1", name = "Other", baseUnit = "kg" }, ct);

        await AssertValidationAsync(response, "sku", ct);
    }

    [Fact]
    public async Task Staff_gets_403_on_every_admin_route()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        var id = (await AddItemsAsync(app, tenant, ("RICE-1", "White rice", true)))[0];
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var before = await GetJsonAsync(client, $"/api/items/{id}", ct);

        var responses = new[]
        {
            await client.PostAsJsonAsync("/api/items", new { sku = "NEW-1", name = "New", baseUnit = "pc" }, ct),
            await client.PutAsJsonAsync($"/api/items/{id}", new { sku = "X-1", name = "Changed", baseUnit = "pc", version = before["version"]!.GetValue<string>() }, ct),
            await client.PostAsync($"/api/items/{id}/deactivate", null, ct),
        };

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            response.Headers.Location.ShouldBeNull();
        }

        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
        (await inventory.Items.CountAsync(ct)).ShouldBe(1);
        var item = await inventory.Items.SingleAsync(ct);
        item.IsActive.ShouldBeTrue();
        item.Name.ShouldBe("White rice");
        Convert.ToBase64String(item.RowVersion).ShouldBe(before["version"]!.GetValue<string>());
    }

    [Fact]
    public async Task Update_changes_fields_and_returns_a_new_version()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var created = await CreateAsync(client, "AB-1", "Rice", "kg", ct);
        var id = created["id"]!.GetValue<Guid>();
        var v1 = (await GetJsonAsync(client, $"/api/items/{id}", ct))["version"]!.GetValue<string>();

        var response = await client.PutAsJsonAsync($"/api/items/{id}", new { sku = "AB-2", name = "Brown rice", baseUnit = "SACK", version = v1 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var item = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        item["sku"]!.GetValue<string>().ShouldBe("AB-2");
        item["name"]!.GetValue<string>().ShouldBe("Brown rice");
        item["baseUnit"]!.GetValue<string>().ShouldBe("sack");
        item["version"]!.GetValue<string>().ShouldNotBe(v1);
    }

    [Fact]
    public async Task Update_with_a_stale_version_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var created = await CreateAsync(client, "AB-1", "Rice", "kg", ct);
        var id = created["id"]!.GetValue<Guid>();
        var v1 = created["version"]!.GetValue<string>();
        (await client.PutAsJsonAsync($"/api/items/{id}", new { sku = "AB-1", name = "First edit", baseUnit = "kg", version = v1 }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await client.PutAsJsonAsync($"/api/items/{id}", new { sku = "AB-1", name = "Second edit", baseUnit = "kg", version = v1 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/concurrency-conflict");
    }

    [Fact]
    public async Task Update_without_version_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "AB-1", "Rice", "kg", ct))["id"]!.GetValue<Guid>();

        var response = await client.PutAsJsonAsync($"/api/items/{id}", new { sku = "AB-1", name = "Rice", baseUnit = "kg" }, ct);

        await AssertValidationAsync(response, "version", ct);
    }

    [Fact]
    public async Task Update_to_another_items_sku_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        await CreateAsync(client, "AB-1", "Rice", "kg", ct);
        var second = await CreateAsync(client, "AB-2", "Oil", "l", ct);

        var response = await client.PutAsJsonAsync(
            $"/api/items/{second["id"]!.GetValue<Guid>()}",
            new { sku = "ab-1", name = "Oil", baseUnit = "l", version = second["version"]!.GetValue<string>() },
            ct);

        await AssertValidationAsync(response, "sku", ct);
    }

    [Fact]
    public async Task Update_and_deactivate_unknown_id_are_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var version = Convert.ToBase64String(new byte[8]);

        var update = await client.PutAsJsonAsync($"/api/items/{Guid.NewGuid()}", new { sku = "A", name = "N", baseUnit = "pc", version }, ct);
        var deactivate = await client.PostAsync($"/api/items/{Guid.NewGuid()}/deactivate", null, ct);

        foreach (var response in new[] { update, deactivate })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        }
    }

    [Fact]
    public async Task Deactivate_is_idempotent_and_hides_the_item_from_active_lists()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "AB-1", "Rice", "kg", ct))["id"]!.GetValue<Guid>();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await client.PostAsync($"/api/items/{id}/deactivate", null, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var item = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            item["isActive"]!.GetValue<bool>().ShouldBeFalse();
        }

        Skus(await GetJsonAsync(client, "/api/items?active=true", ct)).ShouldBeEmpty();
        Skus(await GetJsonAsync(client, "/api/items?active=false", ct)).ShouldBe(["AB-1"]);
    }

    [Fact]
    public async Task Concurrent_deactivations_all_succeed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "AB-1", "Rice", "kg", ct))["id"]!.GetValue<Guid>();

        // A double submit races on the rowversion; the loser must still see 200, not 409.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsync($"/api/items/{id}/deactivate", null, ct)));

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["isActive"]!.GetValue<bool>().ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Update_rejects_a_sku_change_once_the_item_has_a_movement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var created = await CreateAsync(client, "AB-1", "Rice", "kg", ct);
        var id = created["id"]!.GetValue<Guid>();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var store = await LedgerRows.AddLocationAsync(c, "BR-1", ct);
        await ReceiveAsync(c, id, store, ct);

        var response = await client.PutAsJsonAsync(
            $"/api/items/{id}", new { sku = "AB-2", name = "Rice", baseUnit = "kg", version = created["version"]!.GetValue<string>() }, ct);

        await AssertValidationAsync(response, "sku", ct);
        (await GetJsonAsync(client, $"/api/items/{id}", ct))["sku"]!.GetValue<string>().ShouldBe("AB-1");
    }

    [Fact]
    public async Task Update_allows_name_edits_after_a_movement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var created = await CreateAsync(client, "AB-1", "Rice", "kg", ct);
        var id = created["id"]!.GetValue<Guid>();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var store = await LedgerRows.AddLocationAsync(c, "BR-1", ct);
        await ReceiveAsync(c, id, store, ct);

        var response = await client.PutAsJsonAsync(
            $"/api/items/{id}", new { sku = "AB-1", name = "Brown rice", baseUnit = "kg", version = created["version"]!.GetValue<string>() }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var item = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        item["sku"]!.GetValue<string>().ShouldBe("AB-1");
        item["name"]!.GetValue<string>().ShouldBe("Brown rice");
    }

    [Fact]
    public async Task Sku_change_waits_for_an_uncommitted_first_movement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var created = await CreateAsync(client, "AB-1", "Rice", "kg", ct);
        var id = created["id"]!.GetValue<Guid>();
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        var store = await LedgerRows.AddLocationAsync(holder, "BR-1", ct);
        await using var tx = holder.BeginTransaction();
        await ReceiveAsync(holder, id, store, ct, tx);

        // Under RCSI a plain read skips the uncommitted movement; only the HOLDLOCK range lock makes the PUT wait for it.
        var put = client.PutAsJsonAsync(
            $"/api/items/{id}", new { sku = "AB-2", name = "Rice", baseUnit = "kg", version = created["version"]!.GetValue<string>() }, ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        tx.Commit();

        await AssertValidationAsync(await put, "sku", ct);
    }

    [Fact]
    public async Task Deactivate_is_422_while_a_physical_balance_is_not_zero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "AB-1", "Rice", "kg", ct))["id"]!.GetValue<Guid>();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var store = await LedgerRows.AddLocationAsync(c, "BR-1", ct);
        var supplier = await LedgerRows.LocationIdAsync(c, "SUP", ct);
        await LedgerRows.AddBalanceAsync(c, id, store, isVirtual: false, allowNegative: false, 5m, 500.0000m, ct);
        await LedgerRows.AddBalanceAsync(c, id, supplier, isVirtual: true, allowNegative: false, -5m, 0m, ct);

        var response = await client.PostAsync($"/api/items/{id}/deactivate", null, ct);

        var balances = await AssertStockNotZeroAsync(response, ct);
        var balance = balances.ShouldHaveSingleItem()!;
        balance["itemId"]!.GetValue<Guid>().ShouldBe(id);
        balance["locationId"]!.GetValue<Guid>().ShouldBe(store);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(5m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(500m);
        (await GetJsonAsync(client, $"/api/items/{id}", ct))["isActive"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Deactivate_ignores_virtual_and_zero_physical_balances()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "AB-1", "Rice", "kg", ct))["id"]!.GetValue<Guid>();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var store = await LedgerRows.AddLocationAsync(c, "BR-1", ct);
        var supplier = await LedgerRows.LocationIdAsync(c, "SUP", ct);
        await LedgerRows.AddBalanceAsync(c, id, store, isVirtual: false, allowNegative: false, 0m, 0m, ct);
        await LedgerRows.AddBalanceAsync(c, id, supplier, isVirtual: true, allowNegative: false, -5m, 0m, ct);

        var response = await client.PostAsync($"/api/items/{id}/deactivate", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["isActive"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Deactivate_is_422_for_a_negative_physical_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "AB-1", "Rice", "kg", ct))["id"]!.GetValue<Guid>();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var store = await LedgerRows.AddLocationAsync(c, "BR-1", ct, allowNegative: true);
        await LedgerRows.AddBalanceAsync(c, id, store, isVirtual: false, allowNegative: true, -2m, 0m, ct);

        var response = await client.PostAsync($"/api/items/{id}/deactivate", null, ct);

        var balance = (await AssertStockNotZeroAsync(response, ct)).ShouldHaveSingleItem()!;
        balance["locationId"]!.GetValue<Guid>().ShouldBe(store);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(-2m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(0m);
    }

    [Fact]
    public async Task Deactivate_waits_for_an_uncommitted_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "AB-1", "Rice", "kg", ct))["id"]!.GetValue<Guid>();
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        var store = await LedgerRows.AddLocationAsync(holder, "BR-1", ct);
        await using var tx = holder.BeginTransaction();
        await LedgerRows.AddBalanceAsync(holder, id, store, isVirtual: false, allowNegative: false, 5m, 500m, ct, tx);

        // Under RCSI a plain read skips the uncommitted balance; only the locking read makes the deactivate wait for it.
        var deactivate = client.PostAsync($"/api/items/{id}/deactivate", null, ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        tx.Commit();

        var balance = (await AssertStockNotZeroAsync(await deactivate, ct)).ShouldHaveSingleItem()!;
        balance["onHand"]!.GetValue<decimal>().ShouldBe(5m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(500m);
    }

    /// <summary>Asserts a <c>422 stock-not-zero</c> problem and returns its <c>balances</c>.</summary>
    internal static async Task<JsonArray> AssertStockNotZeroAsync(HttpResponseMessage response, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/stock-not-zero");
        problem["status"]!.GetValue<int>().ShouldBe(422);
        var balances = problem["balances"]!.AsArray();
        foreach (var balance in balances)
        {
            balance!.AsObject().Select(p => p.Key).ShouldBe(["itemId", "locationId", "onHand", "stockValue"], ignoreOrder: true);
        }

        return balances;
    }

    /// <summary>A receipt of 10 units at 1000.0000 from the Supplier to <paramref name="location"/>.</summary>
    private static async Task ReceiveAsync(SqlConnection c, Guid itemId, Guid location, CancellationToken ct, SqlTransaction? tx = null)
    {
        var supplier = await LedgerRows.LocationIdAsync(c, "SUP", ct, tx);
        var document = await LedgerRows.AddDocumentAsync(c, ct, tx);
        await LedgerRows.AddMovementAsync(c, document, 1, itemId, supplier, location, 10m, 1000m, ct, tx);
    }

    /// <summary>Inserts items for a tenant through its scope; the interceptor stamps <c>TenantId</c>. Returns the ids in order.</summary>
    internal static async Task<List<Guid>> AddItemsAsync(ApiFactory app, Guid tenant, params (string Sku, string Name, bool Active)[] items)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();

        var added = new List<(Item Item, bool Active)>();
        foreach (var (sku, name, active) in items)
        {
            var item = new Item { Sku = sku, Name = name, BaseUnit = "pc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            inventory.Items.Add(item);
            added.Add((item, active));
        }

        await inventory.SaveChangesAsync(ct);

        // IsActive has a database default of true, so a row is added active and deactivated afterwards.
        foreach (var (item, active) in added)
        {
            item.IsActive = active;
        }

        await inventory.SaveChangesAsync(ct);
        return added.Select(a => a.Item.Id).ToList();
    }

    /// <summary>Posts a valid create request and returns the 201 body.</summary>
    internal static async Task<JsonNode> CreateAsync(HttpClient client, string sku, string name, string baseUnit, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/items", new { sku, name, baseUnit }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static string? Expand(string? value) => value switch
    {
        "TOO-LONG-SKU" => new string('S', 65),
        "TOO-LONG-NAME" => new string('N', 201),
        "TOO-LONG-UNIT" => new string('u', 17),
        _ => value,
    };

    private static async Task AssertValidationAsync(HttpResponseMessage response, string key, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/validation");
        problem["errors"]!.AsObject().ContainsKey(key).ShouldBeTrue();
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        var response = await client.GetAsync(url, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static string[] Skus(JsonNode list) => list["items"]!.AsArray().Select(i => i!["sku"]!.GetValue<string>()).ToArray();
}