using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>Item units API (IMS-28): create and deactivate (Admin), and the <c>units</c> list on GET /items/{id}.</summary>
public sealed class ItemUnitsTests(DatabaseFixture db)
{
    [Fact]
    public async Task Create_returns_201_and_get_lists_the_unit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = await PostingTests.NewItemAsync(client, "RICE-1", ct);

        var response = await client.PostAsJsonAsync($"/api/items/{id}/units", new { code = " Case ", factor = 12 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldEndWith($"/api/items/{id}/units/case");
        var created = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        created["code"]!.GetValue<string>().ShouldBe("case");
        created["factor"]!.GetValue<decimal>().ShouldBe(12m);
        created["isActive"]!.GetValue<bool>().ShouldBeTrue();
        var units = (await GetItemAsync(client, id, ct))["units"]!.AsArray();
        units.Count.ShouldBe(1);
        units[0]!["code"]!.GetValue<string>().ShouldBe("case");
        units[0]!["factor"]!.GetValue<decimal>().ShouldBe(12m);
        units[0]!["isActive"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "12", "code")]
    [InlineData("TOO-LONG-CODE", "12", "code")]
    [InlineData("case", null, "factor")]
    [InlineData("case", "0", "factor")]
    [InlineData("case", "-1", "factor")]
    [InlineData("case", "1.00001", "factor")]
    [InlineData("case", "1000000000", "factor")]
    public async Task Create_validates(string? code, string? factor, string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        code = code == "TOO-LONG-CODE" ? new string('c', 17) : code;
        // The factor is parsed as a JSON number, so 1.00001 reaches the server exactly as written.
        var body = new JsonObject { ["code"] = code, ["factor"] = factor is null ? null : JsonNode.Parse(factor) };

        var response = await client.PostAsJsonAsync($"/api/items/{id}/units", body, ct);

        await AssertValidationAsync(response, key, ct);
        (await GetItemAsync(client, id, ct))["units"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_rejects_the_base_unit_and_duplicates_ignoring_case()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = await PostingTests.NewItemAsync(client, "RICE-1", ct);

        await AssertValidationAsync(await client.PostAsJsonAsync($"/api/items/{id}/units", new { code = "pc", factor = 1 }, ct), "code", ct);
        (await client.PostAsJsonAsync($"/api/items/{id}/units", new { code = "case", factor = 12 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await AssertValidationAsync(await client.PostAsJsonAsync($"/api/items/{id}/units", new { code = "CASE", factor = 6 }, ct), "code", ct);

        (await client.PostAsync($"/api/items/{id}/units/case/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // An inactive code stays reserved.
        await AssertValidationAsync(await client.PostAsJsonAsync($"/api/items/{id}/units", new { code = "case", factor = 12 }, ct), "code", ct);
        (await GetItemAsync(client, id, ct))["units"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deactivate_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await client.PostAsJsonAsync($"/api/items/{id}/units", new { code = "case", factor = 12 }, ct);

        var first = await client.PostAsync($"/api/items/{id}/units/CASE/deactivate", null, ct);
        var second = await client.PostAsync($"/api/items/{id}/units/case/deactivate", null, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!["isActive"]!.GetValue<bool>().ShouldBeFalse();
        (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!["isActive"]!.GetValue<bool>().ShouldBeFalse();
        var units = (await GetItemAsync(client, id, ct))["units"]!.AsArray();
        units.Count.ShouldBe(1);
        units[0]!["isActive"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_item_or_unit_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var id = await PostingTests.NewItemAsync(client, "RICE-1", ct);

        var responses = new[]
        {
            await client.PostAsJsonAsync($"/api/items/{Guid.NewGuid()}/units", new { code = "case", factor = 12 }, ct),
            await client.PostAsync($"/api/items/{Guid.NewGuid()}/units/case/deactivate", null, ct),
            await client.PostAsync($"/api/items/{id}/units/nope/deactivate", null, ct),
        };

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["type"]!.GetValue<string>().ShouldBe("/problems/not-found");
        }
    }

    [Fact]
    public async Task Staff_gets_403_on_unit_routes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var id = await PostingTests.NewItemAsync(admin, "RICE-1", ct);
        await admin.PostAsJsonAsync($"/api/items/{id}/units", new { code = "case", factor = 12 }, ct);
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var responses = new[]
        {
            await staff.PostAsJsonAsync($"/api/items/{id}/units", new { code = "box", factor = 6 }, ct),
            await staff.PostAsync($"/api/items/{id}/units/case/deactivate", null, ct),
        };

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            response.Headers.Location.ShouldBeNull();
        }

        await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);
        var units = await scope.Services.GetRequiredService<InventoryDbContext>().ItemUnits.AsNoTracking().ToListAsync(ct);
        units.Count.ShouldBe(1);
        units[0].Code.ShouldBe("case");
        units[0].IsActive.ShouldBeTrue();
    }

    private static async Task<JsonNode> GetItemAsync(HttpClient client, Guid id, CancellationToken ct)
    {
        var response = await client.GetAsync($"/api/items/{id}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    private static async Task AssertValidationAsync(HttpResponseMessage response, string key, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/validation");
        problem["errors"]!.AsObject().ContainsKey(key).ShouldBeTrue();
    }
}