using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;
using Platform.Tenants;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>Posting routes (S5c): the receipt and issue routes' behaviour on the shared poster, and the helpers later tests reuse.</summary>
public sealed class PostingTests(DatabaseFixture db)
{
    internal const string Receipts = "/api/documents/receipts";
    internal const string Issues = "/api/documents/issues";

    [Fact]
    public async Task Receipt_returns_201_with_document_movements_and_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var location = await NewLocationAsync(admin, ct);
        var item = await NewItemAsync(admin, "RICE-1", ct);
        var (staffId, staffEmail) = await AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var response = await PostDocumentAsync(staff, Receipts, NewKey(), Receipt(location, Line(item, 10m, 100.00m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var id = document["id"]!.GetValue<Guid>();
        response.Headers.Location!.OriginalString.ShouldBe($"/api/documents/{id}");
        document["type"]!.GetValue<string>().ShouldBe("Receipt");
        document["postedBy"]!.GetValue<Guid>().ShouldBe(staffId);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var supplier = await LedgerRows.LocationIdAsync(connection, "SUP", ct);
        var movement = document["movements"]!.AsArray().ShouldHaveSingleItem()!;
        movement["lineNo"]!.GetValue<int>().ShouldBe(1);
        movement["itemId"]!.GetValue<Guid>().ShouldBe(item);
        movement["fromLocationId"]!.GetValue<Guid>().ShouldBe(supplier);
        movement["toLocationId"]!.GetValue<Guid>().ShouldBe(location);
        movement["quantity"]!.GetValue<decimal>().ShouldBe(10m);
        movement["totalCost"]!.GetValue<decimal>().ShouldBe(1000.0000m);
        movement["reversesMovementId"].ShouldBeNull();
        var movementId = movement["id"]!.GetValue<long>();

        // Only the physical balance: the Supplier side is virtual.
        var balance = document["balances"]!.AsArray().ShouldHaveSingleItem()!;
        balance["itemId"]!.GetValue<Guid>().ShouldBe(item);
        balance["locationId"]!.GetValue<Guid>().ShouldBe(location);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(10m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(1000.0000m);
        balance["lastUnitCost"]!.GetValue<decimal>().ShouldBe(100.0000m);
        document["balancesAsOf"]!.GetValue<DateTimeOffset>().ShouldBe(document["postedAt"]!.GetValue<DateTimeOffset>());

        (await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE Id = @id", new SqlParameter("@id", id), ct)).ShouldBe(1);
        var (physicalOnHand, physicalLast) = await BalanceRowAsync(connection, item, location, ct);
        var (supplierOnHand, supplierLast) = await BalanceRowAsync(connection, item, supplier, ct);
        physicalOnHand.ShouldBe(10m);
        supplierOnHand.ShouldBe(-10m);
        physicalLast.ShouldBe(movementId);
        supplierLast.ShouldBe(movementId);
    }

    [Fact]
    public async Task Receipt_movement_ids_follow_line_order()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var items = new[]
        {
            await NewItemAsync(client, "A-1", ct),
            await NewItemAsync(client, "B-1", ct),
            await NewItemAsync(client, "C-1", ct),
        };

        var response = await PostDocumentAsync(client, Receipts, NewKey(),
            Receipt(location, Line(items[0], 1m, 1m), Line(items[1], 2m, 2m), Line(items[2], 3m, 3m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var movements = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["movements"]!.AsArray();
        movements.Select(m => m!["lineNo"]!.GetValue<int>()).ShouldBe([1, 2, 3]);
        movements.Select(m => m!["itemId"]!.GetValue<Guid>()).ShouldBe(items);
        var ids = movements.Select(m => m!["id"]!.GetValue<long>()).ToList();
        ids.ShouldBeInOrder(SortDirection.Ascending);
        ids.Distinct().Count().ShouldBe(3);

        // The engine takes the document's ids with one sp_sequence_get_range call, so they are contiguous.
        ids.ShouldBe([ids[0], ids[0] + 1, ids[0] + 2]);
    }

    [Fact]
    public async Task Receipt_without_key_is_400_idempotency_key_missing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        var body = Receipt(location, Line(item, 1m, 1m));

        var missing = await PostDocumentAsync(client, Receipts, null, body, ct);
        var tooLong = await PostDocumentAsync(client, Receipts, new string('k', 65), body, ct);

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.Content.ReadFromJsonAsync<JsonNode>(ct))!["type"]!.GetValue<string>().ShouldBe("/problems/idempotency-key-missing");
        await AssertValidationAsync(tooLong, "Idempotency-Key", ct);
    }

    [Theory]
    [InlineData("no-lines", "lines")]
    [InlineData("zero-quantity", "lines[0].quantity")]
    [InlineData("five-dp-quantity", "lines[0].quantity")]
    [InlineData("negative-cost", "lines[0].unitCost")]
    [InlineData("missing-cost", "lines[0].unitCost")]
    [InlineData("duplicate-item", "lines[1].itemId")]
    [InlineData("line-total-too-large", "lines[0].unitCost")]
    [InlineData("long-reference", "reference")]
    [InlineData("too-many-lines", "lines")]
    [InlineData("quantity-too-large", "lines[0].quantity")]
    [InlineData("cost-too-large", "lines[0].unitCost")]
    [InlineData("long-notes", "notes")]
    public async Task Receipt_validates_fields(string invalid, string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);

        object body = invalid switch
        {
            "no-lines" => Receipt(location),
            "zero-quantity" => Receipt(location, Line(item, 0m, 1m)),
            "five-dp-quantity" => Receipt(location, Line(item, 1.12345m, 1m)),
            "negative-cost" => Receipt(location, Line(item, 1m, -1m)),
            "missing-cost" => Receipt(location, Line(item, 1m, null)),
            "duplicate-item" => Receipt(location, Line(item, 1m, 1m), Line(item, 2m, 1m)),
            "line-total-too-large" => Receipt(location, Line(item, 999_999_999.9999m, 999_999_999.9999m)),
            "long-reference" => new { locationId = location, reference = new string('R', 101), lines = new[] { Line(item, 1m, 1m) } },

            // Over 100 lines, step 1 skips the per-line checks, so only "lines" is reported.
            "too-many-lines" => Receipt(location, [.. Enumerable.Range(0, 101).Select(_ => Line(Guid.NewGuid(), 1m, 1m))]),
            "quantity-too-large" => Receipt(location, Line(item, 1_000_000_000m, 1m)),
            "cost-too-large" => Receipt(location, Line(item, 1m, 1_000_000_000m)),
            "long-notes" => new { locationId = location, notes = new string('N', 501), lines = new[] { Line(item, 1m, 1m) } },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null),
        };

        await AssertValidationAsync(await PostDocumentAsync(client, Receipts, NewKey(), body, ct), key, ct);
    }

    [Fact]
    public async Task Repeated_idempotency_key_header_is_400_validation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, Receipts) { Content = JsonContent.Create(Receipt(location, Line(item, 1m, 1m))) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", new[] { "k1", "k2" }).ShouldBeTrue();

        // The TestServer keeps the two values apart, as Kestrel does; joined, they would post under the key "k1,k2".
        var response = await client.SendAsync(request, ct);

        await AssertValidationAsync(response, "Idempotency-Key", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await DocumentCountAsync(connection, ct)).ShouldBe(0);
        (await BalanceAsync(connection, item, location, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Balance_beyond_the_column_range_is_400_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var x = await NewItemAsync(client, "X-1", ct);
        var y = await NewItemAsync(client, "Y-1", ct);

        // The line total, 999,999,999,999,900.0000, fits decimal(19,4), so step 1 lets it through.
        await ReceiveAsync(client, location, x, 999_999_999.9999m, 1_000_000m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var documentsBefore = await DocumentCountAsync(connection, ct);

        // Every line passes step 1, but X's stock value would be 1,000,000,000,000,000.0000, past the column's maximum
        // (plan D1): step 8 rejects it, and the key names X's line, not the first.
        var response = await PostDocumentAsync(client, Receipts, NewKey(), Receipt(location, Line(y, 1m, 1m), Line(x, 1m, 100m)), ct);

        await AssertValidationAsync(response, "lines[1].quantity", ct);
        (await DocumentCountAsync(connection, ct)).ShouldBe(documentsBefore);
        (await BalanceAsync(connection, x, location, ct)).ShouldBe((999_999_999.9999m, 999_999_999_999_900m));
        (await BalanceAsync(connection, y, location, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Receipt_into_a_virtual_location_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var item = await NewItemAsync(client, "RICE-1", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var supplier = await LedgerRows.LocationIdAsync(connection, "SUP", ct);

        var response = await PostDocumentAsync(client, Receipts, NewKey(), Receipt(supplier, Line(item, 1m, 1m)), ct);

        await AssertValidationAsync(response, "locationId", ct);
    }

    [Fact]
    public async Task Receipt_of_an_inactive_item_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        (await client.PostAsync($"/api/items/{item}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PostDocumentAsync(client, Receipts, NewKey(), Receipt(location, Line(item, 1m, 1m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-item");
        problem["itemId"]!.GetValue<Guid>().ShouldBe(item);
    }

    [Fact]
    public async Task Receipt_into_an_inactive_location_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        (await client.PostAsync($"/api/locations/{location}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PostDocumentAsync(client, Receipts, NewKey(), Receipt(location, Line(item, 1m, 1m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-location");
        problem["locationId"]!.GetValue<Guid>().ShouldBe(location);
    }

    [Fact]
    public async Task Same_key_and_body_replays_with_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        var key = NewKey();
        var body = Receipt(location, Line(item, 10m, 100m));

        var first = await PostDocumentAsync(client, Receipts, key, body, ct);
        var second = await PostDocumentAsync(client, Receipts, key, body, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        var original = (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var replay = (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        replay["id"]!.GetValue<Guid>().ShouldBe(original["id"]!.GetValue<Guid>());
        Movements(replay).ShouldBe(Movements(original));
        replay["balancesAsOf"]!.GetValue<DateTimeOffset>().ShouldBeGreaterThanOrEqualTo(original["balancesAsOf"]!.GetValue<DateTimeOffset>());

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE IdempotencyKey = @key", new SqlParameter("@key", key), ct))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Same_key_different_body_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        var key = NewKey();

        var first = await PostDocumentAsync(client, Receipts, key, Receipt(location, Line(item, 10m, 100m)), ct);
        var second = await PostDocumentAsync(client, Receipts, key, Receipt(location, Line(item, 11m, 100m)), ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var originalId = (await first.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/idempotency-key-reused");
        problem["originalDocumentId"]!.GetValue<Guid>().ShouldBe(originalId);
    }

    [Fact]
    public async Task Staff_and_admin_may_post_receipts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var location = await NewLocationAsync(admin, ct);
        var item = await NewItemAsync(admin, "RICE-1", ct);
        var (_, staffEmail) = await AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        foreach (var client in new[] { admin, staff })
        {
            var response = await PostDocumentAsync(client, Receipts, NewKey(), Receipt(location, Line(item, 1m, 1m)), ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task Issue_costs_at_the_moving_average()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var location = await NewLocationAsync(admin, ct);
        var item = await NewItemAsync(admin, "RICE-1", ct);
        await ReceiveAsync(admin, location, item, 10m, 100.00m, ct);
        await ReceiveAsync(admin, location, item, 10m, 120.00m, ct);
        var (staffId, staffEmail) = await AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        // Spec §7 V3: 15 × 2,200.0000 / 20.
        var response = await PostDocumentAsync(staff, Issues, NewKey(), Issue(location, IssueLine(item, 15m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        document["type"]!.GetValue<string>().ShouldBe("Issue");
        document["postedBy"]!.GetValue<Guid>().ShouldBe(staffId);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var customer = await LedgerRows.LocationIdAsync(connection, "CUS", ct);
        var movement = document["movements"]!.AsArray().ShouldHaveSingleItem()!;
        movement["itemId"]!.GetValue<Guid>().ShouldBe(item);
        movement["fromLocationId"]!.GetValue<Guid>().ShouldBe(location);
        movement["toLocationId"]!.GetValue<Guid>().ShouldBe(customer);
        movement["quantity"]!.GetValue<decimal>().ShouldBe(15m);
        movement["totalCost"]!.GetValue<decimal>().ShouldBe(1650.0000m);

        // Only the physical balance: the Customer side is virtual. Outflows don't set LastUnitCost, so V2's average stays.
        var balance = document["balances"]!.AsArray().ShouldHaveSingleItem()!;
        balance["locationId"]!.GetValue<Guid>().ShouldBe(location);
        balance["onHand"]!.GetValue<decimal>().ShouldBe(5m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(550.0000m);
        balance["lastUnitCost"]!.GetValue<decimal>().ShouldBe(110.0000m);

        (await BalanceAsync(connection, item, location, ct)).ShouldBe((5m, 550.0000m));
        (await BalanceAsync(connection, item, customer, ct)).ShouldBe((15m, 0m));
    }

    [Fact]
    public async Task Issue_of_the_whole_balance_takes_the_remainder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        await ReceiveAsync(client, location, item, 1m, 10.00m, ct);
        await ReceiveAsync(client, location, item, 2m, 10.01m, ct);

        var response = await PostDocumentAsync(client, Issues, NewKey(), Issue(location, IssueLine(item, 3m)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        document["movements"]!.AsArray().ShouldHaveSingleItem()!["totalCost"]!.GetValue<decimal>().ShouldBe(30.0200m);
        var balance = document["balances"]!.AsArray().ShouldHaveSingleItem()!;
        balance["onHand"]!.GetValue<decimal>().ShouldBe(0m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(0.0000m);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await BalanceAsync(connection, item, location, ct)).ShouldBe((0m, 0m));
    }

    [Fact]
    public async Task Issue_beyond_on_hand_is_422_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        await ReceiveAsync(client, location, item, 6m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var documentsBefore = await DocumentCountAsync(connection, ct);

        var response = await PostDocumentAsync(client, Issues, NewKey(), Issue(location, IssueLine(item, 7m)), ct);

        await AssertInsufficientStockAsync(response, item, location, available: 6m, requested: 7m, ct);
        (await DocumentCountAsync(connection, ct)).ShouldBe(documentsBefore);
        (await BalanceAsync(connection, item, location, ct)).ShouldBe((6m, 60m));
    }

    [Fact]
    public async Task Rejected_issue_does_not_consume_its_key()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        await ReceiveAsync(client, location, item, 6m, 10m, ct);
        var key = NewKey();
        var body = Issue(location, IssueLine(item, 7m));

        var rejected = await PostDocumentAsync(client, Issues, key, body, ct);
        await ReceiveAsync(client, location, item, 1m, 10m, ct);
        var posted = await PostDocumentAsync(client, Issues, key, body, ct);

        // Only a successful post consumes a key (spec §6 step 2).
        await AssertInsufficientStockAsync(rejected, item, location, available: 6m, requested: 7m, ct);
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        posted.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }

    [Fact]
    public async Task First_issue_of_an_unreceived_item_is_422_and_leaves_no_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);

        var response = await PostDocumentAsync(client, Issues, NewKey(), Issue(location, IssueLine(item, 1m)), ct);

        // Step 5 creates both balances, then the rejection rolls the transaction back.
        await AssertInsufficientStockAsync(response, item, location, available: 0m, requested: 1m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var customer = await LedgerRows.LocationIdAsync(connection, "CUS", ct);
        (await BalanceAsync(connection, item, location, ct)).ShouldBeNull();
        (await BalanceAsync(connection, item, customer, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Same_key_on_another_endpoint_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        var key = NewKey();

        var receipt = await PostDocumentAsync(client, Receipts, key, Receipt(location, Line(item, 1m, 1m)), ct);
        var issue = await PostDocumentAsync(client, Issues, key, Issue(location, IssueLine(item, 1m)), ct);

        // The route is in the hash, so the same key on another route is a different request.
        receipt.StatusCode.ShouldBe(HttpStatusCode.Created);
        var receiptId = (await receipt.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        issue.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await issue.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/idempotency-key-reused");
        problem["originalDocumentId"]!.GetValue<Guid>().ShouldBe(receiptId);
    }

    [Fact]
    public async Task Replay_survives_a_later_deactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        var key = NewKey();
        var body = Receipt(location, Line(item, 2m, 10m));

        var original = await PostDocumentAsync(client, Receipts, key, body, ct);
        original.StatusCode.ShouldBe(HttpStatusCode.Created);
        var originalId = (await original.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
        var issue = await PostDocumentAsync(client, Issues, NewKey(), Issue(location, IssueLine(item, 2m)), ct);
        issue.StatusCode.ShouldBe(HttpStatusCode.Created);
        var balance = (await issue.Content.ReadFromJsonAsync<JsonNode>(ct))!["balances"]!.AsArray().ShouldHaveSingleItem()!;
        balance["onHand"]!.GetValue<decimal>().ShouldBe(0m);
        balance["stockValue"]!.GetValue<decimal>().ShouldBe(0m);
        (await client.PostAsync($"/api/items/{item}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Plan D2: the step 2 lookup runs before step 1's database checks, so a retry still replays.
        var replay = await PostDocumentAsync(client, Receipts, key, body, ct);
        var fresh = await PostDocumentAsync(client, Receipts, NewKey(), body, ct);

        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await replay.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>().ShouldBe(originalId);
        fresh.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await fresh.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-item");
        problem["itemId"]!.GetValue<Guid>().ShouldBe(item);
    }

    [Theory]
    [InlineData("no-lines", "lines")]
    [InlineData("zero-quantity", "lines[0].quantity")]
    [InlineData("duplicate-item", "lines[1].itemId")]
    [InlineData("virtual-location", "locationId")]
    public async Task Issue_validates_fields(string invalid, string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, "RICE-1", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var customer = await LedgerRows.LocationIdAsync(connection, "CUS", ct);

        var body = invalid switch
        {
            "no-lines" => Issue(location),
            "zero-quantity" => Issue(location, IssueLine(item, 0m)),
            "duplicate-item" => Issue(location, IssueLine(item, 1m), IssueLine(item, 2m)),
            "virtual-location" => Issue(customer, IssueLine(item, 1m)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null),
        };

        await AssertValidationAsync(await PostDocumentAsync(client, Issues, NewKey(), body, ct), key, ct);
    }

    /// <summary>Posts a document as JSON, with the <c>Idempotency-Key</c> header when <paramref name="key"/> isn't null.</summary>
    internal static async Task<HttpResponseMessage> PostDocumentAsync(HttpClient client, string route, string? key, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(request, ct);
    }

    internal static object Receipt(Guid locationId, params object[] lines) =>
        new { locationId, reference = (string?)null, notes = (string?)null, lines };

    internal static object Line(Guid itemId, decimal quantity, decimal? unitCost) => new { itemId, quantity, unitCost };

    internal static object Issue(Guid locationId, params object[] lines) =>
        new { locationId, reference = (string?)null, notes = (string?)null, lines };

    /// <summary>An issue line carries no unit cost: the engine costs it at the moving average.</summary>
    internal static object IssueLine(Guid itemId, decimal quantity) => new { itemId, quantity };

    /// <summary>The balance row's <c>OnHand</c> and <c>StockValue</c>, or null when the row doesn't exist.</summary>
    internal static async Task<(decimal OnHand, decimal StockValue)?> BalanceAsync(SqlConnection connection, Guid item, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT OnHand, StockValue FROM inventory.StockBalances WHERE ItemId = @item AND LocationId = @location", connection);
        command.Parameters.Add(new SqlParameter("@item", SqlDbType.UniqueIdentifier) { Value = item });
        command.Parameters.Add(new SqlParameter("@location", SqlDbType.UniqueIdentifier) { Value = location });
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetDecimal(0), reader.GetDecimal(1)) : null;
    }

    internal static string NewKey() => Guid.NewGuid().ToString("N");

    internal static async Task ReceiveAsync(HttpClient client, Guid location, Guid item, decimal quantity, decimal unitCost, CancellationToken ct) =>
        (await PostDocumentAsync(client, Receipts, NewKey(), Receipt(location, Line(item, quantity, unitCost)), ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

    internal static async Task<Guid> NewLocationAsync(HttpClient client, CancellationToken ct, string code = "WH-1") =>
        (await LocationsTests.CreateAsync(client, code, "Warehouse", ct))["id"]!.GetValue<Guid>();

    internal static async Task<Guid> NewItemAsync(HttpClient client, string sku, CancellationToken ct) =>
        (await ItemsTests.CreateAsync(client, sku, sku, "pc", ct))["id"]!.GetValue<Guid>();

    /// <summary>Adds another user to the tenant and returns their id and email.</summary>
    private static async Task<(Guid Id, string Email)> AddUserAsync(ApiFactory app, Guid tenant, string role)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        await using var scope = app.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>()
            .EnsureUserAsync(tenant, email, role, DatabaseFixture.UserPassword, CancellationToken.None);
        return (user.Id, email);
    }

    private static List<string> Movements(JsonNode document) =>
        document["movements"]!.AsArray()
            .Select(m => string.Join('|',
                m!["id"]!.GetValue<long>(),
                m["lineNo"]!.GetValue<int>(),
                m["itemId"]!.GetValue<Guid>(),
                m["fromLocationId"]!.GetValue<Guid>(),
                m["toLocationId"]!.GetValue<Guid>(),
                m["quantity"]!.GetValue<decimal>().ToString("F4", CultureInfo.InvariantCulture),
                m["totalCost"]!.GetValue<decimal>().ToString("F4", CultureInfo.InvariantCulture)))
            .ToList();

    private static async Task<(decimal OnHand, long? LastMovementId)> BalanceRowAsync(SqlConnection connection, Guid item, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT OnHand, LastMovementId FROM inventory.StockBalances WHERE ItemId = @item AND LocationId = @location", connection);
        command.Parameters.Add(new SqlParameter("@item", SqlDbType.UniqueIdentifier) { Value = item });
        command.Parameters.Add(new SqlParameter("@location", SqlDbType.UniqueIdentifier) { Value = location });
        await using var reader = await command.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).ShouldBeTrue($"No balance row for item {item} at location {location}");
        return (reader.GetDecimal(0), reader.IsDBNull(1) ? null : reader.GetInt64(1));
    }

    internal static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, SqlParameter parameter, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(parameter);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>The tenant's documents: RLS limits the connection to the tenant in its session context.</summary>
    private static async Task<int> DocumentCountAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM inventory.StockDocuments", connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task AssertInsufficientStockAsync(
        HttpResponseMessage response, Guid item, Guid location, decimal available, decimal requested, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/insufficient-stock");
        problem["itemId"]!.GetValue<Guid>().ShouldBe(item);
        problem["locationId"]!.GetValue<Guid>().ShouldBe(location);
        problem["available"]!.GetValue<decimal>().ShouldBe(available);
        problem["requested"]!.GetValue<decimal>().ShouldBe(requested);
    }

    private static async Task AssertValidationAsync(HttpResponseMessage response, string key, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/validation");
        problem["errors"]!.AsObject().Select(e => e.Key).ShouldBe([key]);
    }
}