using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>Transfer route (S6): costing at the source average, validation, atomicity and idempotency, through the real API.</summary>
public sealed class TransferTests(DatabaseFixture db)
{
    internal const string Transfers = "/api/documents/transfers";

    public static TheoryData<string, string> NonPhysicalSides() => new()
    {
        { "from", "SUP" }, { "from", "CUS" }, { "from", "ADJ" }, { "from", "none" },
        { "to", "SUP" }, { "to", "CUS" }, { "to", "ADJ" }, { "to", "none" },
    };

    [Fact]
    public async Task Transfer_costs_at_the_source_average_and_carries_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var a = await PostingTests.NewLocationAsync(admin, ct, "A");
        var b = await PostingTests.NewLocationAsync(admin, ct, "B");
        var item = await PostingTests.NewItemAsync(admin, "RICE-1", ct);

        // Spec §7 V1-V5: A ends at 10 / 1,200.0000.
        await PostingTests.ReceiveAsync(admin, a, item, 10m, 100.00m, ct);
        await PostingTests.ReceiveAsync(admin, a, item, 10m, 120.00m, ct);
        (await PostingTests.PostDocumentAsync(admin, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(a, PostingTests.IssueLine(item, 15m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await PostingTests.ReceiveAsync(admin, a, item, 5m, 130.00m, ct);
        var (staffId, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBe((10m, 1200.0000m));

        var response = await PostingTests.PostDocumentAsync(staff, Transfers, PostingTests.NewKey(), Transfer(a, b, TransferLine(item, 4m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var id = document["id"]!.GetValue<Guid>();
        document["type"]!.GetValue<string>().ShouldBe("Transfer");
        document["reasonCode"].ShouldBeNull();
        document["postedBy"]!.GetValue<Guid>().ShouldBe(staffId);

        var movement = document["movements"]!.AsArray().ShouldHaveSingleItem()!;
        movement["lineNo"]!.GetValue<int>().ShouldBe(1);
        movement["itemId"]!.GetValue<Guid>().ShouldBe(item);
        movement["fromLocationId"]!.GetValue<Guid>().ShouldBe(a);
        movement["toLocationId"]!.GetValue<Guid>().ShouldBe(b);
        movement["quantity"]!.GetValue<decimal>().ShouldBe(4m);
        movement["totalCost"]!.GetValue<decimal>().ShouldBe(480.0000m);

        var balances = document["balances"]!.AsArray();
        balances.Count.ShouldBe(2);
        var balanceA = balances.Single(x => x!["locationId"]!.GetValue<Guid>() == a)!;
        var balanceB = balances.Single(x => x!["locationId"]!.GetValue<Guid>() == b)!;
        balanceA["itemId"]!.GetValue<Guid>().ShouldBe(item);
        balanceA["onHand"]!.GetValue<decimal>().ShouldBe(6m);
        balanceA["stockValue"]!.GetValue<decimal>().ShouldBe(720.0000m);
        balanceA["lastUnitCost"]!.GetValue<decimal>().ShouldBe(120.0000m);
        balanceB["itemId"]!.GetValue<Guid>().ShouldBe(item);
        balanceB["onHand"]!.GetValue<decimal>().ShouldBe(4m);
        balanceB["stockValue"]!.GetValue<decimal>().ShouldBe(480.0000m);
        balanceB["lastUnitCost"]!.GetValue<decimal>().ShouldBe(120.0000m);

        (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBe((6m, 720.0000m));
        (await PostingTests.BalanceAsync(connection, item, b, ct)).ShouldBe((4m, 480.0000m));
        await using var reason = new SqlCommand("SELECT ReasonCode FROM inventory.StockDocuments WHERE Id = @id", connection);
        reason.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = id });
        (await reason.ExecuteScalarAsync(ct)).ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task Multi_line_transfer_numbers_movements_in_line_order()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var x = await PostingTests.NewItemAsync(client, "X-1", ct);
        var y = await PostingTests.NewItemAsync(client, "Y-1", ct);
        await PostingTests.ReceiveAsync(client, a, x, 5m, 10m, ct);
        await PostingTests.ReceiveAsync(client, a, y, 5m, 20m, ct);

        var response = await PostingTests.PostDocumentAsync(client, Transfers, PostingTests.NewKey(),
            Transfer(a, b, TransferLine(x, 2m), TransferLine(y, 3m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var movements = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["movements"]!.AsArray();
        movements.Select(m => m!["lineNo"]!.GetValue<int>()).ShouldBe([1, 2]);
        movements.Select(m => m!["itemId"]!.GetValue<Guid>()).ShouldBe([x, y]);
        var ids = movements.Select(m => m!["id"]!.GetValue<long>()).ToList();
        ids.ShouldBeInOrder(SortDirection.Ascending);
        ids.Distinct().Count().ShouldBe(2);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await LastMovementIdAsync(connection, x, a, ct)).ShouldBe(ids[0]);
        (await LastMovementIdAsync(connection, x, b, ct)).ShouldBe(ids[0]);
        (await LastMovementIdAsync(connection, y, a, ct)).ShouldBe(ids[1]);
        (await LastMovementIdAsync(connection, y, b, ct)).ShouldBe(ids[1]);
    }

    [Fact]
    public async Task Transfer_beyond_on_hand_is_422_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 3m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var documentsBefore = await DocumentCountAsync(connection, ct);

        var response = await PostingTests.PostDocumentAsync(client, Transfers, PostingTests.NewKey(), Transfer(a, b, TransferLine(item, 4m)), ct);

        await PostingTests.AssertInsufficientStockAsync(response, item, a, available: 3m, requested: 4m, ct);
        (await DocumentCountAsync(connection, ct)).ShouldBe(documentsBefore);
        (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBe((3m, 30m));
        (await PostingTests.BalanceAsync(connection, item, b, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Transfer_to_the_same_location_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 3m, 10m, ct);

        var response = await PostingTests.PostDocumentAsync(client, Transfers, PostingTests.NewKey(), Transfer(a, a, TransferLine(item, 1m)), ct);

        await PostingTests.AssertValidationAsync(response, "toLocationId", ct);
    }

    [Theory]
    [MemberData(nameof(NonPhysicalSides))]
    public async Task Transfer_to_or_from_a_non_physical_location_is_400(string side, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var physical = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, physical, item, 3m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var wrong = code == "none" ? Guid.NewGuid() : await LedgerRows.LocationIdAsync(connection, code, ct);
        var body = side == "from"
            ? Transfer(wrong, physical, TransferLine(item, 1m))
            : Transfer(physical, wrong, TransferLine(item, 1m));

        var response = await PostingTests.PostDocumentAsync(client, Transfers, PostingTests.NewKey(), body, ct);

        await PostingTests.AssertValidationAsync(response, side == "from" ? "fromLocationId" : "toLocationId", ct);
    }

    [Fact]
    public async Task Transfer_into_an_inactive_location_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 3m, 10m, ct);
        (await client.PostAsync($"/api/locations/{b}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var documentsBefore = await DocumentCountAsync(connection, ct);

        var response = await PostingTests.PostDocumentAsync(client, Transfers, PostingTests.NewKey(), Transfer(a, b, TransferLine(item, 1m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-location");
        problem["locationId"]!.GetValue<Guid>().ShouldBe(b);
        (await DocumentCountAsync(connection, ct)).ShouldBe(documentsBefore);
        (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBe((3m, 30m));
    }

    [Fact]
    public async Task Transfer_from_an_inactive_location_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);

        // Both are empty, so both deactivate; the source is checked first, as at step 7.
        (await client.PostAsync($"/api/locations/{a}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync($"/api/locations/{b}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var documentsBefore = await DocumentCountAsync(connection, ct);

        var response = await PostingTests.PostDocumentAsync(client, Transfers, PostingTests.NewKey(), Transfer(a, b, TransferLine(item, 1m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-location");
        problem["locationId"]!.GetValue<Guid>().ShouldBe(a);
        (await DocumentCountAsync(connection, ct)).ShouldBe(documentsBefore);
        (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBeNull();
        (await PostingTests.BalanceAsync(connection, item, b, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Transfer_of_an_inactive_item_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var stocked = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var inactive = await PostingTests.NewItemAsync(client, "RICE-2", ct);
        await PostingTests.ReceiveAsync(client, a, stocked, 3m, 10m, ct);

        // It has no stock, so it deactivates. Items are checked before stock, so this isn't insufficient-stock.
        (await client.PostAsync($"/api/items/{inactive}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var documentsBefore = await DocumentCountAsync(connection, ct);

        var response = await PostingTests.PostDocumentAsync(client, Transfers, PostingTests.NewKey(),
            Transfer(a, b, TransferLine(stocked, 1m), TransferLine(inactive, 1m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-item");
        problem["itemId"]!.GetValue<Guid>().ShouldBe(inactive);

        // The valid first line isn't written either.
        (await DocumentCountAsync(connection, ct)).ShouldBe(documentsBefore);
        (await PostingTests.BalanceAsync(connection, stocked, a, ct)).ShouldBe((3m, 30m));
        (await PostingTests.BalanceAsync(connection, stocked, b, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Same_key_and_body_replays_with_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 10m, 10m, ct);
        var key = PostingTests.NewKey();
        var body = Transfer(a, b, TransferLine(item, 4m));

        var first = await PostingTests.PostDocumentAsync(client, Transfers, key, body, ct);
        var second = await PostingTests.PostDocumentAsync(client, Transfers, key, body, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        var original = (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var replay = (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        replay["id"]!.GetValue<Guid>().ShouldBe(original["id"]!.GetValue<Guid>());
        replay["type"]!.GetValue<string>().ShouldBe("Transfer");
        replay["movements"]!.AsArray().ShouldHaveSingleItem()!["totalCost"]!.GetValue<decimal>().ShouldBe(40.0000m);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE IdempotencyKey = @key",
            new SqlParameter("@key", key), ct)).ShouldBe(1);
        (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBe((6m, 60m));
    }

    [Fact]
    public async Task Same_key_different_body_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 10m, 10m, ct);
        var key = PostingTests.NewKey();

        var first = await PostingTests.PostDocumentAsync(client, Transfers, key, Transfer(a, b, TransferLine(item, 4m)), ct);
        var second = await PostingTests.PostDocumentAsync(client, Transfers, key, Transfer(a, b, TransferLine(item, 5m)), ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var originalId = (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/idempotency-key-reused");
        problem["originalDocumentId"]!.GetValue<Guid>().ShouldBe(originalId);
    }

    [Fact]
    public async Task Same_key_and_body_on_the_issue_route_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 10m, 10m, ct);
        var key = PostingTests.NewKey();

        var transfer = await PostingTests.PostDocumentAsync(client, Transfers, key, Transfer(a, b, TransferLine(item, 4m)), ct);
        var issue = await PostingTests.PostDocumentAsync(client, PostingTests.Issues, key, PostingTests.Issue(a, PostingTests.IssueLine(item, 4m)), ct);

        // The route is in the hash, so the same key on another route is a different request.
        transfer.StatusCode.ShouldBe(HttpStatusCode.Created);
        var transferId = (await transfer.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        issue.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await issue.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/idempotency-key-reused");
        problem["originalDocumentId"]!.GetValue<Guid>().ShouldBe(transferId);
    }

    internal static object Transfer(Guid from, Guid to, params object[] lines) =>
        new { fromLocationId = from, toLocationId = to, reference = (string?)null, notes = (string?)null, lines };

    internal static object TransferLine(Guid itemId, decimal quantity) => new { itemId, quantity };

    private static async Task<long?> LastMovementIdAsync(SqlConnection connection, Guid item, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT LastMovementId FROM inventory.StockBalances WHERE ItemId = @item AND LocationId = @location", connection);
        command.Parameters.Add(new SqlParameter("@item", SqlDbType.UniqueIdentifier) { Value = item });
        command.Parameters.Add(new SqlParameter("@location", SqlDbType.UniqueIdentifier) { Value = location });
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : (long)value;
    }

    /// <summary>The tenant's documents: RLS limits the connection to the tenant in its session context.</summary>
    private static async Task<int> DocumentCountAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM inventory.StockDocuments", connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }
}