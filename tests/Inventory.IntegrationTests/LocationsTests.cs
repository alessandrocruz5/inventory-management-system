using System.Data;
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
using Platform.Tenants;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>Locations API (S4): any role may list; create, update and deactivate are Admin-only and touch physical locations only.</summary>
public sealed class LocationsTests(DatabaseFixture db)
{
    [Fact]
    public async Task List_shows_physical_first_then_the_three_system_locations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var staffEmail = await AddUserAsync(app, tenant, Roles.Staff);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        await CreateAsync(admin, "ZZ", "Last", ct);
        await CreateAsync(admin, "AA", "First", ct);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var list = await GetJsonAsync(staff, "/api/locations", ct);

        var rows = list["items"]!.AsArray().Select(i => i!).ToList();
        rows.Select(r => r["code"]!.GetValue<string>()).ShouldBe(["AA", "ZZ", "ADJ", "CUS", "SUP"]);
        rows.Select(r => r["kind"]!.GetValue<string>()).ShouldBe(["Physical", "Physical", "Adjustment", "Customer", "Supplier"]);
        rows.Select(r => r["isSystem"]!.GetValue<bool>()).ShouldBe([false, false, true, true, true]);
        list["total"]!.GetValue<int>().ShouldBe(5);
    }

    [Fact]
    public async Task Create_returns_201_with_location_and_trimmed_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var response = await client.PostAsJsonAsync("/api/locations", new { code = " BR-1 ", name = " Branch " }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var location = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var id = location["id"]!.GetValue<Guid>();
        response.Headers.Location!.OriginalString.ShouldBe($"/api/locations/{id}");
        location["code"]!.GetValue<string>().ShouldBe("BR-1");
        location["name"]!.GetValue<string>().ShouldBe("Branch");
        location["kind"]!.GetValue<string>().ShouldBe("Physical");
        location["isSystem"]!.GetValue<bool>().ShouldBeFalse();
        location["allowNegativeStock"]!.GetValue<bool>().ShouldBeFalse();
        location["isActive"]!.GetValue<bool>().ShouldBeTrue();
        Convert.FromBase64String(location["version"]!.GetValue<string>()).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(null, "N", "code")]
    [InlineData("TOO-LONG-CODE", "N", "code")]
    [InlineData("C", "", "name")]
    [InlineData("C", "TOO-LONG-NAME", "name")]
    public async Task Create_validates_fields(string? code, string name, string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        // The "TOO-LONG-*" markers stand for a value one past the column limit.
        var response = await client.PostAsJsonAsync("/api/locations", new { code = Expand(code), name = Expand(name) }, ct);

        await AssertValidationAsync(response, key, ct);
    }

    [Theory]
    [InlineData("SUP")]
    [InlineData("sup")]
    [InlineData("EARLIER")]
    public async Task Create_rejects_a_reserved_or_duplicate_code(string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        await CreateAsync(client, "EARLIER", "Created earlier", ct);

        var response = await client.PostAsJsonAsync("/api/locations", new { code, name = "Other" }, ct);

        await AssertValidationAsync(response, "code", ct);
    }

    [Fact]
    public async Task Update_changes_code_name_and_allowNegativeStock()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var created = await CreateAsync(client, "BR-1", "Branch", ct);
        var id = created["id"]!.GetValue<Guid>();
        var v1 = created["version"]!.GetValue<string>();

        var response = await client.PutAsJsonAsync(
            $"/api/locations/{id}",
            new { code = " BR-2 ", name = " Renamed ", version = v1, allowNegativeStock = true },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var location = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        location["code"]!.GetValue<string>().ShouldBe("BR-2");
        location["name"]!.GetValue<string>().ShouldBe("Renamed");
        location["allowNegativeStock"]!.GetValue<bool>().ShouldBeTrue();
        location["version"]!.GetValue<string>().ShouldNotBe(v1);

        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
        var row = await inventory.Locations.SingleAsync(l => l.Id == id, ct);
        row.AllowNegativeStock.ShouldBeTrue();
        row.Code.ShouldBe("BR-2");
    }

    [Fact]
    public async Task Toggle_on_and_off_mirrors_every_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();
        var first = await PostingTests.NewItemAsync(client, "AB-1", ct);
        var second = await PostingTests.NewItemAsync(client, "AB-2", ct);
        await PostingTests.ReceiveAsync(client, id, first, 3m, 10m, ct);
        await PostingTests.ReceiveAsync(client, id, second, 5m, 20m, ct);
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var sup = await LedgerRows.LocationIdAsync(c, "SUP", ct);

        (await AllowNegativeAsync(client, id, true, ct))["allowNegativeStock"]!.GetValue<bool>().ShouldBeTrue();

        (await AllowNegativeFlagsAsync(c, id, ct)).ShouldBe([true, true]);
        (await AllowNegativeFlagsAsync(c, sup, ct)).ShouldAllBe(flag => !flag);

        (await AllowNegativeAsync(client, id, false, ct))["allowNegativeStock"]!.GetValue<bool>().ShouldBeFalse();

        (await AllowNegativeFlagsAsync(c, id, ct)).ShouldBe([false, false]);
        var supFlags = await AllowNegativeFlagsAsync(c, sup, ct);
        supFlags.Count.ShouldBe(2);
        supFlags.ShouldAllBe(flag => !flag);
    }

    [Fact]
    public async Task Toggle_off_over_a_negative_balance_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();
        var item = await PostingTests.NewItemAsync(client, "AB-1", ct);
        await AllowNegativeAsync(client, id, true, ct);
        await PostingTests.ReceiveAsync(client, id, item, 4m, 50m, ct);
        (await PostingTests.PostDocumentAsync(
                client, PostingTests.Issues, PostingTests.NewKey(), PostingTests.Issue(id, PostingTests.IssueLine(item, 6m)), ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var current = await ListedAsync(client, id, ct);
        var version = current["version"]!.GetValue<string>();

        var response = await client.PutAsJsonAsync(
            $"/api/locations/{id}",
            new { code = "BR-1", name = "Branch", version, allowNegativeStock = false },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/negative-balances-exist");
        problem["status"]!.GetValue<int>().ShouldBe(422);
        var balance = problem["balances"]!.AsArray().ShouldHaveSingleItem()!;
        balance.AsObject().Select(p => p.Key).ShouldBe(["itemId", "locationId", "onHand", "stockValue"], ignoreOrder: true);
        balance["itemId"]!.GetValue<Guid>().ShouldBe(item);
        balance["locationId"]!.GetValue<Guid>().ShouldBe(id);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(-2m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(-100m);

        var after = await ListedAsync(client, id, ct);
        after["allowNegativeStock"]!.GetValue<bool>().ShouldBeTrue();
        after["version"]!.GetValue<string>().ShouldBe(version);
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        (await AllowNegativeFlagsAsync(c, id, ct)).ShouldBe([true]);
    }

    [Fact]
    public async Task Absent_allowNegativeStock_leaves_the_toggle_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();
        var on = await AllowNegativeAsync(client, id, true, ct);

        var response = await client.PutAsJsonAsync(
            $"/api/locations/{id}",
            new { code = "BR-1", name = "Renamed", version = on["version"]!.GetValue<string>() },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var location = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        location["name"]!.GetValue<string>().ShouldBe("Renamed");
        location["allowNegativeStock"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Toggle_with_a_stale_version_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var created = await CreateAsync(client, "BR-1", "Branch", ct);
        var id = created["id"]!.GetValue<Guid>();
        var v1 = created["version"]!.GetValue<string>();
        var item = await PostingTests.NewItemAsync(client, "AB-1", ct);
        await PostingTests.ReceiveAsync(client, id, item, 3m, 10m, ct);
        (await client.PutAsJsonAsync($"/api/locations/{id}", new { code = "BR-1", name = "Branch", version = v1, allowNegativeStock = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await client.PutAsJsonAsync(
            $"/api/locations/{id}",
            new { code = "BR-1", name = "Branch", version = v1, allowNegativeStock = false },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["type"]!.GetValue<string>().ShouldBe("/problems/concurrency-conflict");
        (await ListedAsync(client, id, ct))["allowNegativeStock"]!.GetValue<bool>().ShouldBeTrue();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        (await AllowNegativeFlagsAsync(c, id, ct)).ShouldBe([true]);
    }

    [Fact]
    public async Task Update_to_another_locations_code_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        await CreateAsync(client, "BR-1", "Branch", ct);
        var second = await CreateAsync(client, "BR-2", "Other", ct);

        var response = await client.PutAsJsonAsync(
            $"/api/locations/{second["id"]!.GetValue<Guid>()}",
            new { code = "br-1", name = "Other", version = second["version"]!.GetValue<string>() },
            ct);

        await AssertValidationAsync(response, "code", ct);
    }

    [Fact]
    public async Task Update_with_a_stale_version_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var created = await CreateAsync(client, "BR-1", "Branch", ct);
        var id = created["id"]!.GetValue<Guid>();
        var v1 = created["version"]!.GetValue<string>();
        (await client.PutAsJsonAsync($"/api/locations/{id}", new { code = "BR-1", name = "First edit", version = v1 }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await client.PutAsJsonAsync($"/api/locations/{id}", new { code = "BR-1", name = "Second edit", version = v1 }, ct);

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
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();

        var response = await client.PutAsJsonAsync($"/api/locations/{id}", new { code = "BR-1", name = "Branch" }, ct);

        await AssertValidationAsync(response, "version", ct);
    }

    [Fact]
    public async Task Update_and_deactivate_unknown_id_are_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var version = Convert.ToBase64String(new byte[8]);

        var update = await client.PutAsJsonAsync($"/api/locations/{Guid.NewGuid()}", new { code = "A", name = "N", version }, ct);
        var deactivate = await client.PostAsync($"/api/locations/{Guid.NewGuid()}/deactivate", null, ct);

        foreach (var response in new[] { update, deactivate })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            problem["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        }
    }

    [Fact]
    public async Task System_locations_cannot_be_updated_or_deactivated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var sup = (await GetJsonAsync(client, "/api/locations", ct))["items"]!.AsArray().Single(i => i!["code"]!.GetValue<string>() == "SUP")!;
        var id = sup["id"]!.GetValue<Guid>();
        var version = sup["version"]!.GetValue<string>();

        var update = await client.PutAsJsonAsync($"/api/locations/{id}", new { code = "SUP2", name = "Changed", version }, ct);
        var deactivate = await client.PostAsync($"/api/locations/{id}/deactivate", null, ct);

        await AssertValidationAsync(update, "location", ct);
        await AssertValidationAsync(deactivate, "location", ct);
        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
        var row = await inventory.Locations.SingleAsync(l => l.Id == id, ct);
        row.Code.ShouldBe("SUP");
        row.Name.ShouldBe("Supplier");
        row.IsActive.ShouldBeTrue();
        Convert.ToBase64String(row.RowVersion).ShouldBe(version);
    }

    [Fact]
    public async Task Deactivate_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await client.PostAsync($"/api/locations/{id}/deactivate", null, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var location = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            location["isActive"]!.GetValue<bool>().ShouldBeFalse();
        }

        var listed = (await GetJsonAsync(client, "/api/locations", ct))["items"]!.AsArray().Single(i => i!["id"]!.GetValue<Guid>() == id)!;
        listed["isActive"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_deactivations_all_succeed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();

        // A double submit races on the rowversion; the loser must still see 200, not 409.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsync($"/api/locations/{id}/deactivate", null, ct)));

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["isActive"]!.GetValue<bool>().ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Deactivate_is_422_while_a_balance_is_not_zero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var stocked = await LedgerRows.AddItemAsync(c, "AB-1", ct);
        var empty = await LedgerRows.AddItemAsync(c, "AB-2", ct);
        await LedgerRows.AddBalanceAsync(c, stocked, id, isVirtual: false, allowNegative: false, 3m, 30m, ct);
        await LedgerRows.AddBalanceAsync(c, empty, id, isVirtual: false, allowNegative: false, 0m, 0m, ct);

        var response = await client.PostAsync($"/api/locations/{id}/deactivate", null, ct);

        var balance = (await ItemsTests.AssertStockNotZeroAsync(response, ct)).ShouldHaveSingleItem()!;
        balance["itemId"]!.GetValue<Guid>().ShouldBe(stocked);
        balance["locationId"]!.GetValue<Guid>().ShouldBe(id);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(3m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(30m);
        var listed = (await GetJsonAsync(client, "/api/locations", ct))["items"]!.AsArray().Single(i => i!["id"]!.GetValue<Guid>() == id)!;
        listed["isActive"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Deactivate_succeeds_when_every_balance_is_zero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();
        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        var item = await LedgerRows.AddItemAsync(c, "AB-1", ct);
        await LedgerRows.AddBalanceAsync(c, item, id, isVirtual: false, allowNegative: false, 0m, 0m, ct);

        var response = await client.PostAsync($"/api/locations/{id}/deactivate", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["isActive"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Deactivate_waits_for_an_uncommitted_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        var item = await LedgerRows.AddItemAsync(holder, "AB-1", ct);
        await using var tx = holder.BeginTransaction();
        await LedgerRows.AddBalanceAsync(holder, item, id, isVirtual: false, allowNegative: false, 5m, 500m, ct, tx);

        // Under RCSI a plain read skips the uncommitted balance; only the locking read makes the deactivate wait for it.
        var deactivate = client.PostAsync($"/api/locations/{id}/deactivate", null, ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        tx.Commit();

        var balance = (await ItemsTests.AssertStockNotZeroAsync(await deactivate, ct)).ShouldHaveSingleItem()!;
        balance["itemId"]!.GetValue<Guid>().ShouldBe(item);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(5m);
    }

    [Fact]
    public async Task Deactivate_on_zero_stock_holds_off_postings_until_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();
        var item = (await ItemsTests.CreateAsync(client, "AB-1", "Rice", "kg", ct))["id"]!.GetValue<Guid>();
        await using var probe = await LedgerRows.OpenAsync(db, tenant, ct);
        var second = await LedgerRows.AddItemAsync(probe, "AB-2", ct);
        var other = await LedgerRows.AddItemAsync(probe, "AB-3", ct);
        await LedgerRows.AddBalanceAsync(probe, item, id, isVirtual: false, allowNegative: false, 0m, 0m, ct);
        await LedgerRows.AddBalanceAsync(probe, second, id, isVirtual: false, allowNegative: false, 0m, 0m, ct);

        // The holder X-locks the location row, so the deactivate pauses in SaveChanges after taking its balance locks.
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await ItemsTests.ExecuteAsync(holder, tx, "UPDATE inventory.Locations SET Name = Name WHERE Id = @id", ("@id", id));
        var deactivate = client.PostAsync($"/api/locations/{id}/deactivate", null, ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);

        // Posting step 6 locks a zero balance, and a first posting of another item here inserts into the locked range.
        await ItemsTests.ExecuteAsync(probe, null, "SET LOCK_TIMEOUT 500;");
        await ItemsTests.AssertLockTimeoutAsync(probe, """
            SELECT OnHand FROM inventory.StockBalances WITH (UPDLOCK, ROWLOCK) WHERE ItemId = @i AND LocationId = @l
            """, ("@i", item), ("@l", id));
        await ItemsTests.AssertRangeLockedAsync(db, probe, other, id);
        tx.Rollback();

        var response = await deactivate;
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["isActive"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Toggle_off_waits_for_an_uncommitted_negative_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();
        var version = (await AllowNegativeAsync(client, id, true, ct))["version"]!.GetValue<string>();
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        var item = await LedgerRows.AddItemAsync(holder, "AB-1", ct);
        await using var tx = holder.BeginTransaction();
        await LedgerRows.AddBalanceAsync(holder, item, id, isVirtual: false, allowNegative: true, -2m, -100m, ct, tx);

        // Under RCSI a plain read skips the uncommitted balance; only the locking read makes the toggle wait for it.
        var toggle = client.PutAsJsonAsync(
            $"/api/locations/{id}",
            new { code = "BR-1", name = "Branch", version, allowNegativeStock = false },
            ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        tx.Commit();

        var response = await toggle;
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/negative-balances-exist");
        var balance = problem["balances"]!.AsArray().ShouldHaveSingleItem()!;
        balance["itemId"]!.GetValue<Guid>().ShouldBe(item);
        balance["locationId"]!.GetValue<Guid>().ShouldBe(id);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(-2m);
    }

    [Fact]
    public async Task Toggle_off_holds_off_postings_until_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = (await CreateAsync(client, "BR-1", "Branch", ct))["id"]!.GetValue<Guid>();
        await AllowNegativeAsync(client, id, true, ct);
        var item = (await ItemsTests.CreateAsync(client, "AB-1", "Rice", "kg", ct))["id"]!.GetValue<Guid>();
        await PostingTests.ReceiveAsync(client, id, item, 1m, 10m, ct);
        var version = (await ListedAsync(client, id, ct))["version"]!.GetValue<string>();
        await using var probe = await LedgerRows.OpenAsync(db, tenant, ct);
        var other = await LedgerRows.AddItemAsync(probe, "AB-2", ct);

        // The holder X-locks the location row, so the toggle pauses in SaveChanges after taking its balance locks.
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await ItemsTests.ExecuteAsync(holder, tx, "UPDATE inventory.Locations SET Name = Name WHERE Id = @id", ("@id", id));
        var toggle = client.PutAsJsonAsync(
            $"/api/locations/{id}",
            new { code = "BR-1", name = "Branch", version, allowNegativeStock = false },
            ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);

        // Posting step 6 locks the item's balance, and a first posting of another item here inserts into the locked range.
        await ItemsTests.ExecuteAsync(probe, null, "SET LOCK_TIMEOUT 500;");
        await ItemsTests.AssertLockTimeoutAsync(probe, """
            SELECT OnHand FROM inventory.StockBalances WITH (UPDLOCK, ROWLOCK) WHERE ItemId = @i AND LocationId = @l
            """, ("@i", item), ("@l", id));
        await ItemsTests.AssertRangeLockedAsync(db, probe, other, id);

        // The issue either waits at step 6 behind the toggle or starts after it commits; either way step 7 sees the change.
        var issue = PostingTests.PostDocumentAsync(
            client, PostingTests.Issues, PostingTests.NewKey(), PostingTests.Issue(id, PostingTests.IssueLine(item, 3m)), ct);
        tx.Rollback();

        var response = await toggle;
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["allowNegativeStock"]!.GetValue<bool>().ShouldBeFalse();
        await PostingTests.AssertInsufficientStockAsync(await issue, item, id, available: 1m, requested: 3m, ct);

        await using var c = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(c, item, id, ct)).ShouldBe((1m, 10m));
        (await AllowNegativeFlagsAsync(c, id, ct)).ShouldBe([false]);
        await LedgerInvariants.AssertHoldAsync(c, ct);
    }

    [Fact]
    public async Task Staff_gets_403_on_every_admin_route()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var staffEmail = await AddUserAsync(app, tenant, Roles.Staff);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var created = await CreateAsync(admin, "BR-1", "Branch", ct);
        var id = created["id"]!.GetValue<Guid>();
        var version = created["version"]!.GetValue<string>();
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var responses = new[]
        {
            await staff.PostAsJsonAsync("/api/locations", new { code = "NEW-1", name = "New" }, ct),
            await staff.PutAsJsonAsync($"/api/locations/{id}", new { code = "X-1", name = "Changed", version }, ct),
            await staff.PostAsync($"/api/locations/{id}/deactivate", null, ct),
        };

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            response.Headers.Location.ShouldBeNull();
        }

        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
        (await inventory.Locations.CountAsync(l => l.Kind == LocationKind.Physical, ct)).ShouldBe(1);
        var row = await inventory.Locations.SingleAsync(l => l.Id == id, ct);
        row.IsActive.ShouldBeTrue();
        row.Name.ShouldBe("Branch");
        Convert.ToBase64String(row.RowVersion).ShouldBe(version);
    }

    [Fact]
    public async Task Staff_gets_403_when_flipping_the_toggle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var staffEmail = await AddUserAsync(app, tenant, Roles.Staff);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var created = await CreateAsync(admin, "BR-1", "Branch", ct);
        var id = created["id"]!.GetValue<Guid>();
        var version = created["version"]!.GetValue<string>();
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var response = await staff.PutAsJsonAsync(
            $"/api/locations/{id}",
            new { code = "BR-1", name = "Branch", version, allowNegativeStock = true },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Location.ShouldBeNull();
        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
        var row = await inventory.Locations.SingleAsync(l => l.Id == id, ct);
        row.AllowNegativeStock.ShouldBeFalse();
        Convert.ToBase64String(row.RowVersion).ShouldBe(version);
    }

    /// <summary>Posts a valid create request and returns the 201 body.</summary>
    [Fact]
    public async Task Another_tenants_location_cannot_be_toggled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, emailA) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, emailB) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var clientA = await DatabaseFixture.SignInAsync(app, emailA);
        using var clientB = await DatabaseFixture.SignInAsync(app, emailB);
        var id = (await CreateAsync(clientB, "BR-B", "Branch B", ct))["id"]!.GetValue<Guid>();
        var item = await PostingTests.NewItemAsync(clientB, "B-1", ct);
        await PostingTests.ReceiveAsync(clientB, id, item, 3m, 10m, ct);
        var version = (await GetJsonAsync(clientB, "/api/locations?page=1&pageSize=100", ct))["items"]!.AsArray()
            .Single(i => i!["id"]!.GetValue<Guid>() == id)!["version"]!.GetValue<string>();

        var response = await clientA.PutAsJsonAsync(
            $"/api/locations/{id}",
            new { code = "BR-B", name = "Branch B", version, allowNegativeStock = true },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await using var c = await LedgerRows.OpenAsync(db, b, ct);
        var flags = await AllowNegativeFlagsAsync(c, id, ct);
        flags.ShouldNotBeEmpty();
        flags.ShouldAllBe(flag => !flag);
    }

    internal static async Task<JsonNode> CreateAsync(HttpClient client, string code, string name, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/locations", new { code, name }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    /// <summary>Sets a location's negative-stock toggle from its listed code, name and version; asserts 200 and returns the body.</summary>
    internal static async Task<JsonNode> AllowNegativeAsync(HttpClient client, Guid id, bool allow, CancellationToken ct)
    {
        var location = await ListedAsync(client, id, ct);
        var response = await client.PutAsJsonAsync(
            $"/api/locations/{id}",
            new
            {
                code = location["code"]!.GetValue<string>(),
                name = location["name"]!.GetValue<string>(),
                version = location["version"]!.GetValue<string>(),
                allowNegativeStock = allow,
            },
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    /// <summary>The location's row from the first list page.</summary>
    private static async Task<JsonNode> ListedAsync(HttpClient client, Guid id, CancellationToken ct) =>
        (await GetJsonAsync(client, "/api/locations?page=1&pageSize=100", ct))["items"]!.AsArray()
            .Single(i => i!["id"]!.GetValue<Guid>() == id)!;

    /// <summary>Every balance's <c>AllowNegative</c> at a location, by item.</summary>
    private static async Task<List<bool>> AllowNegativeFlagsAsync(SqlConnection c, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT AllowNegative FROM inventory.StockBalances WHERE LocationId = @id ORDER BY ItemId", c);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = location });
        await using var reader = await command.ExecuteReaderAsync(ct);
        var flags = new List<bool>();
        while (await reader.ReadAsync(ct))
        {
            flags.Add(reader.GetBoolean(0));
        }

        return flags;
    }

    /// <summary>Adds another user to an existing tenant and returns their email.</summary>
    private static async Task<string> AddUserAsync(ApiFactory app, Guid tenant, string role)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        await using var scope = app.Services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<TenantProvisioner>();
        await provisioner.EnsureUserAsync(tenant, email, role, DatabaseFixture.UserPassword, CancellationToken.None);
        return email;
    }

    private static string? Expand(string? value) => value switch
    {
        "TOO-LONG-CODE" => new string('C', 33),
        "TOO-LONG-NAME" => new string('N', 201),
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
}