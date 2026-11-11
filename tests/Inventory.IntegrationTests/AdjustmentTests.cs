using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>Adjustment route (S6): Admin only, costing at the average or the given cost, the reason code, validation and idempotency.</summary>
public sealed class AdjustmentTests(DatabaseFixture db)
{
    internal const string Adjustments = "/api/documents/adjustments";

    [Fact]
    public async Task Staff_gets_403_and_nothing_is_written()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var location = await PostingTests.NewLocationAsync(admin, ct);
        var item = await PostingTests.NewItemAsync(admin, "RICE-1", ct);
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var response = await PostingTests.PostDocumentAsync(staff, Adjustments, PostingTests.NewKey(),
            Adjustment(location, "Found", Delta(item, 1m, 1m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await AdjustmentCountAsync(connection, ct)).ShouldBe(0);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Negative_line_costs_at_the_average_and_stores_the_reason()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 3m, 10.00m, ct);
        await PostingTests.ReceiveAsync(client, location, item, 3m, 12.00m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var adj = await LedgerRows.LocationIdAsync(connection, "ADJ", ct);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((6m, 66.0000m));

        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(location, "Damaged", Delta(item, -2m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var id = document["id"]!.GetValue<Guid>();
        document["type"]!.GetValue<string>().ShouldBe("Adjustment");
        document["reasonCode"]!.GetValue<string>().ShouldBe("Damaged");
        var movement = document["movements"]!.AsArray().ShouldHaveSingleItem()!;
        movement["lineNo"]!.GetValue<int>().ShouldBe(1);
        movement["fromLocationId"]!.GetValue<Guid>().ShouldBe(location);
        movement["toLocationId"]!.GetValue<Guid>().ShouldBe(adj);
        movement["quantity"]!.GetValue<decimal>().ShouldBe(2m);
        movement["totalCost"]!.GetValue<decimal>().ShouldBe(22.0000m);
        var balance = document["balances"]!.AsArray().ShouldHaveSingleItem()!;
        balance["locationId"]!.GetValue<Guid>().ShouldBe(location);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(4m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(44.0000m);

        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((4m, 44.0000m));
        (await PostingTests.ScalarAsync<string>(connection, "SELECT ReasonCode FROM inventory.StockDocuments WHERE Id = @id",
            new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = id }, ct)).ShouldBe("Damaged");

        // Virtual balances aren't in the response: read the ADJ side from the database.
        await using var adjBalance = new SqlCommand("SELECT OnHand FROM inventory.StockBalances WHERE ItemId = @item AND LocationId = @adj", connection);
        adjBalance.Parameters.Add(new SqlParameter("@item", SqlDbType.UniqueIdentifier) { Value = item });
        adjBalance.Parameters.Add(new SqlParameter("@adj", SqlDbType.UniqueIdentifier) { Value = adj });
        ((decimal)(await adjBalance.ExecuteScalarAsync(ct))!).ShouldBe(2m);
    }

    [Fact]
    public async Task Positive_line_without_a_cost_takes_the_average()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 1m, 10.00m, ct);
        await PostingTests.ReceiveAsync(client, location, item, 2m, 10.01m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var adj = await LedgerRows.LocationIdAsync(connection, "ADJ", ct);

        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(location, "Found", Delta(item, 3m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var movement = document["movements"]!.AsArray().ShouldHaveSingleItem()!;
        movement["fromLocationId"]!.GetValue<Guid>().ShouldBe(adj);
        movement["toLocationId"]!.GetValue<Guid>().ShouldBe(location);
        movement["quantity"]!.GetValue<decimal>().ShouldBe(3m);

        // 3 × 30.02 ÷ 3; rounding the average first would give 30.0201.
        movement["totalCost"]!.GetValue<decimal>().ShouldBe(30.0200m);
        var balance = document["balances"]!.AsArray().ShouldHaveSingleItem()!;
        balance["onHand"]!.GetValue<decimal>().ShouldBe(6m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(60.0400m);
        balance["lastUnitCost"]!.GetValue<decimal>().ShouldBe(10.0067m);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((6m, 60.0400m));
    }

    [Fact]
    public async Task Positive_line_with_a_cost_uses_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 3m, 10.00m, ct);

        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(location, "CountCorrection", Delta(item, 2m, 12.50m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        document["movements"]!.AsArray().ShouldHaveSingleItem()!["totalCost"]!.GetValue<decimal>().ShouldBe(25.0000m);
        var balance = document["balances"]!.AsArray().ShouldHaveSingleItem()!;
        balance["onHand"]!.GetValue<decimal>().ShouldBe(5m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(55.0000m);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((5m, 55.0000m));
    }

    [Fact]
    public async Task Mixed_sign_document_moves_each_line_its_own_way()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var x = await PostingTests.NewItemAsync(client, "X-1", ct);
        var y = await PostingTests.NewItemAsync(client, "Y-1", ct);
        await PostingTests.ReceiveAsync(client, location, x, 5m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var adj = await LedgerRows.LocationIdAsync(connection, "ADJ", ct);

        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(location, "Other", Delta(x, -1m), Delta(y, 4m, 2.50m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var movements = document["movements"]!.AsArray();
        movements.Count.ShouldBe(2);
        var first = movements[0]!;
        first["lineNo"]!.GetValue<int>().ShouldBe(1);
        first["itemId"]!.GetValue<Guid>().ShouldBe(x);
        first["fromLocationId"]!.GetValue<Guid>().ShouldBe(location);
        first["toLocationId"]!.GetValue<Guid>().ShouldBe(adj);
        first["quantity"]!.GetValue<decimal>().ShouldBe(1m);
        first["totalCost"]!.GetValue<decimal>().ShouldBe(10.0000m);
        var second = movements[1]!;
        second["lineNo"]!.GetValue<int>().ShouldBe(2);
        second["itemId"]!.GetValue<Guid>().ShouldBe(y);
        second["fromLocationId"]!.GetValue<Guid>().ShouldBe(adj);
        second["toLocationId"]!.GetValue<Guid>().ShouldBe(location);
        second["quantity"]!.GetValue<decimal>().ShouldBe(4m);
        second["totalCost"]!.GetValue<decimal>().ShouldBe(10.0000m);

        var balances = document["balances"]!.AsArray();
        balances.Count.ShouldBe(2);
        var balanceX = balances.Single(b => b!["itemId"]!.GetValue<Guid>() == x)!;
        var balanceY = balances.Single(b => b!["itemId"]!.GetValue<Guid>() == y)!;
        balanceX["onHand"]!.GetValue<decimal>().ShouldBe(4m);
        balanceX["stockValue"]!.GetValue<decimal>().ShouldBe(40.0000m);
        balanceY["onHand"]!.GetValue<decimal>().ShouldBe(4m);
        balanceY["stockValue"]!.GetValue<decimal>().ShouldBe(10.0000m);
        (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((4m, 40.0000m));
        (await PostingTests.BalanceAsync(connection, y, location, ct)).ShouldBe((4m, 10.0000m));
    }

    [Fact]
    public async Task Unit_cost_is_required_when_nothing_is_on_hand()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var adj = await LedgerRows.LocationIdAsync(connection, "ADJ", ct);
        var key = PostingTests.NewKey();

        var rejected = await PostingTests.PostDocumentAsync(client, Adjustments, key, Adjustment(location, "Found", Delta(item, 1m)), ct);

        await PostingTests.AssertValidationAsync(rejected, "lines[0].unitCost", ct);
        (await AdjustmentCountAsync(connection, ct)).ShouldBe(0);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBeNull();
        (await PostingTests.BalanceAsync(connection, item, adj, ct)).ShouldBeNull();

        // A rejected post doesn't consume its key (spec §6 step 2).
        var posted = await PostingTests.PostDocumentAsync(client, Adjustments, key, Adjustment(location, "Found", Delta(item, 1m, 5m)), ct);

        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        posted.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((1m, 5.0000m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("damaged")]
    [InlineData("1")]
    [InlineData("Bogus")]
    public async Task An_invalid_reason_is_400(string? reasonCode)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);

        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(location, reasonCode, Delta(item, 1m, 1m)), ct);

        await PostingTests.AssertValidationAsync(response, "reasonCode", ct);
    }

    [Fact]
    public async Task A_unit_cost_on_a_negative_line_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 5m, 10m, ct);

        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(location, "Lost", Delta(item, -1m, 1m)), ct);

        await PostingTests.AssertValidationAsync(response, "lines[0].unitCost", ct);
    }

    [Fact]
    public async Task A_zero_delta_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 5m, 10m, ct);

        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(location, "Other", Delta(item, 0m)), ct);

        await PostingTests.AssertValidationAsync(response, "lines[0].quantityDelta", ct);
    }

    [Fact]
    public async Task Negative_line_beyond_on_hand_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 2m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(location, "Lost", Delta(item, -3m)), ct);

