using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Inventory.Features.Posting;
using Inventory.Features.Posting.PostReceipt;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// T2 and T4 (spec §11) against the real API: parallel postings of the last unit, idempotency under sequential, parallel
/// and deterministic step-4 races, and the step-7 re-read of a deactivation committed mid-posting (spec §6).
/// T4's parallel same-key reversals land with the reversal route in S9.
/// </summary>
public sealed class PostingConcurrencyTests(DatabaseFixture db)
{
    [Fact]
    public async Task Twenty_parallel_issues_of_the_last_unit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, ct);
        await ReceiveAsync(client, location, item, 1m, 10m, ct);

        var responses = await InParallelAsync(20, () =>
            PostingTests.PostDocumentAsync(client, PostingTests.Issues, NewKey(), PostingTests.Issue(location, PostingTests.IssueLine(item, 1m)), ct));

        // 1 + 19 = 20, so no 500 and no 409.
        Count(responses, HttpStatusCode.Created).ShouldBe(1, Statuses(responses));
        Count(responses, HttpStatusCode.UnprocessableEntity).ShouldBe(19, Statuses(responses));
        foreach (var rejected in responses.Where(r => r.StatusCode == HttpStatusCode.UnprocessableEntity))
        {
            var problem = (await rejected.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            problem["type"]!.GetValue<string>().ShouldBe("/problems/insufficient-stock");
            problem["available"]!.GetValue<decimal>().ShouldBe(0m);
            problem["requested"]!.GetValue<decimal>().ShouldBe(1m);
        }

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((0m, 0m));
        (await DocumentsOfTypeAsync(connection, "Issue", ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Same_key_and_body_sequentially_replays()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, ct);
        var key = NewKey();
        var body = PostingTests.Receipt(location, PostingTests.Line(item, 4m, 12.5m));

        var first = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key, body, ct);
        var second = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key, body, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await IdAsync(second, ct)).ShouldBe(await IdAsync(first, ct));

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await DocumentsWithKeyAsync(connection, key, ct)).ShouldBe(1);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((4m, 50m));
    }

    [Fact]
    public async Task Same_key_and_body_in_parallel_gives_one_document()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, ct);
        var key = NewKey();
        var body = PostingTests.Receipt(location, PostingTests.Line(item, 4m, 12.5m));

        var responses = await InParallelAsync(10, () => PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key, body, ct));

        // 1 + 9 = 10, so no 409, 422 or 500.
        Count(responses, HttpStatusCode.Created).ShouldBe(1, Statuses(responses));
        Count(responses, HttpStatusCode.OK).ShouldBe(9, Statuses(responses));
        foreach (var replay in responses.Where(r => r.StatusCode == HttpStatusCode.OK))
        {
            replay.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        }

        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            ids.Add(await IdAsync(response, ct));
        }

        ids.Distinct().ShouldHaveSingleItem();
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await DocumentsWithKeyAsync(connection, key, ct)).ShouldBe(1);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((4m, 50m));
    }

    [Fact]
    public async Task Two_parallel_same_key_issues_of_the_last_unit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, ct);
        await ReceiveAsync(client, location, item, 1m, 10m, ct);
        var key = NewKey();
        var body = PostingTests.Issue(location, PostingTests.IssueLine(item, 1m));

        var responses = await InParallelAsync(2, () => PostingTests.PostDocumentAsync(client, PostingTests.Issues, key, body, ct));

        // The document is inserted first (step 4), so the duplicate waits on the key, never on the stock: no 422.
        Count(responses, HttpStatusCode.Created).ShouldBe(1, Statuses(responses));
        Count(responses, HttpStatusCode.OK).ShouldBe(1, Statuses(responses));
        responses.Single(r => r.StatusCode == HttpStatusCode.OK).Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await IdAsync(responses[0], ct)).ShouldBe(await IdAsync(responses[1], ct));

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await PostingTests.BalanceAsync(connection, item, location, ct)).ShouldBe((0m, 0m));
        (await DocumentsWithKeyAsync(connection, key, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Same_key_different_body_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, ct);
        await ReceiveAsync(client, location, item, 2m, 10m, ct);
        var key = NewKey();

        var first = await PostingTests.PostDocumentAsync(client, PostingTests.Issues, key, PostingTests.Issue(location, PostingTests.IssueLine(item, 1m)), ct);
        var second = await PostingTests.PostDocumentAsync(client, PostingTests.Issues, key, PostingTests.Issue(location, PostingTests.IssueLine(item, 2m)), ct);

        // The second would also be short of stock, but the key is checked first (step 2): 409, not 422.
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await second.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/idempotency-key-reused");
        problem["originalDocumentId"]!.GetValue<Guid>().ShouldBe(await IdAsync(first, ct));
    }

    [Fact]
    public async Task Race_on_the_key_replays_when_the_holder_commits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, ct);
        var key = NewKey();
        var (body, hash) = ReceiptWithHash(location, item, 3m, 7.5m);

        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        var held = await InsertDocumentAsync(holder, tx, key, hash, ct);

        // RCSI hides the uncommitted row from the step 2 lookup, so the request blocks on the key's unique index at step 4.
        var request = Task.Run(() => PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key, body, ct), ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        await tx.CommitAsync(ct);
        var response = await request;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        document["id"]!.GetValue<Guid>().ShouldBe(held);

        // The replay is the holder's document, which has no movements; the request's own attempt rolled back.
        document["movements"]!.AsArray().ShouldBeEmpty();
        (await DocumentsWithKeyAsync(holder, key, ct)).ShouldBe(1);
        (await PostingTests.BalanceAsync(holder, item, location, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Race_on_the_key_proceeds_when_the_holder_rolls_back()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, ct);
        var key = NewKey();
        var (body, hash) = ReceiptWithHash(location, item, 3m, 7.5m);

        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        var held = await InsertDocumentAsync(holder, tx, key, hash, ct);

        var request = Task.Run(() => PostingTests.PostDocumentAsync(client, PostingTests.Receipts, key, body, ct), ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        await tx.RollbackAsync(ct);
        var response = await request;

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        var id = document["id"]!.GetValue<Guid>();
        id.ShouldNotBe(held);
        document["movements"]!.AsArray().ShouldHaveSingleItem()!["totalCost"]!.GetValue<decimal>().ShouldBe(22.5m);

        (await DocumentsWithKeyAsync(holder, key, ct)).ShouldBe(1);
        (await MovementsOfDocumentAsync(holder, id, ct)).ShouldBe(1);
        (await PostingTests.BalanceAsync(holder, item, location, ct)).ShouldBe((3m, 22.5m));
    }

    [Fact]
    public async Task Deactivation_committed_mid_posting_is_seen_at_step_7()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, ct);

        // Both balance rows now exist, so the next posting's step 5 inserts nothing and takes no FK lock on Items.
        await ReceiveAsync(client, location, item, 1m, 10m, ct);
        var key = NewKey();

        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await DeactivateItemAsync(holder, tx, item, ct);

        // RCSI shows the item as active at step 1; step 7's READCOMMITTEDLOCK re-read waits for the holder.
        var request = Task.Run(() => PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, key, PostingTests.Receipt(location, PostingTests.Line(item, 1m, 10m)), ct), ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        await tx.CommitAsync(ct);
        var response = await request;

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-item");
        problem["itemId"]!.GetValue<Guid>().ShouldBe(item);
        (await DocumentsWithKeyAsync(holder, key, ct)).ShouldBe(0);
        (await PostingTests.BalanceAsync(holder, item, location, ct)).ShouldBe((1m, 10m));
    }

    [Fact]
    public async Task Deactivation_rolled_back_mid_posting_lets_it_post()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await NewLocationAsync(client, ct);
        var item = await NewItemAsync(client, ct);
        await ReceiveAsync(client, location, item, 1m, 10m, ct);
        var key = NewKey();

        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await DeactivateItemAsync(holder, tx, item, ct);

        var request = Task.Run(() => PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, key, PostingTests.Receipt(location, PostingTests.Line(item, 1m, 10m)), ct), ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        await tx.RollbackAsync(ct);
        var response = await request;

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await DocumentsWithKeyAsync(holder, key, ct)).ShouldBe(1);
        (await PostingTests.BalanceAsync(holder, item, location, ct)).ShouldBe((2m, 20m));
    }

    /// <summary>Starts every send parked behind one gate, then opens it, so the requests reach the API together.</summary>
    private static async Task<HttpResponseMessage[]> InParallelAsync(int count, Func<Task<HttpResponseMessage>> send)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = Enumerable.Range(0, count)
            .Select(async _ =>
            {
                await gate.Task;
                return await send();
            })
            .ToList();
        gate.SetResult();
        return await Task.WhenAll(sends);
    }

    /// <summary>
    /// The receipt's JSON body and the <c>RequestHash</c> the API computes for it: the same step-1 validation, then the
    /// canonical hash. A mismatch would make the race tests' request a 409 instead of a replay.
    /// </summary>
    private static (object Body, byte[] Hash) ReceiptWithHash(Guid location, Guid item, decimal quantity, decimal unitCost)
    {
        var errors = new Dictionary<string, string[]>();
        var validated = PostingValidation.Receipt(new PostReceiptRequest(location, null, null, [new PostReceiptLine(item, quantity, unitCost)]), errors);
        errors.ShouldBeEmpty();
        return (PostingTests.Receipt(location, PostingTests.Line(item, quantity, unitCost)), RequestHash.Receipt(validated!));
    }

    /// <summary>Claims the key in the holder's open transaction, as step 4 would. <c>TenantId</c> comes from the session context.</summary>
    private static async Task<Guid> InsertDocumentAsync(SqlConnection holder, SqlTransaction tx, string key, byte[] hash, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var command = new SqlCommand("""
            INSERT INTO inventory.StockDocuments (Id, Type, IdempotencyKey, RequestHash, PostedAt, PostedBy)
            VALUES (@id, 'Receipt', @key, @hash, SYSDATETIMEOFFSET(), NEWID());
            """, holder, tx);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = id });
        command.Parameters.Add(new SqlParameter("@key", SqlDbType.VarChar, 64) { Value = key });
        command.Parameters.Add(new SqlParameter("@hash", SqlDbType.Binary, 32) { Value = hash });
        await command.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task DeactivateItemAsync(SqlConnection holder, SqlTransaction tx, Guid item, CancellationToken ct)
    {
        await using var command = new SqlCommand("UPDATE inventory.Items SET IsActive = 0 WHERE Id = @x", holder, tx);
        command.Parameters.Add(new SqlParameter("@x", SqlDbType.UniqueIdentifier) { Value = item });
        (await command.ExecuteNonQueryAsync(ct)).ShouldBe(1);
    }

    private static Task<int> DocumentsWithKeyAsync(SqlConnection connection, string key, CancellationToken ct) =>
        CountAsync(connection, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE IdempotencyKey = @p",
            new SqlParameter("@p", SqlDbType.VarChar, 64) { Value = key }, ct);

    /// <summary>The tenant's documents of this type: RLS limits the connection to the tenant in its session context.</summary>
    private static Task<int> DocumentsOfTypeAsync(SqlConnection connection, string type, CancellationToken ct) =>
        CountAsync(connection, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE Type = @p",
            new SqlParameter("@p", SqlDbType.VarChar, 16) { Value = type }, ct);

    private static Task<int> MovementsOfDocumentAsync(SqlConnection connection, Guid document, CancellationToken ct) =>
        CountAsync(connection, "SELECT COUNT(*) FROM inventory.StockMovements WHERE DocumentId = @p",
            new SqlParameter("@p", SqlDbType.UniqueIdentifier) { Value = document }, ct);

    private static async Task<int> CountAsync(SqlConnection connection, string sql, SqlParameter parameter, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(parameter);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response, CancellationToken ct) =>
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();

    private static int Count(HttpResponseMessage[] responses, HttpStatusCode status) => responses.Count(r => r.StatusCode == status);

    private static string Statuses(HttpResponseMessage[] responses) =>
        string.Join(", ", responses.GroupBy(r => (int)r.StatusCode).OrderBy(g => g.Key).Select(g => $"{g.Key} × {g.Count()}"));

    // PostingTests keeps these private; the same few lines here.
    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static async Task ReceiveAsync(HttpClient client, Guid location, Guid item, decimal quantity, decimal unitCost, CancellationToken ct) =>
        (await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, NewKey(), PostingTests.Receipt(location, PostingTests.Line(item, quantity, unitCost)), ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

    private static async Task<Guid> NewLocationAsync(HttpClient client, CancellationToken ct) =>
        (await LocationsTests.CreateAsync(client, "WH-1", "Warehouse", ct))["id"]!.GetValue<Guid>();

    private static async Task<Guid> NewItemAsync(HttpClient client, CancellationToken ct) =>
        (await ItemsTests.CreateAsync(client, "RICE-1", "Rice", "pc", ct))["id"]!.GetValue<Guid>();
}