using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>T1's API run of vector sets R, A and V (spec §7): the costing rounding, the adjustment average, the remainder rule, and the transfer, reversal and rejection steps, through the real routes.</summary>
public sealed class PostingVectorTests(DatabaseFixture db)
{
    [Fact]
    public async Task Set_R_rounds_away_from_zero_and_issues_the_remainder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var c = (await LocationsTests.CreateAsync(client, "C", "Location C", ct))["id"]!.GetValue<Guid>();
        var item = (await ItemsTests.CreateAsync(client, "R-1", "R-1", "pc", ct))["id"]!.GetValue<Guid>();
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        (string Step, string Route, object Body, decimal Cost, decimal OnHand, decimal StockValue)[] steps =
        [
            ("R1", PostingTests.Receipts, PostingTests.Receipt(c, PostingTests.Line(item, 1m, 10.00m)), 10.0000m, 1m, 10.0000m),
            ("R2", PostingTests.Receipts, PostingTests.Receipt(c, PostingTests.Line(item, 2m, 10.01m)), 20.0200m, 3m, 30.0200m),
            ("R3", PostingTests.Issues, PostingTests.Issue(c, PostingTests.IssueLine(item, 1m)), 10.0067m, 2m, 20.0133m),

            // 20.0133 ÷ 2 = 10.00665: the midpoint rounds away from zero. Banker's rounding would give 10.0066.
            ("R4", PostingTests.Issues, PostingTests.Issue(c, PostingTests.IssueLine(item, 1m)), 10.0067m, 1m, 10.0066m),

            // The last unit takes the remainder, not the average.
            ("R5", PostingTests.Issues, PostingTests.Issue(c, PostingTests.IssueLine(item, 1m)), 10.0066m, 0m, 0.0000m),
        ];

        var costs = new Dictionary<string, decimal>();
        foreach (var (step, route, body, cost, onHand, stockValue) in steps)
        {
            var response = await PostingTests.PostDocumentAsync(client, route, Guid.NewGuid().ToString("N"), body, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Created, step);
            var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            var movementCost = document["movements"]!.AsArray().ShouldHaveSingleItem()!["totalCost"]!.GetValue<decimal>();
            movementCost.ShouldBe(cost, step);
            var balance = document["balances"]!.AsArray().ShouldHaveSingleItem()!;
            balance["locationId"]!.GetValue<Guid>().ShouldBe(c, step);
            balance["onHand"]!.GetValue<decimal>().ShouldBe(onHand, step);
            balance["stockValue"]!.GetValue<decimal>().ShouldBe(stockValue, step);
            (await PostingTests.BalanceAsync(connection, item, c, ct)).ShouldBe((onHand, stockValue), step);
            costs[step] = movementCost;
        }

