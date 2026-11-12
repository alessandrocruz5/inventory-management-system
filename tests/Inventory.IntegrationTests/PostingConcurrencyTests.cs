using System.Data;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Inventory.Features.Posting;
using Inventory.Features.Posting.PostReceipt;
using Inventory.Persistence;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// T2 and T4 (spec §11) against the real API: parallel postings of the last unit, idempotency under sequential, parallel
/// and deterministic step-4 races, the step-7 re-read of an item or location deactivation committed mid-posting, and
/// the retry of a deadlocked posting (spec §6), and T3: 50 parallel transfers both ways between two locations, which
/// must never deadlock (spec §6 step 6). T4's parallel same-key reversals land with the reversal route in S9.
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
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 1m, 10m, ct);

        var responses = await InParallelAsync(20, () =>
            PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(), PostingTests.Issue(location, PostingTests.IssueLine(item, 1m)), ct));

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
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var key = PostingTests.NewKey();
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
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var key = PostingTests.NewKey();
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
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 1m, 10m, ct);
        var key = PostingTests.NewKey();
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
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 2m, 10m, ct);
        var key = PostingTests.NewKey();

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
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var key = PostingTests.NewKey();
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
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var key = PostingTests.NewKey();
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
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);

        // Both balance rows now exist, so the next posting's step 5 inserts nothing and takes no FK lock on Items.
        await PostingTests.ReceiveAsync(client, location, item, 1m, 10m, ct);
        var key = PostingTests.NewKey();

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
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 1m, 10m, ct);
        var key = PostingTests.NewKey();

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

    [Fact]
    public async Task Location_deactivation_committed_mid_posting_is_seen_at_step_7()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);

        // Both balance rows now exist, so the next posting's step 5 inserts nothing and takes no FK lock on Locations.
        await PostingTests.ReceiveAsync(client, location, item, 1m, 10m, ct);
        var key = PostingTests.NewKey();

        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await DeactivateLocationAsync(holder, tx, location, ct);

        // RCSI shows the location as active at step 1; step 7's READCOMMITTEDLOCK re-read waits for the holder.
        var request = Task.Run(() => PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, key, PostingTests.Receipt(location, PostingTests.Line(item, 1m, 10m)), ct), ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        await tx.CommitAsync(ct);
        var response = await request;

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        problem["type"]!.GetValue<string>().ShouldBe("/problems/inactive-location");
        problem["locationId"]!.GetValue<Guid>().ShouldBe(location);
        (await DocumentsWithKeyAsync(holder, key, ct)).ShouldBe(0);
        (await PostingTests.BalanceAsync(holder, item, location, ct)).ShouldBe((1m, 10m));
    }

    [Fact]
    public async Task Location_deactivation_rolled_back_mid_posting_lets_it_post()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 1m, 10m, ct);
        var key = PostingTests.NewKey();

        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await DeactivateLocationAsync(holder, tx, location, ct);

        var request = Task.Run(() => PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, key, PostingTests.Receipt(location, PostingTests.Line(item, 1m, 10m)), ct), ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        await tx.RollbackAsync(ct);
        var response = await request;

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await DocumentsWithKeyAsync(holder, key, ct)).ShouldBe(1);
        (await PostingTests.BalanceAsync(holder, item, location, ct)).ShouldBe((2m, 20m));
    }

    [Fact]
    public async Task Retry_budget_is_three()
    {
        await using var app = db.CreateApp();
        await using var scope = app.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var strategy = inventory.Database.CreateExecutionStrategy();

        // Spec §6: a deadlock (1205) retries up to 3 times. Aspire's own strategy, with 6, must be the one switched off.
        strategy.ShouldBeOfType<SqlServerRetryingExecutionStrategy>();
        ((ExecutionStrategy)strategy).MaxRetryCount.ShouldBe(3);
    }

    [Fact]
    public async Task Deadlocked_posting_is_retried_and_posts_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var retries = new RetryLog();
        await using var app = new ApiFactory(db.AppConnectionString) { Retries = retries };
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var first = await PostingTests.NewLocationAsync(client, ct);
        var second = await PostingTests.NewLocationAsync(client, ct, "WH-2");
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);

        // (X, SUP) now exists. (X, L2) doesn't, so the receipt below inserts it at step 5 and holds its X lock.
        await PostingTests.ReceiveAsync(client, first, item, 1m, 10m, ct);
        var key = PostingTests.NewKey();

        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        var supplier = await LedgerRows.LocationIdAsync(holder, "SUP", ct);
        await using (var priority = new SqlCommand("SET DEADLOCK_PRIORITY HIGH;", holder) { CommandTimeout = 60 })
        {
            await priority.ExecuteNonQueryAsync(ct);
        }

        await using var tx = holder.BeginTransaction();
        (await LockBalanceAsync(holder, tx, item, supplier, ct)).ShouldBe(-1m);

        // Step 6 waits for the holder's U lock on (X, SUP) while holding (X, L2).
        var request = Task.Run(() => PostingTests.PostDocumentAsync(
            client, PostingTests.Receipts, key, PostingTests.Receipt(second, PostingTests.Line(item, 1m, 10m)), ct), ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);

        // Asking for (X, L2) closes the cycle. The deadlock monitor picks the posting, whose priority is lower, and its
        // rollback removes (X, L2): so this returns no row, and only once the deadlock is resolved. The execution strategy
        // then retries the posting, which waits on the holder again.
        (await LockBalanceAsync(holder, tx, item, second, ct)).ShouldBeNull();
        await tx.RollbackAsync(ct);
        var response = await request;

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        retries.Count.ShouldBeGreaterThanOrEqualTo(1);
        (await DocumentsWithKeyAsync(holder, key, ct)).ShouldBe(1);
        (await PostingTests.BalanceAsync(holder, item, second, ct)).ShouldBe((1m, 10m));
        (await PostingTests.BalanceAsync(holder, item, first, ct)).ShouldBe((1m, 10m));
    }

    [Fact]
    public async Task Fifty_parallel_transfers_both_ways_never_deadlock()
    {
        var ct = TestContext.Current.CancellationToken;
        var retries = new RetryLog();
        await using var app = new ApiFactory(db.AppConnectionString) { Retries = retries };
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "WH-A");
        var b = await PostingTests.NewLocationAsync(client, ct, "WH-B");
        Guid[] items =
        [
            await PostingTests.NewItemAsync(client, "RICE-1", ct),
            await PostingTests.NewItemAsync(client, "RICE-2", ct),
            await PostingTests.NewItemAsync(client, "RICE-3", ct),
        ];
        decimal[] costsAtA = [10.00m, 20.00m, 30.00m];
        decimal[] costsAtB = [12.50m, 21.75m, 33.10m];

        // Every touched balance exists before the race, so step 5 inserts nothing and only step 6's lock path is tested.
        for (var i = 0; i < items.Length; i++)
        {
            await PostingTests.ReceiveAsync(client, a, items[i], 100m, costsAtA[i], ct);
            await PostingTests.ReceiveAsync(client, b, items[i], 100m, costsAtB[i], ct);
        }

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var initialValues = new decimal[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            initialValues[i] = (await PostingTests.BalanceAsync(connection, items[i], a, ct))!.Value.StockValue
                + (await PostingTests.BalanceAsync(connection, items[i], b, ct))!.Value.StockValue;
        }

        var retriesBefore = retries.Count;

        // Every transfer locks the same six balances in clustered-key order (TenantId, ItemId, LocationId). The holder
        // U-locks the fourth of them, so the first transfer to reach step 6 takes the three before it and waits, and the
        // rest queue behind it: all 50 are then inside SQL Server together, and can't pass by running one after another.
        // Released together, any two that took their locks in different orders would deadlock, and the retry would show.
        var middle = items
            .SelectMany(item => new[] { (Item: item, Location: a), (Item: item, Location: b) })
            .OrderBy(k => new SqlGuid(k.Item))
            .ThenBy(k => new SqlGuid(k.Location))
            .ElementAt(3);
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        (await LockBalanceAsync(holder, tx, middle.Item, middle.Location, ct)).ShouldBe(100m);

        // Even sends go A→B in line order [1, 2, 3], odd sends B→A in [3, 2, 1]: the request's line order can't be what
        // keeps the locks ordered.
        var sending = InParallelAsync(50, index => PostingTests.PostDocumentAsync(client, TransferTests.Transfers, PostingTests.NewKey(),
            index % 2 == 0
                ? TransferTests.Transfer(a, b, TransferTests.TransferLine(items[0], 1m), TransferTests.TransferLine(items[1], 1m), TransferTests.TransferLine(items[2], 1m))
                : TransferTests.Transfer(b, a, TransferTests.TransferLine(items[2], 1m), TransferTests.TransferLine(items[1], 1m), TransferTests.TransferLine(items[0], 1m)),
            ct));
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        (await WaitUntilQueuedAsync(holder, tx, 50, ct)).ShouldBe(50, "not every transfer was waiting at step 6 when the holder let go");
        await tx.RollbackAsync(ct);
        var responses = await sending;

        // 50 × 201, so no 409 concurrency-conflict and no 500.
        Count(responses, HttpStatusCode.Created).ShouldBe(50, Statuses(responses));
        (retries.Count - retriesBefore).ShouldBe(0, "a deadlock was retried: the step-6 lock order didn't hold");

        for (var i = 0; i < items.Length; i++)
        {
            var atA = (await PostingTests.BalanceAsync(connection, items[i], a, ct))!.Value;
            var atB = (await PostingTests.BalanceAsync(connection, items[i], b, ct))!.Value;
            atA.OnHand.ShouldBe(100m);
            atB.OnHand.ShouldBe(100m);

            // Transfers move value between A and B, never create or destroy it.
            (atA.StockValue + atB.StockValue).ShouldBe(initialValues[i]);
        }

        await LedgerInvariants.AssertHoldAsync(connection, ct);
        (await DocumentsOfTypeAsync(connection, "Transfer", ct)).ShouldBe(50);
        (await MovementsOfTypeAsync(connection, "Transfer", ct)).ShouldBe(150);
    }

    /// <summary>Starts every send parked behind one gate, then opens it, so the requests reach the API together.</summary>
    private static Task<HttpResponseMessage[]> InParallelAsync(int count, Func<Task<HttpResponseMessage>> send) =>
        InParallelAsync(count, _ => send());

    /// <summary>As above, passing each send its index, so the sends can differ.</summary>
    private static async Task<HttpResponseMessage[]> InParallelAsync(int count, Func<int, Task<HttpResponseMessage>> send)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = Enumerable.Range(0, count)
            .Select(async index =>
            {
                await gate.Task;
                return await send(index);
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

    private static async Task DeactivateLocationAsync(SqlConnection holder, SqlTransaction tx, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand("UPDATE inventory.Locations SET IsActive = 0 WHERE Id = @l", holder, tx);
        command.Parameters.Add(new SqlParameter("@l", SqlDbType.UniqueIdentifier) { Value = location });
        (await command.ExecuteNonQueryAsync(ct)).ShouldBe(1);
    }

    /// <summary>
    /// U-locks the balance row in the holder's transaction and returns its <c>OnHand</c>, or null when there is no row.
    /// The 60 s timeout covers the deadlock monitor, which can take about 5 s to break a cycle.
    /// </summary>
    private static async Task<decimal?> LockBalanceAsync(SqlConnection holder, SqlTransaction tx, Guid item, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT OnHand FROM inventory.StockBalances WITH (UPDLOCK, ROWLOCK) WHERE ItemId = @x AND LocationId = @l", holder, tx)
        {
            CommandTimeout = 60,
        };
        command.Parameters.Add(new SqlParameter("@x", SqlDbType.UniqueIdentifier) { Value = item });
        command.Parameters.Add(new SqlParameter("@l", SqlDbType.UniqueIdentifier) { Value = location });
        return (decimal?)await command.ExecuteScalarAsync(ct);
    }

    /// <summary>
    /// Waits (10 s at most) until <paramref name="count"/> requests wait on <paramref name="holder"/>, directly or behind
    /// another waiter, polling sys.dm_exec_requests as sa; returns the last count seen.
    /// </summary>
    private async Task<int> WaitUntilQueuedAsync(SqlConnection holder, SqlTransaction tx, int count, CancellationToken ct)
    {
        int spid;
        await using (var command = new SqlCommand("SELECT @@SPID", holder, tx))
        {
            spid = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }

        await using var admin = new SqlConnection(db.AdminConnectionString);
        await admin.OpenAsync(ct);
        await using var poll = new SqlCommand("""
            WITH waiters AS (
                SELECT session_id FROM sys.dm_exec_requests WHERE blocking_session_id = @spid
                UNION ALL
                SELECT r.session_id FROM sys.dm_exec_requests AS r JOIN waiters AS w ON r.blocking_session_id = w.session_id)
            SELECT COUNT(DISTINCT session_id) FROM waiters;
            """, admin);
        poll.Parameters.Add(new SqlParameter("@spid", SqlDbType.Int) { Value = spid });

        var waited = Stopwatch.StartNew();
        var queued = 0;
        while (waited.Elapsed < TimeSpan.FromSeconds(10) && queued < count)
        {
            queued = (int)(await poll.ExecuteScalarAsync(ct))!;
            if (queued < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
            }
        }

        return queued;
    }

    private static Task<int> DocumentsWithKeyAsync(SqlConnection connection, string key, CancellationToken ct) =>
        PostingTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE IdempotencyKey = @p",
            new SqlParameter("@p", SqlDbType.VarChar, 64) { Value = key }, ct);

    /// <summary>The tenant's documents of this type: RLS limits the connection to the tenant in its session context.</summary>
    private static Task<int> DocumentsOfTypeAsync(SqlConnection connection, string type, CancellationToken ct) =>
        PostingTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.StockDocuments WHERE Type = @p",
            new SqlParameter("@p", SqlDbType.VarChar, 16) { Value = type }, ct);

    private static Task<int> MovementsOfDocumentAsync(SqlConnection connection, Guid document, CancellationToken ct) =>
        PostingTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM inventory.StockMovements WHERE DocumentId = @p",
            new SqlParameter("@p", SqlDbType.UniqueIdentifier) { Value = document }, ct);

    private static Task<int> MovementsOfTypeAsync(SqlConnection connection, string type, CancellationToken ct) =>
        PostingTests.ScalarAsync<int>(connection, """
            SELECT COUNT(*) FROM inventory.StockMovements AS m
            JOIN inventory.StockDocuments AS d ON d.Id = m.DocumentId
            WHERE d.Type = @p
            """,
            new SqlParameter("@p", SqlDbType.VarChar, 16) { Value = type }, ct);

    private static async Task<Guid> IdAsync(HttpResponseMessage response, CancellationToken ct) =>
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();

    private static int Count(HttpResponseMessage[] responses, HttpStatusCode status) => responses.Count(r => r.StatusCode == status);

    private static string Statuses(HttpResponseMessage[] responses) =>
        string.Join(", ", responses.GroupBy(r => (int)r.StatusCode).OrderBy(g => g.Key).Select(g => $"{g.Key} × {g.Count()}"));
}