        await PostingTests.AssertInsufficientStockAsync(response, item, location, available: 2m, requested: 3m, ct);
        (await AdjustmentCountAsync(connection, ct)).ShouldBe(0);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((2m, 20m));
    }

    [Fact]
    public async Task Adjustment_of_a_virtual_location_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var supplier = await LedgerRows.LocationIdAsync(connection, "SUP", ct);

        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(supplier, "Found", Delta(item, 1m, 1m)), ct);

        await PostingTests.AssertValidationAsync(response, "locationId", ct);
    }

    [Fact]
    public async Task Balance_beyond_the_column_range_names_quantityDelta()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var x = await PostingTests.NewItemAsync(client, "X-1", ct);
        var y = await PostingTests.NewItemAsync(client, "Y-1", ct);

        // The line total, 999,999,999,999,900.0000, fits decimal(19,4), so step 1 lets it through.
        await PostingTests.ReceiveAsync(client, location, x, 999_999_999.9999m, 1_000_000m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var documentsBefore = await DocumentCountAsync(connection, ct);

        // X's stock value would pass the column's maximum: step 8 rejects it, and the key names X's line, not the first.
        var response = await PostingTests.PostDocumentAsync(client, Adjustments, PostingTests.NewKey(),
            Adjustment(location, "Found", Delta(y, 1m, 1m), Delta(x, 1m, 100m)), ct);

        await PostingTests.AssertValidationAsync(response, "lines[1].quantityDelta", ct);
        (await DocumentCountAsync(connection, ct)).ShouldBe(documentsBefore);
        (await PostingTests.BalanceAsync(connection, x, location, ct)).ShouldBe((999_999_999.9999m, 999_999_999_999_900m));
        (await PostingTests.BalanceAsync(connection, y, location, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Same_key_and_body_replays_with_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 5m, 10m, ct);
        var key = PostingTests.NewKey();
        var body = Adjustment(location, "Damaged", Delta(item, -2m));

        var first = await PostingTests.PostDocumentAsync(client, Adjustments, key, body, ct);
        var second = await PostingTests.PostDocumentAsync(client, Adjustments, key, body, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        var original = (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var replay = (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        replay["id"]!.GetValue<Guid>().ShouldBe(original["id"]!.GetValue<Guid>());
        replay["type"]!.GetValue<string>().ShouldBe("Adjustment");
        replay["reasonCode"]!.GetValue<string>().ShouldBe("Damaged");
        replay["movements"]!.AsArray().ShouldHaveSingleItem()!["totalCost"]!.GetValue<decimal>().ShouldBe(20.0000m);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE IdempotencyKey = @key",
            new SqlParameter("@key", key), ct)).ShouldBe(1);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((3m, 30m));
    }

    [Fact]
    public async Task A_changed_reason_with_the_same_key_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 5m, 10m, ct);
        var key = PostingTests.NewKey();

        var first = await PostingTests.PostDocumentAsync(client, Adjustments, key, Adjustment(location, "Damaged", Delta(item, -1m)), ct);
        var second = await PostingTests.PostDocumentAsync(client, Adjustments, key, Adjustment(location, "Lost", Delta(item, -1m)), ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var originalId = (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/idempotency-key-reused");
        problem["originalDocumentId"]!.GetValue<Guid>().ShouldBe(originalId);
    }

    internal static object Adjustment(Guid location, string? reasonCode, params object[] lines) =>
        new { locationId = location, reasonCode, reference = (string?)null, notes = (string?)null, lines };

    internal static object Delta(Guid itemId, decimal quantityDelta, decimal? unitCost = null) => new { itemId, quantityDelta, unitCost };

    private static async Task<int> AdjustmentCountAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM inventory.StockDocuments WHERE [Type] = 'Adjustment'", connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>The tenant's documents: RLS limits the connection to the tenant in its session context.</summary>
    private static async Task<int> DocumentCountAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM inventory.StockDocuments", connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }
}