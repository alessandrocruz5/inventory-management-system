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
    public async Task Update_changes_code_and_name_and_ignores_allowNegativeStock()
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
        location["allowNegativeStock"]!.GetValue<bool>().ShouldBeFalse();
        location["version"]!.GetValue<string>().ShouldNotBe(v1);

        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
        var row = await inventory.Locations.SingleAsync(l => l.Id == id, ct);
        row.AllowNegativeStock.ShouldBeFalse();
        row.Code.ShouldBe("BR-2");
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

    /// <summary>Posts a valid create request and returns the 201 body.</summary>
    internal static async Task<JsonNode> CreateAsync(HttpClient client, string code, string name, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/locations", new { code, name }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
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