using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>T8 (spec §11) and the reversal route's rejections (S9): <c>POST /api/documents/{id}/reversal</c>, Admin only, no body.</summary>
public sealed class ReversalTests(DatabaseFixture db)
{
    internal static string Route(Guid documentId) => $"/api/documents/{documentId}/reversal";

    /// <summary>Posts a reversal with no body, with the Idempotency-Key header when <paramref name="key"/> isn't null.</summary>
    internal static async Task<HttpResponseMessage> ReverseAsync(HttpClient client, Guid documentId, string? key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route(documentId));
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(request, ct);
    }

    /// <summary>The <c>id</c> of a posting response.</summary>
    internal static async Task<Guid> IdAsync(HttpResponseMessage response, CancellationToken ct) =>
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();

    /// <summary>OnHand, StockValue and LastUnitCost of the balance row, or null when there is none.</summary>
    internal static async Task<(decimal OnHand, decimal StockValue, decimal? LastUnitCost)?> BalanceAsync(
        SqlConnection connection, Guid item, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT OnHand, StockValue, LastUnitCost FROM inventory.StockBalances WHERE ItemId = @item AND LocationId = @location", connection);
        command.Parameters.Add(new SqlParameter("@item", SqlDbType.UniqueIdentifier) { Value = item });
        command.Parameters.Add(new SqlParameter("@location", SqlDbType.UniqueIdentifier) { Value = location });
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? (reader.GetDecimal(0), reader.GetDecimal(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2))
            : null;
    }

    /// <summary>Asserts the status and the problem <c>type</c> "/problems/&lt;slug&gt;"; extension members are read from the body by the caller.</summary>
    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string slug, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(status);
        var problem = await ProblemAsync(response, ct);
        problem["type"]!.GetValue<string>().ShouldBe($"/problems/{slug}");
    }

    /// <summary>The problem body. It reads the buffered text, so it can be read again after <see cref="AssertProblemAsync"/>.</summary>
    internal static async Task<JsonNode> ProblemAsync(HttpResponseMessage response, CancellationToken ct) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;

    private static async Task<Guid> ReceiptAsync(HttpClient client, Guid location, CancellationToken ct, params object[] lines)
    {
        var response = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, PostingTests.NewKey(),
            PostingTests.Receipt(location, lines), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await IdAsync(response, ct);
    }

    private static async Task<Guid> IssueAsync(HttpClient client, Guid location, Guid item, decimal quantity, CancellationToken ct)
    {
        var response = await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(item, quantity)), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await IdAsync(response, ct);
    }

    private static async Task<Guid> ReverseOkAsync(HttpClient client, Guid documentId, CancellationToken ct)
    {
        var response = await ReverseAsync(client, documentId, PostingTests.NewKey(), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await IdAsync(response, ct);
    }

    private static async Task<int> ReversalCountAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM inventory.StockDocuments WHERE Type = 'Reversal'", connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<decimal> OnHandAsync(SqlConnection connection, Guid item, Guid location, CancellationToken ct) =>
        (await BalanceAsync(connection, item, location, ct))!.Value.OnHand;

    [Fact]
    public async Task Reversing_a_two_line_receipt_restores_both_balances()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var x = await PostingTests.NewItemAsync(client, "X", ct);
        var y = await PostingTests.NewItemAsync(client, "Y", ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var sup = await LedgerRows.LocationIdAsync(connection, "SUP", ct);
        var receiptResponse = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, PostingTests.NewKey(),
            PostingTests.Receipt(a, PostingTests.Line(x, 3m, 10m), PostingTests.Line(y, 2m, 7.5m)), ct);
        receiptResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var receipt = (await receiptResponse.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var receiptId = receipt["id"]!.GetValue<Guid>();

        var response = await ReverseAsync(client, receiptId, PostingTests.NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reversal = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var reversalId = reversal["id"]!.GetValue<Guid>();
        response.Headers.Location!.OriginalString.ShouldBe($"/api/documents/{reversalId}");
        reversal["type"]!.GetValue<string>().ShouldBe("Reversal");
        reversal["reversesDocumentId"]!.GetValue<Guid>().ShouldBe(receiptId);
        reversal["reasonCode"].ShouldBeNull();
        var originals = receipt["movements"]!.AsArray().ToDictionary(m => m!["id"]!.GetValue<long>());
        var movements = reversal["movements"]!.AsArray();
        movements.Count.ShouldBe(2);
        foreach (var movement in movements)
        {
            var original = originals[movement!["reversesMovementId"]!.GetValue<long>()]!;
            movement["fromLocationId"]!.GetValue<Guid>().ShouldBe(a);
            movement["toLocationId"]!.GetValue<Guid>().ShouldBe(sup);
            movement["itemId"]!.GetValue<Guid>().ShouldBe(original["itemId"]!.GetValue<Guid>());
            movement["quantity"]!.GetValue<decimal>().ShouldBe(original["quantity"]!.GetValue<decimal>());
            movement["totalCost"]!.GetValue<decimal>().ShouldBe(original["totalCost"]!.GetValue<decimal>());
            movement["lineNo"]!.GetValue<int>().ShouldBe(original["lineNo"]!.GetValue<int>());
        }

        movements.Select(m => m!["reversesMovementId"]!.GetValue<long>()).ShouldBe(originals.Keys, ignoreOrder: true);
        var balances = reversal["balances"]!.AsArray();
        balances.Count.ShouldBe(2);
        foreach (var item in new[] { x, y })
        {
            var balance = balances.Single(b => b!["itemId"]!.GetValue<Guid>() == item)!;
            balance["locationId"]!.GetValue<Guid>().ShouldBe(a);
            balance["onHand"]!.GetValue<decimal>().ShouldBe(0m);
            balance["stockValue"]!.GetValue<decimal>().ShouldBe(0m);
            balance["lastUnitCost"].ShouldBeNull();
            (await BalanceAsync(connection, item, a, ct)).ShouldBe((0m, 0m, null));
            (await OnHandAsync(connection, item, sup, ct)).ShouldBe(0m);
        }

        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Reversing_an_issue_restores_value_and_last_unit_cost()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 10m, 100m, ct);
        var issue = await IssueAsync(client, a, item, 3m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var cus = await LedgerRows.LocationIdAsync(connection, "CUS", ct);
        (await BalanceAsync(connection, item, a, ct)).ShouldBe((7m, 700m, 100m));

        var response = await ReverseAsync(client, issue, PostingTests.NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await BalanceAsync(connection, item, a, ct)).ShouldBe((10m, 1000m, 100m));
        (await OnHandAsync(connection, item, cus, ct)).ShouldBe(0m);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Reversing_a_transfer_restores_both_locations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 10m, 100m, ct);
        await PostingTests.ReceiveAsync(client, b, item, 5m, 120m, ct);
        var transferResponse = await PostingTests.PostDocumentAsync(client, TransferTests.Transfers, PostingTests.NewKey(),
            TransferTests.Transfer(a, b, TransferTests.TransferLine(item, 4m)), ct);
        transferResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var transfer = await IdAsync(transferResponse, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await BalanceAsync(connection, item, b, ct)).ShouldBe((9m, 1000m, 111.1111m));

        var response = await ReverseAsync(client, transfer, PostingTests.NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await BalanceAsync(connection, item, a, ct)).ShouldBe((10m, 1000m, 100m));
        (await BalanceAsync(connection, item, b, ct)).ShouldBe((5m, 600m, 120m));
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Reversing_an_adjustment_stores_no_reason()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var d = await PostingTests.NewLocationAsync(client, ct, "D");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, d, item, 3m, 10m, ct);
        var adjustmentResponse = await PostingTests.PostDocumentAsync(client, AdjustmentTests.Adjustments, PostingTests.NewKey(),
            AdjustmentTests.Adjustment(d, "Damaged", AdjustmentTests.Delta(item, -1m)), ct);
        adjustmentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var adjustment = await IdAsync(adjustmentResponse, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await BalanceAsync(connection, item, d, ct)).ShouldBe((2m, 20m, 10m));

        var response = await ReverseAsync(client, adjustment, PostingTests.NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reversal = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        reversal["reasonCode"].ShouldBeNull();
        (await BalanceAsync(connection, item, d, ct)).ShouldBe((3m, 30m, 10m));
        var stored = await PostingTests.ScalarAsync<object>(connection, "SELECT ReasonCode FROM inventory.StockDocuments WHERE Id = @id",
            new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = reversal["id"]!.GetValue<Guid>() }, ct);
        stored.ShouldBe(DBNull.Value);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Reversing_twice_is_409_already_reversed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var receipt = await ReceiptAsync(client, a, ct, PostingTests.Line(item, 5m, 10m));
        var first = await ReverseOkAsync(client, receipt, ct);

        var response = await ReverseAsync(client, receipt, PostingTests.NewKey(), ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "already-reversed", ct);
        (await ProblemAsync(response, ct))["reversalDocumentId"]!.GetValue<Guid>().ShouldBe(first);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await ReversalCountAsync(connection, ct)).ShouldBe(1);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Reversing_a_reversal_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var receipt = await ReceiptAsync(client, a, ct, PostingTests.Line(item, 5m, 10m));
        var reversal = await ReverseOkAsync(client, receipt, ct);

        var response = await ReverseAsync(client, reversal, PostingTests.NewKey(), ct);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cannot-reverse-reversal", ct);
        (await ProblemAsync(response, ct))["reversesDocumentId"]!.GetValue<Guid>().ShouldBe(receipt);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await ReversalCountAsync(connection, ct)).ShouldBe(1);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Reversing_a_non_latest_document_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var receipt = await ReceiptAsync(client, a, ct, PostingTests.Line(item, 5m, 10m));
        var issue = await IssueAsync(client, a, item, 1m, ct);

        var response = await ReverseAsync(client, receipt, PostingTests.NewKey(), ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "reversal-not-latest", ct);
        var problem = await ProblemAsync(response, ct);
        problem["itemId"]!.GetValue<Guid>().ShouldBe(item);
        problem["locationId"]!.GetValue<Guid>().ShouldBe(a);
        problem["blockingDocumentId"]!.GetValue<Guid>().ShouldBe(issue);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, item, a, ct)).ShouldBe((4m, 40m));
        (await ReversalCountAsync(connection, ct)).ShouldBe(0);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Same_key_replays_with_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var receipt = await ReceiptAsync(client, a, ct, PostingTests.Line(item, 5m, 10m));
        var key = PostingTests.NewKey();
        var first = await ReverseAsync(client, receipt, key, ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await ReverseAsync(client, receipt, key, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await IdAsync(second, ct)).ShouldBe(await IdAsync(first, ct));
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await ReversalCountAsync(connection, ct)).ShouldBe(1);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Same_key_elsewhere_is_409_idempotency_key_reused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item1 = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var item2 = await PostingTests.NewItemAsync(client, "RICE-2", ct);
        var item3 = await PostingTests.NewItemAsync(client, "RICE-3", ct);
        var r1 = await ReceiptAsync(client, a, ct, PostingTests.Line(item1, 5m, 10m));
        var r2 = await ReceiptAsync(client, a, ct, PostingTests.Line(item2, 5m, 10m));
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        // (a) The key already belongs to the reversal of R1.
        var key = PostingTests.NewKey();
        var reversal1 = await ReverseAsync(client, r1, key, ct);
        reversal1.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reusedOnReversal = await ReverseAsync(client, r2, key, ct);
        await AssertProblemAsync(reusedOnReversal, HttpStatusCode.Conflict, "idempotency-key-reused", ct);
        (await ProblemAsync(reusedOnReversal, ct))["originalDocumentId"]!.GetValue<Guid>().ShouldBe(await IdAsync(reversal1, ct));

        // (b) The key already belongs to a receipt.
        var key2 = PostingTests.NewKey();
        var receiptResponse = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key2,
            PostingTests.Receipt(a, PostingTests.Line(item3, 1m, 1m)), ct);
        receiptResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reusedOnReceipt = await ReverseAsync(client, r2, key2, ct);
        await AssertProblemAsync(reusedOnReceipt, HttpStatusCode.Conflict, "idempotency-key-reused", ct);
        (await ProblemAsync(reusedOnReceipt, ct))["originalDocumentId"]!.GetValue<Guid>().ShouldBe(await IdAsync(receiptResponse, ct));

        (await ReversalCountAsync(connection, ct)).ShouldBe(1);
        (await BalanceAsync(connection, item2, a, ct)).ShouldBe((5m, 50m, 10m));
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Missing_key_is_400_and_unknown_id_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var receipt = await ReceiptAsync(client, a, ct, PostingTests.Line(item, 5m, 10m));

        await AssertProblemAsync(await ReverseAsync(client, receipt, null, ct), HttpStatusCode.BadRequest, "idempotency-key-missing", ct);
        await AssertProblemAsync(await ReverseAsync(client, Guid.NewGuid(), PostingTests.NewKey(), ct), HttpStatusCode.NotFound, "not-found", ct);

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await ReversalCountAsync(connection, ct)).ShouldBe(0);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Staff_gets_403_and_nothing_is_written()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, adminEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, adminEmail);
        var location = await PostingTests.NewLocationAsync(admin, ct);
        var item = await PostingTests.NewItemAsync(admin, "RICE-1", ct);
        var receipt = await ReceiptAsync(admin, location, ct, PostingTests.Line(item, 5m, 10m));
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        var response = await ReverseAsync(staff, receipt, PostingTests.NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await ReversalCountAsync(connection, ct)).ShouldBe(0);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((5m, 50m));
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Inactive_item_is_422_and_the_key_stays_free()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var other = await PostingTests.NewItemAsync(client, "RICE-2", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        var issue = await IssueAsync(client, a, item, 5m, ct);
        (await client.PostAsync($"/api/items/{item}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var key = PostingTests.NewKey();

        var response = await ReverseAsync(client, issue, key, ct);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "inactive-item", ct);
        (await ProblemAsync(response, ct))["itemId"]!.GetValue<Guid>().ShouldBe(item);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await ReversalCountAsync(connection, ct)).ShouldBe(0);

        // Only a successful post consumes a key.
        var reused = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key,
            PostingTests.Receipt(a, PostingTests.Line(other, 1m, 1m)), ct);
        reused.StatusCode.ShouldBe(HttpStatusCode.Created);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Inactive_location_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        var issue = await IssueAsync(client, a, item, 5m, ct);
        (await client.PostAsync($"/api/locations/{a}/deactivate", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await ReverseAsync(client, issue, PostingTests.NewKey(), ct);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "inactive-location", ct);
        (await ProblemAsync(response, ct))["locationId"]!.GetValue<Guid>().ShouldBe(a);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await ReversalCountAsync(connection, ct)).ShouldBe(0);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    [Fact]
    public async Task Reversal_links_both_ways_and_shows_in_item_history()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 10m, 100m, ct);
        var issue = await IssueAsync(client, a, item, 3m, ct);
        var reversal = await ReverseOkAsync(client, issue, ct);

        var issueDetail = await GetJsonAsync(client, $"/api/documents/{issue}", ct);
        var reversalDetail = await GetJsonAsync(client, $"/api/documents/{reversal}", ct);
        var history = await GetJsonAsync(client, $"/api/items/{item}/movements?pageSize=100", ct);

        issueDetail["reversedByDocumentId"]!.GetValue<Guid>().ShouldBe(reversal);
        reversalDetail["reversesDocumentId"]!.GetValue<Guid>().ShouldBe(issue);
        reversalDetail["type"]!.GetValue<string>().ShouldBe("Reversal");
        reversalDetail["lines"]!.AsArray().ShouldHaveSingleItem()!["reversesMovementId"].ShouldNotBeNull();
        var newest = history["items"]!.AsArray()[0]!;
        newest["documentId"]!.GetValue<Guid>().ShouldBe(reversal);
        newest["documentType"]!.GetValue<string>().ShouldBe("Reversal");
        newest["direction"]!.GetValue<string>().ShouldBe("In");
        newest["locationId"]!.GetValue<Guid>().ShouldBe(a);
        newest["quantityChange"]!.GetValue<decimal>().ShouldBe(3m);
        newest["valueChange"]!.GetValue<decimal>().ShouldBe(300m);
        newest["runningOnHand"]!.GetValue<decimal>().ShouldBe(10m);
        newest["runningValue"]!.GetValue<decimal>().ShouldBe(1000m);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await LedgerInvariants.AssertHoldAsync(connection, ct);
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        var response = await client.GetAsync(path, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }
}