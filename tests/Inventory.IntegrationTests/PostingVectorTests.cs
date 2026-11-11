using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>T1's API run of vector sets R and A (spec §7): the costing rounding, the adjustment average and the remainder rule, through the real routes.</summary>
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
}