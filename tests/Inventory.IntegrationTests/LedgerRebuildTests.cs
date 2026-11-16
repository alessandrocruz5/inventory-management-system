using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// S11's <c>POST /api/ledger/rebuild</c>: Admin only, an empty diff and no writes on a healthy ledger, each kind of drift
/// returned and overwritten, and the ledger lock in Exclusive mode against postings in both directions.
/// </summary>
public sealed class LedgerRebuildTests(DatabaseFixture db)
{
    private const string Rebuild = "/api/ledger/rebuild";

    [Fact]
    public async Task Healthy_ledger_rebuild_returns_an_empty_diff_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var b = await PostingTests.NewLocationAsync(client, ct, "B");
        var p = await PostingTests.NewItemAsync(client, "P", ct);

        await PostAsync(PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(p, 10m, 100.00m)));
        await PostAsync(PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(p, 5m, 120.00m)));
        await PostAsync(PostingTests.Issues, PostingTests.Issue(a, PostingTests.IssueLine(p, 3m)));
        await PostAsync(TransferTests.Transfers, TransferTests.Transfer(a, b, TransferTests.TransferLine(p, 4m)));
        await PostAsync(AdjustmentTests.Adjustments, AdjustmentTests.Adjustment(a, "Found", AdjustmentTests.Delta(p, 2m)));
        var receipt = await PostAsync(PostingTests.Receipts, PostingTests.Receipt(b, PostingTests.Line(p, 6m, 125.00m)));
        (await ReversalTests.ReverseAsync(client, receipt, PostingTests.NewKey(), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var before = await RowVersionsAsync(connection, ct);

        var rebuild = await RebuildAsync(client, ct);

        rebuild["differences"]!.AsArray().ShouldBeEmpty();
        rebuild["balancesChecked"]!.GetValue<int>().ShouldBe(before.Count);
        (await RowVersionsAsync(connection, ct)).ShouldBe(before);

        async Task<Guid> PostAsync(string route, object body)
        {
            var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return await ReversalTests.IdAsync(response, ct);
        }
    }

    [Fact]
    public async Task Rebuild_fixes_a_drifted_physical_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "D-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var lastMovementId = (await LedgerInvariants.MovementsAsync(connection, ct)).Max(m => m.Id);

        // All four values drift; the row stays within the CHECKs.
        await UpdateBalanceAsync(connection, "OnHand = 6, StockValue = 61, LastUnitCost = 1.2345, LastMovementId = NULL", tenant, item, a, ct);

        var rebuild = await RebuildAsync(client, ct);

        var difference = rebuild["differences"]!.AsArray().ShouldHaveSingleItem()!;
        difference["itemId"]!.GetValue<Guid>().ShouldBe(item);
        difference["sku"]!.GetValue<string>().ShouldBe("D-1");
        difference["locationId"]!.GetValue<Guid>().ShouldBe(a);
        difference["locationCode"]!.GetValue<string>().ShouldBe("A");
        difference["inserted"]!.GetValue<bool>().ShouldBeFalse();
        Snapshot(difference["before"]!).ShouldBe((6m, 61m, (decimal?)1.2345m, (long?)null));
        Snapshot(difference["after"]!).ShouldBe((5m, 50m, (decimal?)10m, (long?)lastMovementId));
        (await StoredAsync(connection, item, a, ct)).ShouldBe((5m, 50m, (decimal?)10m, (long?)lastMovementId, false, false));
        await AssertHealthyAsync(client, ct);
        (await RebuildAsync(client, ct))["differences"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Rebuild_writes_four_decimal_values_back_exactly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "F-1", ct);

        // 1.5 x 1.2346 = 1.8519 exactly, so every ledger-derived value is a true 4-decimal one: a decimal(18,2) or
        // otherwise rounded parameter would write 1.50 / 1.85 / 1.23 instead.
        await PostingTests.ReceiveAsync(client, a, item, 1.5m, 1.2346m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var lastMovementId = (await LedgerInvariants.MovementsAsync(connection, ct)).Max(m => m.Id);
        (await StoredAsync(connection, item, a, ct)).ShouldBe((1.5m, 1.8519m, (decimal?)1.2346m, (long?)lastMovementId, false, false));

        await UpdateBalanceAsync(connection, "OnHand = 9, StockValue = 99, LastUnitCost = 7, LastMovementId = NULL", tenant, item, a, ct);

        var rebuild = await RebuildAsync(client, ct);

        var difference = rebuild["differences"]!.AsArray().ShouldHaveSingleItem()!;
        Snapshot(difference["before"]!).ShouldBe((9m, 99m, (decimal?)7m, (long?)null));
        Snapshot(difference["after"]!).ShouldBe((1.5m, 1.8519m, (decimal?)1.2346m, (long?)lastMovementId));
        (await StoredAsync(connection, item, a, ct)).ShouldBe((1.5m, 1.8519m, (decimal?)1.2346m, (long?)lastMovementId, false, false));
        await AssertHealthyAsync(client, ct);
    }

    [Fact]
    public async Task Rebuild_resets_an_empty_ADJ_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "E-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var adjustment = await LedgerRows.LocationIdAsync(connection, "ADJ", ct);

        // Step 5 created the (item, ADJ) row with no movements; a virtual row keeps its value at 0.
        await UpdateBalanceAsync(connection, "OnHand = 5", tenant, item, adjustment, ct);

        var rebuild = await RebuildAsync(client, ct);

        var difference = rebuild["differences"]!.AsArray().ShouldHaveSingleItem()!;
        difference["locationId"]!.GetValue<Guid>().ShouldBe(adjustment);
        difference["locationCode"]!.GetValue<string>().ShouldBe("ADJ");
        difference["inserted"]!.GetValue<bool>().ShouldBeFalse();
        Snapshot(difference["before"]!).ShouldBe((5m, 0m, (decimal?)null, (long?)null));
        Snapshot(difference["after"]!).ShouldBe((0m, 0m, (decimal?)null, (long?)null));
        (await StoredAsync(connection, item, adjustment, ct)).ShouldBe((0m, 0m, (decimal?)null, (long?)null, true, false));
        await AssertHealthyAsync(client, ct);
    }

    [Fact]
    public async Task Rebuild_inserts_a_missing_balance_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "M-1", ct);
        await PostingTests.ReceiveAsync(client, a, item, 5m, 10m, ct);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var supplier = await LedgerRows.LocationIdAsync(connection, "SUP", ct);
        var lastMovementId = (await LedgerInvariants.MovementsAsync(connection, ct)).Max(m => m.Id);

        // As the owner: inventory_app can't delete. RLS applies to the owner too, so it sets the same session context.
        await using (var owner = new SqlConnection(new SqlConnectionStringBuilder(db.AdminConnectionString) { Pooling = false }.ConnectionString))
        {
            await owner.OpenAsync(ct);
            await using (var context = new SqlCommand("EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1;", owner))
            {
                context.Parameters.Add(new SqlParameter("@t", SqlDbType.UniqueIdentifier) { Value = tenant });
                await context.ExecuteNonQueryAsync(ct);
            }

            await using var delete = new SqlCommand(
                "DELETE FROM inventory.StockBalances WHERE TenantId = @t AND ItemId = @i AND LocationId = @l", owner);
            delete.Parameters.Add(new SqlParameter("@t", SqlDbType.UniqueIdentifier) { Value = tenant });
            delete.Parameters.Add(new SqlParameter("@i", SqlDbType.UniqueIdentifier) { Value = item });
            delete.Parameters.Add(new SqlParameter("@l", SqlDbType.UniqueIdentifier) { Value = supplier });
            (await delete.ExecuteNonQueryAsync(ct)).ShouldBe(1);
        }

        var rebuild = await RebuildAsync(client, ct);

        var difference = rebuild["differences"]!.AsArray().ShouldHaveSingleItem()!;
        difference["itemId"]!.GetValue<Guid>().ShouldBe(item);
        difference["locationId"]!.GetValue<Guid>().ShouldBe(supplier);
        difference["locationCode"]!.GetValue<string>().ShouldBe("SUP");
        difference["inserted"]!.GetValue<bool>().ShouldBeTrue();
        difference["before"].ShouldBeNull();
        Snapshot(difference["after"]!).ShouldBe((-5m, 0m, (decimal?)null, (long?)lastMovementId));
        (await StoredAsync(connection, item, supplier, ct)).ShouldBe((-5m, 0m, (decimal?)null, (long?)lastMovementId, true, false));
        await AssertHealthyAsync(client, ct);
    }

    [Fact]
    public async Task Rebuild_waits_for_a_posting_that_holds_the_ledger_lock()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await TakeLedgerLockAsync(holder, tx, tenant, "Shared", ct);

        var rebuild = client.PostAsync(Rebuild, null, ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        tx.Commit();

        (await rebuild).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Posting_waits_while_the_ledger_lock_is_exclusive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(client, ct, "A");
        var item = await PostingTests.NewItemAsync(client, "W-1", ct);
        await using var holder = await LedgerRows.OpenAsync(db, tenant, ct);
        await using var tx = holder.BeginTransaction();
        await TakeLedgerLockAsync(holder, tx, tenant, "Exclusive", ct);

        // The engine and LedgerLock must agree on the resource name, or the receipt would not wait.
        var receipt = PostingTests.PostDocumentAsync(client, PostingTests.Receipts, PostingTests.NewKey(),
            PostingTests.Receipt(a, PostingTests.Line(item, 5m, 10m)), ct);
        await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx);
        tx.Commit();

        (await receipt).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Staff_gets_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var admin = await DatabaseFixture.SignInAsync(app, email);
        var a = await PostingTests.NewLocationAsync(admin, ct, "A");
        var item = await PostingTests.NewItemAsync(admin, "S-1", ct);
        await PostingTests.ReceiveAsync(admin, a, item, 5m, 10m, ct);
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        await UpdateBalanceAsync(connection, "OnHand = 6, StockValue = 61", tenant, item, a, ct);
        var before = await RowVersionsAsync(connection, ct);

        var response = await staff.PostAsync(Rebuild, null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RowVersionsAsync(connection, ct)).ShouldBe(before);
        (await StoredAsync(connection, item, a, ct))!.Value.OnHand.ShouldBe(6m);
    }

    internal static async Task<JsonNode> RebuildAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.PostAsync(Rebuild, null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
    }

    internal static async Task AssertHealthyAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/ledger/health", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var health = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        health["healthy"]!.GetValue<bool>().ShouldBeTrue(health.ToJsonString());
    }

    /// <summary>Every visible balance's <c>RowVersion</c>, keyed by (ItemId, LocationId); the connection's tenant only.</summary>
    internal static async Task<Dictionary<(Guid ItemId, Guid LocationId), string>> RowVersionsAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT ItemId, LocationId, CAST(RowVersion AS binary(8)) FROM inventory.StockBalances", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var versions = new Dictionary<(Guid, Guid), string>();
        while (await reader.ReadAsync(ct))
        {
            versions[(reader.GetGuid(0), reader.GetGuid(1))] = Convert.ToHexString((byte[])reader[2]);
        }

        return versions;
    }

    /// <summary>The balance row's four values and its two mirrors, or null when the row doesn't exist.</summary>
    private static async Task<(decimal OnHand, decimal StockValue, decimal? LastUnitCost, long? LastMovementId, bool IsVirtual, bool AllowNegative)?> StoredAsync(
        SqlConnection connection, Guid item, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT OnHand, StockValue, LastUnitCost, LastMovementId, IsVirtual, AllowNegative
            FROM inventory.StockBalances WHERE ItemId = @i AND LocationId = @l
            """, connection);
        command.Parameters.Add(new SqlParameter("@i", SqlDbType.UniqueIdentifier) { Value = item });
        command.Parameters.Add(new SqlParameter("@l", SqlDbType.UniqueIdentifier) { Value = location });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return (
            reader.GetDecimal(0),
            reader.GetDecimal(1),
            reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5));
    }

    private static (decimal OnHand, decimal StockValue, decimal? LastUnitCost, long? LastMovementId) Snapshot(JsonNode snapshot) => (
        snapshot["onHand"]!.GetValue<decimal>(),
        snapshot["stockValue"]!.GetValue<decimal>(),
        snapshot["lastUnitCost"]?.GetValue<decimal>(),
        snapshot["lastMovementId"]?.GetValue<long>());

    /// <summary>Takes the tenant's ledger applock in <paramref name="mode"/>, owned by <paramref name="tx"/>, as a posting would.</summary>
    private static async Task TakeLedgerLockAsync(SqlConnection holder, SqlTransaction tx, Guid tenant, string mode, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            DECLARE @r int;
            EXEC @r = sp_getapplock @Resource = @res, @LockMode = @mode, @LockOwner = 'Transaction';
            SELECT @r;
            """, holder, tx);
        command.Parameters.Add(new SqlParameter("@res", SqlDbType.NVarChar, 255) { Value = $"ledger:{tenant:D}" });
        command.Parameters.Add(new SqlParameter("@mode", SqlDbType.VarChar, 32) { Value = mode });
        ((int)(await command.ExecuteScalarAsync(ct))!).ShouldBeGreaterThanOrEqualTo(0);
    }

    /// <summary>Sets <paramref name="assignments"/> on one balance row through the tenant's raw connection.</summary>
    internal static async Task UpdateBalanceAsync(
        SqlConnection connection, string assignments, Guid tenant, Guid item, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            $"UPDATE inventory.StockBalances SET {assignments} WHERE TenantId = @t AND ItemId = @i AND LocationId = @l", connection);
        command.Parameters.Add(new SqlParameter("@t", SqlDbType.UniqueIdentifier) { Value = tenant });
        command.Parameters.Add(new SqlParameter("@i", SqlDbType.UniqueIdentifier) { Value = item });
        command.Parameters.Add(new SqlParameter("@l", SqlDbType.UniqueIdentifier) { Value = location });
        (await command.ExecuteNonQueryAsync(ct)).ShouldBe(1);
    }
}