        // Spec §7: everything received is issued, to the last ten-thousandth.
        var received = costs["R1"] + costs["R2"];
        var issued = costs["R3"] + costs["R4"] + costs["R5"];
        issued.ShouldBe(30.0200m);
        issued.ShouldBe(received);
    }

    [Fact]
    public async Task Set_A_adjusts_at_the_average_and_takes_the_remainder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var d = (await LocationsTests.CreateAsync(client, "D", "Location D", ct))["id"]!.GetValue<Guid>();
        var item = (await ItemsTests.CreateAsync(client, "A-1", "A-1", "pc", ct))["id"]!.GetValue<Guid>();
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        (string Step, string Route, object Body, decimal? Cost, decimal OnHand, decimal StockValue)[] steps =
        [
            ("A1", PostingTests.Receipts, PostingTests.Receipt(d, PostingTests.Line(item, 1m, 10.00m)), 10.0000m, 1m, 10.0000m),
            ("A2", PostingTests.Receipts, PostingTests.Receipt(d, PostingTests.Line(item, 2m, 10.01m)), 20.0200m, 3m, 30.0200m),

            // 3 × 30.02 ÷ 3; rounding the average first gives 30.0201, which is wrong.
            ("A3", AdjustmentTests.Adjustments, AdjustmentTests.Adjustment(d, "Found", AdjustmentTests.Delta(item, 3m)), 30.0200m, 6m, 60.0400m),
            ("A4", AdjustmentTests.Adjustments, AdjustmentTests.Adjustment(d, "Lost", AdjustmentTests.Delta(item, -1m)), 10.0067m, 5m, 50.0333m),
            ("A5", AdjustmentTests.Adjustments, AdjustmentTests.Adjustment(d, "Found", AdjustmentTests.Delta(item, 2m, 12.50m)), 25.0000m, 7m, 75.0333m),

            // The last units take the remainder, not the average.
            ("A6", AdjustmentTests.Adjustments, AdjustmentTests.Adjustment(d, "Damaged", AdjustmentTests.Delta(item, -7m)), 75.0333m, 0m, 0.0000m),

            // Nothing is on hand, so a positive line needs a cost: the step is rejected and the balance is unchanged.
            ("A7", AdjustmentTests.Adjustments, AdjustmentTests.Adjustment(d, "Found", AdjustmentTests.Delta(item, 1m)), null, 0m, 0.0000m),
        ];

        var costs = new Dictionary<string, decimal>();
        foreach (var (step, route, body, cost, onHand, stockValue) in steps)
        {
            var response = await PostingTests.PostDocumentAsync(client, route, Guid.NewGuid().ToString("N"), body, ct);

            if (cost is null)
            {
                await PostingTests.AssertValidationAsync(response, "lines[0].unitCost", ct);
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Created, step);
                var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
                var movementCost = document["movements"]!.AsArray().ShouldHaveSingleItem()!["totalCost"]!.GetValue<decimal>();
                movementCost.ShouldBe(cost.Value, step);
                var balance = document["balances"]!.AsArray().ShouldHaveSingleItem()!;
                balance["locationId"]!.GetValue<Guid>().ShouldBe(d, step);
                balance["onHand"]!.GetValue<decimal>().ShouldBe(onHand, step);
                balance["stockValue"]!.GetValue<decimal>().ShouldBe(stockValue, step);
                costs[step] = movementCost;
            }

            (await PostingTests.BalanceAsync(connection, item, d, ct)).ShouldBe((onHand, stockValue), step);
        }

        // Spec §7: everything that came in went out, to the last ten-thousandth.
        var received = costs["A1"] + costs["A2"] + costs["A3"] + costs["A5"];
        var removed = costs["A4"] + costs["A6"];
        received.ShouldBe(85.0400m);
        removed.ShouldBe(85.0400m);
        removed.ShouldBe(received);
    }

    [Fact]
    public async Task Set_V_moves_reverses_and_rejects_through_the_api()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var a = (await LocationsTests.CreateAsync(client, "A", "Location A", ct))["id"]!.GetValue<Guid>();
        var b = (await LocationsTests.CreateAsync(client, "B", "Location B", ct))["id"]!.GetValue<Guid>();
        var item = (await ItemsTests.CreateAsync(client, "V-1", "V-1", "pc", ct))["id"]!.GetValue<Guid>();
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        var sup = await LedgerRows.LocationIdAsync(connection, "SUP", ct);

        await PostAsync("V1", PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 10m, 100.00m)), 1000.0000m);
        await AssertBalancesAsync("V1", (10m, 1000.0000m, null), null);
        await PostAsync("V2", PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 10m, 120.00m)), 1200.0000m);
        await AssertBalancesAsync("V2", (20m, 2200.0000m, null), null);
        await PostAsync("V3", PostingTests.Issues, PostingTests.Issue(a, PostingTests.IssueLine(item, 15m)), 1650.0000m);
        await AssertBalancesAsync("V3", (5m, 550.0000m, null), null);
        var v4 = await PostAsync("V4", PostingTests.Receipts, PostingTests.Receipt(a, PostingTests.Line(item, 5m, 130.00m)), 650.0000m);
        await AssertBalancesAsync("V4", (10m, 1200.0000m, 120.0000m), null);
        var v5 = await PostAsync("V5", TransferTests.Transfers, TransferTests.Transfer(a, b, TransferTests.TransferLine(item, 4m)), 480.0000m);
        await AssertBalancesAsync("V5", (6m, 720.0000m, null), (4m, 480.0000m, null));
        var v6 = await PostAsync("V6", PostingTests.Receipts, PostingTests.Receipt(b, PostingTests.Line(item, 6m, 125.00m)), 750.0000m);
        await AssertBalancesAsync("V6", (6m, 720.0000m, null), (10m, 1230.0000m, 123.0000m));

        // V7: the reversal takes the receipt back out of B at its original cost.
        var reversal = await ReversalTests.ReverseAsync(client, v6, PostingTests.NewKey(), ct);
        reversal.StatusCode.ShouldBe(HttpStatusCode.Created, "V7");
        var reversed = (await reversal.Content.ReadFromJsonAsync<JsonNode>(ct))!["movements"]!.AsArray().ShouldHaveSingleItem()!;
        reversed["totalCost"]!.GetValue<decimal>().ShouldBe(750.0000m, "V7");
        reversed["fromLocationId"]!.GetValue<Guid>().ShouldBe(b, "V7");
        reversed["toLocationId"]!.GetValue<Guid>().ShouldBe(sup, "V7");
        await AssertBalancesAsync("V7", (6m, 720.0000m, null), (4m, 480.0000m, 120.0000m));

        // V8: the transfer V5 touched A after V4 was posted.
        var blocked = await ReversalTests.ReverseAsync(client, v4, PostingTests.NewKey(), ct);
        await ReversalTests.AssertProblemAsync(blocked, HttpStatusCode.Conflict, "reversal-not-latest", ct);
        var blockedProblem = await ReversalTests.ProblemAsync(blocked, ct);
        blockedProblem["locationId"]!.GetValue<Guid>().ShouldBe(a, "V8");
        blockedProblem["blockingDocumentId"]!.GetValue<Guid>().ShouldBe(v5, "V8");
        await AssertBalancesAsync("V8", (6m, 720.0000m, null), (4m, 480.0000m, null));

        // V9: only 6 are on hand at A.
        var tooMany = await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(a, PostingTests.IssueLine(item, 7m)), ct);
        await PostingTests.AssertInsufficientStockAsync(tooMany, item, a, 6m, 7m, ct);
        await AssertBalancesAsync("V9", (6m, 720.0000m, null), (4m, 480.0000m, null));

        // V10: the last 6 take the remainder.
        await PostAsync("V10", PostingTests.Issues, PostingTests.Issue(a, PostingTests.IssueLine(item, 6m)), 720.0000m);
        await AssertBalancesAsync("V10", (0m, 0.0000m, null), (4m, 480.0000m, null));

        await LedgerInvariants.AssertHoldAsync(connection, ct);

        async Task<Guid> PostAsync(string step, string route, object body, decimal cost)
        {
            var response = await PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, step);
            var document = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            document["movements"]!.AsArray().ShouldHaveSingleItem()!["totalCost"]!.GetValue<decimal>().ShouldBe(cost, step);
            return document["id"]!.GetValue<Guid>();
        }

        // lastUnitCost is checked only where the table names it.
        async Task AssertBalancesAsync(string step, (decimal OnHand, decimal StockValue, decimal? LastUnitCost) atA,
            (decimal OnHand, decimal StockValue, decimal? LastUnitCost)? atB)
        {
            var balanceA = (await ReversalTests.BalanceAsync(connection, item, a, ct)).ShouldNotBeNull(step);
            (balanceA.OnHand, balanceA.StockValue).ShouldBe((atA.OnHand, atA.StockValue), $"{step} A");
            if (atA.LastUnitCost is not null)
            {
                balanceA.LastUnitCost.ShouldBe(atA.LastUnitCost, $"{step} A");
            }

            var balanceB = await ReversalTests.BalanceAsync(connection, item, b, ct);
            if (atB is null)
            {
                balanceB.ShouldBeNull(step);
                return;
            }

            balanceB.ShouldNotBeNull(step);
            (balanceB.Value.OnHand, balanceB.Value.StockValue).ShouldBe((atB.Value.OnHand, atB.Value.StockValue), $"{step} B");
            if (atB.Value.LastUnitCost is not null)
            {
                balanceB.Value.LastUnitCost.ShouldBe(atB.Value.LastUnitCost, $"{step} B");
            }
        }
    }
}