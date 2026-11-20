using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Assistant.Drafts;
using Assistant.Tools;

using Inventory.Features.Posting;

using Kernel.Tenancy;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>The assistant's draft_document tool, called inside a tenant scope. It validates and collects; nothing is posted.</summary>
public sealed class AssistantDraftTests(DatabaseFixture db)
{
    private static async Task<(Guid Tenant, HttpClient Client)> StartAsync(ApiFactory app)
    {
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        return (tenant, await DatabaseFixture.SignInAsync(app, email));
    }

    private static ITenantScope Scope(ApiFactory app, Guid tenant) =>
        app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant);

    private static DraftLineRequest L(string? sku, decimal? quantity = 1m, string? unit = null, decimal? unitCost = null, string? direction = null) =>
        new(sku, quantity, unit, unitCost, direction);

    private static async Task<JsonNode> DraftAsync(
        ITenantScope scope,
        string role,
        string type,
        DraftLineRequest[] lines,
        CancellationToken ct,
        string? location = null,
        string? fromLocation = null,
        string? toLocation = null,
        string? reasonCode = null,
        string? reference = null,
        string? notes = null)
    {
        var tool = scope.Services.GetRequiredService<ToolCatalog>().For(role).OfType<AIFunction>().Single(f => f.Name == "draft_document");
        var args = new AIFunctionArguments
        {
            ["type"] = type,
            ["lines"] = lines,
            ["location"] = location,
            ["fromLocation"] = fromLocation,
            ["toLocation"] = toLocation,
            ["reasonCode"] = reasonCode,
            ["reference"] = reference,
            ["notes"] = notes,
        };

        var result = await tool.InvokeAsync(args, ct);
        return result switch
        {
            JsonElement element => JsonNode.Parse(element.GetRawText())!,
            _ => JsonSerializer.SerializeToNode(result, ToolJson.Options)!,
        };
    }

    private static string[] Errors(JsonNode result) =>
        [.. result["errors"]!.AsArray().Select(e => e!.GetValue<string>())];

    private static async Task<int> DocumentTotalAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/documents", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["total"]!.GetValue<int>();
    }

    [Fact]
    public async Task A_receipt_in_sacks_becomes_a_draft()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var location = await PostingTests.NewLocationAsync(client, ct, "WH-1");
        var item = await PostingTests.NewItemAsync(client, "RICE-25", ct);
        (await client.PostAsJsonAsync($"/api/items/{item}/units", new { code = "sack", factor = 25 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var before = await DocumentTotalAsync(client, ct);

        await using var scope = Scope(app, tenant);
        var result = await DraftAsync(scope, RoleNames.Staff, "Receipt", [L("RICE-25", 2m, "SACK", 1250m)], ct, location: "wh-1");

        result["ok"]!.GetValue<bool>().ShouldBeTrue();
        result["draft"]!.GetValue<int>().ShouldBe(1);
        result["summary"]!.GetValue<string>().ShouldContain("Not posted");
        var draft = scope.Services.GetRequiredService<DraftCollector>().Drafts.ShouldHaveSingleItem();
        draft.Type.ShouldBe("receipt");
        draft.Location.ShouldNotBeNull().Id.ShouldBe(location);
        draft.Location.Code.ShouldBe("WH-1");
        draft.Location.Name.ShouldBe("Warehouse");
        var line = draft.Lines.ShouldHaveSingleItem();
        line.ItemId.ShouldBe(item);
        line.Sku.ShouldBe("RICE-25");
        line.ItemName.ShouldBe("RICE-25");
        line.BaseUnit.ShouldBe("pc");
        line.Unit.ShouldBe("sack");
        line.Quantity.ShouldBe(2m);
        line.UnitCost.ShouldBe(1250m);
        (await DocumentTotalAsync(client, ct)).ShouldBe(before);
    }

    [Fact]
    public async Task Only_an_admin_drafts_an_adjustment()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        await PostingTests.NewLocationAsync(client, ct, "WH-1");
        await PostingTests.NewItemAsync(client, "X-1", ct);

        await using var scope = Scope(app, tenant);
        var staff = await DraftAsync(scope, RoleNames.Staff, "adjustment", [L("X-1", 1m, direction: "remove")], ct, location: "WH-1", reasonCode: "Damaged");
        scope.Services.GetRequiredService<DraftCollector>().Drafts.ShouldBeEmpty();
        var admin = await DraftAsync(scope, RoleNames.Admin, "adjustment", [L("X-1", 1m, direction: "remove")], ct, location: "WH-1", reasonCode: "damaged");

        staff["ok"]!.GetValue<bool>().ShouldBeFalse();
        Errors(staff).ShouldBe(["type: Only an Admin can correct stock."]);
        admin["ok"]!.GetValue<bool>().ShouldBeTrue();
        var draft = scope.Services.GetRequiredService<DraftCollector>().Drafts.ShouldHaveSingleItem();
        draft.ReasonCode.ShouldBe("Damaged");
        draft.Lines[0].Direction.ShouldBe("remove");
    }

    [Fact]
    public async Task Foreign_inactive_and_virtual_references_are_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenantA, clientA) = await StartAsync(app);
        using var _a = clientA;
        var (_, clientB) = await StartAsync(app);
        using var _b = clientB;
        await PostingTests.NewLocationAsync(clientA, ct, "A-LOC");
        var dead = await PostingTests.NewItemAsync(clientA, "A-DEAD", ct);
        await PostingTests.NewItemAsync(clientA, "A-LIVE", ct);
        var closed = await PostingTests.NewLocationAsync(clientA, ct, "A-OLD");
        (await clientA.PostAsync($"/api/items/{dead}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await clientA.PostAsync($"/api/locations/{closed}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await PostingTests.NewLocationAsync(clientB, ct, "B-LOC");
        await PostingTests.NewItemAsync(clientB, "B-ITEM", ct);

        await using var scope = Scope(app, tenantA);
        var foreignSku = await DraftAsync(scope, RoleNames.Staff, "receipt", [L("B-ITEM", 1m, unitCost: 1m)], ct, location: "A-LOC");
        var foreignLocation = await DraftAsync(scope, RoleNames.Staff, "receipt", [L("A-LIVE", 1m, unitCost: 1m)], ct, location: "B-LOC");
        var inactiveItem = await DraftAsync(scope, RoleNames.Staff, "receipt", [L("A-DEAD", 1m, unitCost: 1m)], ct, location: "A-LOC");
        var supplier = await DraftAsync(scope, RoleNames.Staff, "receipt", [L("A-LIVE", 1m, unitCost: 1m)], ct, location: "SUP");
        var inactiveLocation = await DraftAsync(scope, RoleNames.Staff, "receipt", [L("A-LIVE", 1m, unitCost: 1m)], ct, location: "A-OLD");

        Errors(foreignSku).ShouldBe(["lines[0].sku: No active item with SKU B-ITEM."]);
        Errors(foreignLocation).ShouldBe(["location: No active location with code B-LOC."]);
        Errors(inactiveItem).ShouldBe(["lines[0].sku: No active item with SKU A-DEAD."]);
        Errors(supplier).ShouldBe(["location: No active location with code SUP."]);
        Errors(inactiveLocation).ShouldBe(["location: No active location with code A-OLD."]);
        scope.Services.GetRequiredService<DraftCollector>().Drafts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("qty-zero", "lines[0].quantity")]
    [InlineData("qty-negative", "lines[0].quantity")]
    [InlineData("qty-five-decimals", "lines[0].quantity")]
    [InlineData("qty-over-max", "lines[0].quantity")]
    [InlineData("receipt-no-cost", "lines[0].unitCost")]
    [InlineData("issue-with-cost", "lines[0].unitCost")]
    [InlineData("unknown-unit", "lines[0].unit")]
    [InlineData("duplicate-sku", "lines[1].sku")]
    [InlineData("lines-101", "lines")]
    [InlineData("transfer-same", "toLocation")]
    [InlineData("adjustment-no-reason", "reasonCode")]
    [InlineData("adjustment-bad-direction", "lines[0].direction")]
    [InlineData("reference-101", "reference")]
    [InlineData("notes-501", "notes")]
    [InlineData("type-sale", "type")]
    public async Task Line_rules_match_posting(string name, string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        await PostingTests.NewLocationAsync(client, ct, "WH-1");
        await PostingTests.NewLocationAsync(client, ct, "WH-2");
        await PostingTests.NewItemAsync(client, "X-1", ct);

        var (type, lines, args) = name switch
        {
            "qty-zero" => ("issue", [L("X-1", 0m)], new Header()),
            "qty-negative" => ("issue", [L("X-1", -1m)], new Header()),
            "qty-five-decimals" => ("issue", [L("X-1", 1.00001m)], new Header()),
            "qty-over-max" => ("issue", [L("X-1", DraftLimits.MaxAmount + 0.0001m)], new Header()),
            "receipt-no-cost" => ("receipt", [L("X-1")], new Header()),
            "issue-with-cost" => ("issue", [L("X-1", 1m, unitCost: 5m)], new Header()),
            "unknown-unit" => ("issue", [L("X-1", 1m, "crate")], new Header()),
            "duplicate-sku" => ("issue", [L("X-1"), L("x-1")], new Header()),
            "lines-101" => ("issue", Enumerable.Range(0, 101).Select(_ => L("X-1")).ToArray(), new Header()),
            "transfer-same" => ("transfer", [L("X-1")], new Header(Location: null, From: "WH-1", To: "wh-1")),
            "adjustment-no-reason" => ("adjustment", [L("X-1", 1m, direction: "remove")], new Header()),
            "adjustment-bad-direction" => ("adjustment", [L("X-1", 1m, direction: "up")], new Header(Reason: "Lost")),
            "reference-101" => ("issue", [L("X-1")], new Header(Reference: new string('r', 101))),
            "notes-501" => ("issue", [L("X-1")], new Header(Notes: new string('n', 501))),
            "type-sale" => ("sale", [L("X-1")], new Header()),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        await using var scope = Scope(app, tenant);
        var result = await DraftAsync(scope, RoleNames.Admin, type, lines, ct, args.Location, args.From, args.To, args.Reason, args.Reference, args.Notes);

        result["ok"]!.GetValue<bool>().ShouldBeFalse();
        Errors(result).ShouldContain(e => e.StartsWith(key + ":", StringComparison.Ordinal));
        scope.Services.GetRequiredService<DraftCollector>().Drafts.ShouldBeEmpty();
    }

    private sealed record Header(string? Location = "WH-1", string? From = null, string? To = null, string? Reason = null, string? Reference = null, string? Notes = null);

    [Fact]
    public async Task Outflows_over_on_hand_warn_but_still_draft()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        var location = await PostingTests.NewLocationAsync(client, ct, "WH-1");
        await PostingTests.NewLocationAsync(client, ct, "WH-2");
        var item = await PostingTests.NewItemAsync(client, "X-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 5m, 10m, ct);

        await using var scope = Scope(app, tenant);
        var issue = await DraftAsync(scope, RoleNames.Staff, "issue", [L("X-1", 8m)], ct, location: "WH-1");
        var enough = await DraftAsync(scope, RoleNames.Staff, "issue", [L("X-1", 5m)], ct, location: "WH-1");
        var transfer = await DraftAsync(scope, RoleNames.Staff, "transfer", [L("X-1", 6m)], ct, fromLocation: "WH-2", toLocation: "WH-1");

        issue["ok"]!.GetValue<bool>().ShouldBeTrue();
        issue["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).ShouldBe(["lines[0]: Only 5 on hand at WH-1."]);
        enough["ok"]!.GetValue<bool>().ShouldBeTrue();
        enough["warnings"]!.AsArray().ShouldBeEmpty();
        transfer["ok"]!.GetValue<bool>().ShouldBeTrue();
        transfer["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).ShouldBe(["lines[0]: Only 0 on hand at WH-2."]);
        scope.Services.GetRequiredService<DraftCollector>().Drafts.Count.ShouldBe(3);
        scope.Services.GetRequiredService<DraftCollector>().Drafts[0].Warnings.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task At_most_three_drafts_per_reply()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, client) = await StartAsync(app);
        using var _ = client;
        await PostingTests.NewLocationAsync(client, ct, "WH-1");
        await PostingTests.NewItemAsync(client, "X-1", ct);

        await using var scope = Scope(app, tenant);
        for (var i = 1; i <= 3; i++)
        {
            var ok = await DraftAsync(scope, RoleNames.Staff, "receipt", [L("X-1", i, unitCost: 1m)], ct, location: "WH-1");
            ok["ok"]!.GetValue<bool>().ShouldBeTrue();
            ok["draft"]!.GetValue<int>().ShouldBe(i);
        }

        var fourth = await DraftAsync(scope, RoleNames.Staff, "receipt", [L("X-1", 4m, unitCost: 1m)], ct, location: "WH-1");

        fourth["ok"]!.GetValue<bool>().ShouldBeFalse();
        Errors(fourth).ShouldBe(["At most 3 drafts per reply."]);
        scope.Services.GetRequiredService<DraftCollector>().Drafts.Count.ShouldBe(3);
    }

    [Fact]
    public void Draft_limits_mirror_posting_validation()
    {
        DraftLimits.MaxAmount.ShouldBe(PostingValidation.MaxAmount);
        DraftLimits.MaxLines.ShouldBe(PostingValidation.MaxLines);
        Const("MaxReferenceLength").ShouldBe(DraftLimits.MaxReferenceLength);
        Const("MaxNotesLength").ShouldBe(DraftLimits.MaxNotesLength);

        static object? Const(string name) =>
            typeof(PostingValidation).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue();
    }
}