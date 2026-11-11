using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>T1's API run of vector set R (spec §7): the costing rounding and the remainder rule, through the real routes.</summary>
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
}