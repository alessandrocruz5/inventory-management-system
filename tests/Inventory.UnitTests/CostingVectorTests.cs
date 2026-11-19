using Inventory.Domain;

using Shouldly;

namespace Inventory.UnitTests;

/// <summary>
/// T1 unit part (spec §11): vector sets V, R, A, N, X and U of spec §7 :395-467. Must match the spec tables (CLAUDE.md Hidden coupling).
/// </summary>
public sealed class CostingVectorTests
{
    private const string Item = "ITEM";

    private static void Expect(LedgerHarness h, string loc, decimal qty, decimal value, string item = Item)
    {
        var s = h.State(item, loc);
        s.OnHand.ShouldBe(qty, $"{loc} qty");
        s.Value.ShouldBe(value, $"{loc} value");
    }

    private static void ExpectLuc(LedgerHarness h, string loc, decimal luc) =>
        h.State(Item, loc).Luc.ShouldBe(luc, $"{loc} LUC");

    [Fact]
    public void Vector_set_V_two_locations()
    {
        var h = new LedgerHarness();

        var v1 = h.Receive(Item, "A", 10m, 100.00m);
        v1.Movements.Single().TotalCost.ShouldBe(1000.0000m);
        Expect(h, "A", 10m, 1000.0000m);
        ExpectLuc(h, "A", 100m);
        h.AssertInvariants("V1");

        var v2 = h.Receive(Item, "A", 10m, 120.00m);
        v2.Movements.Single().TotalCost.ShouldBe(1200.0000m);
        Expect(h, "A", 20m, 2200.0000m);
        ExpectLuc(h, "A", 110m);
        h.AssertInvariants("V2");

        var v3 = h.Issue(Item, "A", 15m);
        v3.Movements.Single().TotalCost.ShouldBe(1650.0000m);
        Expect(h, "A", 5m, 550.0000m);
        ExpectLuc(h, "A", 110m);
        h.AssertInvariants("V3");

        var v4 = h.Receive(Item, "A", 5m, 130.00m);
        v4.Movements.Single().TotalCost.ShouldBe(650.0000m);
        Expect(h, "A", 10m, 1200.0000m);
        ExpectLuc(h, "A", 120m);
        h.AssertInvariants("V4");

        var v5 = h.Transfer(Item, "A", "B", 4m);
        v5.Movements.Single().TotalCost.ShouldBe(480.0000m);
        Expect(h, "A", 6m, 720.0000m);
        Expect(h, "B", 4m, 480.0000m);
        ExpectLuc(h, "A", 120m);
        ExpectLuc(h, "B", 120m);
        h.AssertInvariants("V5");

        var v6 = h.Receive(Item, "B", 6m, 125.00m);
        v6.Movements.Single().TotalCost.ShouldBe(750.0000m);
        Expect(h, "A", 6m, 720.0000m);
        Expect(h, "B", 10m, 1230.0000m);
        ExpectLuc(h, "B", 123m);
        h.AssertInvariants("V6");

        var v7 = h.Reverse(v6.DocumentId);
        v7.Movements.Single().TotalCost.ShouldBe(750.0000m);
        Expect(h, "A", 6m, 720.0000m);
        Expect(h, "B", 4m, 480.0000m);
        ExpectLuc(h, "B", 120m);
        h.AssertInvariants("V7");

        var movements = h.MovementCount;
        var balances = h.BalanceCount;

        var v8 = Should.Throw<ReversalNotLatestException>(() => h.Reverse(v4.DocumentId));
        v8.ItemId.ShouldBe(h.ItemId(Item));
        v8.LocationId.ShouldBe(h.LocationId("A"));
        v8.BlockingMovementId.ShouldBe(v5.Movements.Single().Id);
        Expect(h, "A", 6m, 720.0000m);
        Expect(h, "B", 4m, 480.0000m);
        h.MovementCount.ShouldBe(movements);
        h.BalanceCount.ShouldBe(balances);
        h.AssertInvariants("V8");

        var v9 = Should.Throw<InsufficientStockException>(() => h.Issue(Item, "A", 7m));
        v9.LocationId.ShouldBe(h.LocationId("A"));
        v9.Available.ShouldBe(6m);
        v9.Requested.ShouldBe(7m);
        Expect(h, "A", 6m, 720.0000m);
        Expect(h, "B", 4m, 480.0000m);
        h.MovementCount.ShouldBe(movements);
        h.BalanceCount.ShouldBe(balances);
        h.AssertInvariants("V9");

        var v10 = h.Issue(Item, "A", 6m);
        v10.Movements.Single().TotalCost.ShouldBe(720.0000m);
        Expect(h, "A", 0m, 0.0000m);
        Expect(h, "B", 4m, 480.0000m);
        h.AssertInvariants("V10");
    }

    [Fact]
    public void Vector_set_R_rounding()
    {
        var h = new LedgerHarness();

        var r1 = h.Receive(Item, "C", 1m, 10.00m);
        r1.Movements.Single().TotalCost.ShouldBe(10.0000m);
        Expect(h, "C", 1m, 10.0000m);
        h.AssertInvariants("R1");

        var r2 = h.Receive(Item, "C", 2m, 10.01m);
        r2.Movements.Single().TotalCost.ShouldBe(20.0200m);
        Expect(h, "C", 3m, 30.0200m);
        h.AssertInvariants("R2");

        var r3 = h.Issue(Item, "C", 1m);
        r3.Movements.Single().TotalCost.ShouldBe(10.0067m);
        Expect(h, "C", 2m, 20.0133m);
        h.AssertInvariants("R3");

        // The midpoint case: banker's rounding would give 10.0066.
        var r4 = h.Issue(Item, "C", 1m);
        r4.Movements.Single().TotalCost.ShouldBe(10.0067m);
        Expect(h, "C", 1m, 10.0066m);
        h.AssertInvariants("R4");

        var r5 = h.Issue(Item, "C", 1m);
        r5.Movements.Single().TotalCost.ShouldBe(10.0066m);
        Expect(h, "C", 0m, 0.0000m);
        h.AssertInvariants("R5");

        var issued = r3.Movements.Single().TotalCost + r4.Movements.Single().TotalCost + r5.Movements.Single().TotalCost;
        var received = r1.Movements.Single().TotalCost + r2.Movements.Single().TotalCost;
        issued.ShouldBe(30.0200m);
        issued.ShouldBe(received);
    }

    [Fact]
    public void Vector_set_A_adjustments()
    {
        var h = new LedgerHarness();

        var a1 = h.Receive(Item, "D", 1m, 10.00m);
        a1.Movements.Single().TotalCost.ShouldBe(10.0000m);
        Expect(h, "D", 1m, 10.0000m);
        h.AssertInvariants("A1");

        var a2 = h.Receive(Item, "D", 2m, 10.01m);
        a2.Movements.Single().TotalCost.ShouldBe(20.0200m);
        Expect(h, "D", 3m, 30.0200m);
        h.AssertInvariants("A2");

        // 30.0200, not 30.0201: rounding the average first is wrong.
        var a3 = h.Adjust(Item, "D", 3m);
        a3.Movements.Single().TotalCost.ShouldBe(30.0200m);
        Expect(h, "D", 6m, 60.0400m);
        h.AssertInvariants("A3");

        var a4 = h.Adjust(Item, "D", -1m);
        a4.Movements.Single().TotalCost.ShouldBe(10.0067m);
        Expect(h, "D", 5m, 50.0333m);
        h.AssertInvariants("A4");

        var a5 = h.Adjust(Item, "D", 2m, 12.50m);
        a5.Movements.Single().TotalCost.ShouldBe(25.0000m);
        Expect(h, "D", 7m, 75.0333m);
        h.AssertInvariants("A5");

        var a6 = h.Adjust(Item, "D", -7m);
        a6.Movements.Single().TotalCost.ShouldBe(75.0333m);
        Expect(h, "D", 0m, 0.0000m);
        h.AssertInvariants("A6");

        var movements = h.MovementCount;
        var balances = h.BalanceCount;

        var a7 = Should.Throw<UnitCostRequiredException>(() => h.Adjust(Item, "D", 1m));
        a7.LineNo.ShouldBe(1);
        Expect(h, "D", 0m, 0.0000m);
        h.MovementCount.ShouldBe(movements);
        h.BalanceCount.ShouldBe(balances);
        h.AssertInvariants("A7");
    }

    [Fact]
    public void Vector_set_N_negative_stock()
    {
        var h = new LedgerHarness();
        h.AllowNegative("K");
        decimal receipts = 0m, issues = 0m, revaluations = 0m;

        var n1 = h.Receive(Item, "K", 4m, 50.00m);
        n1.Movements.Single().TotalCost.ShouldBe(200.0000m);
        h.Revaluation(n1).ShouldBeNull();
        Expect(h, "K", 4m, 200.0000m);
        ExpectLuc(h, "K", 50.0000m);
        h.AssertInvariants("N1");
        receipts += 200.0000m;

        var n2 = h.Issue(Item, "K", 6m);
        n2.Movements.Single().TotalCost.ShouldBe(300.0000m);
        h.Revaluation(n2).ShouldBeNull();
        Expect(h, "K", -2m, -100.0000m);
        ExpectLuc(h, "K", 50.0000m);
        h.AssertInvariants("N2");
        issues += 300.0000m;

        var n3 = h.Issue(Item, "K", 1m);
        n3.Movements.Single().TotalCost.ShouldBe(50.0000m);
        h.Revaluation(n3).ShouldBeNull();
        Expect(h, "K", -3m, -150.0000m);
        ExpectLuc(h, "K", 50.0000m);
        h.AssertInvariants("N3");
        issues += 50.0000m;

        var n4 = h.Receive(Item, "K", 10m, 56.00m);
        n4.Movements.Single(m => m.LineNo == 1).TotalCost.ShouldBe(560.0000m);
        var n4Reval = h.Revaluation(n4).ShouldNotBeNull();
        n4Reval.LineNo.ShouldBe(1001);
        n4Reval.FromLocationId.ShouldBe(h.LocationId("K"));
        n4Reval.ToLocationId.ShouldBe(h.LocationId("ADJ"));
        n4Reval.Quantity.ShouldBe(0m);
        n4Reval.TotalCost.ShouldBe(18.0000m);
        Expect(h, "K", 7m, 392.0000m);
        ExpectLuc(h, "K", 56.0000m);
        h.AssertInvariants("N4");
        receipts += 560.0000m;
        revaluations += n4Reval.TotalCost;

        var n5 = h.Issue(Item, "K", 7m);
        n5.Movements.Single().TotalCost.ShouldBe(392.0000m);
        h.Revaluation(n5).ShouldBeNull();
        Expect(h, "K", 0m, 0.0000m);
        ExpectLuc(h, "K", 56.0000m);
        h.AssertInvariants("N5");
        issues += 392.0000m;

        var n6 = h.Issue(Item, "K", 2m);
        n6.Movements.Single().TotalCost.ShouldBe(112.0000m);
        h.Revaluation(n6).ShouldBeNull();
        Expect(h, "K", -2m, -112.0000m);
        ExpectLuc(h, "K", 56.0000m);
        h.AssertInvariants("N6");
        issues += 112.0000m;

        var n7 = h.Receive(Item, "K", 2m, 60.00m);
        n7.Movements.Single(m => m.LineNo == 1).TotalCost.ShouldBe(120.0000m);
        var n7Reval = h.Revaluation(n7).ShouldNotBeNull();
        n7Reval.LineNo.ShouldBe(1001);
        n7Reval.FromLocationId.ShouldBe(h.LocationId("K"));
        n7Reval.ToLocationId.ShouldBe(h.LocationId("ADJ"));
        n7Reval.Quantity.ShouldBe(0m);
        n7Reval.TotalCost.ShouldBe(8.0000m);
        Expect(h, "K", 0m, 0.0000m);
        ExpectLuc(h, "K", 60.0000m);
        h.AssertInvariants("N7");
        receipts += 120.0000m;
        revaluations += n7Reval.TotalCost;

        var n8 = h.Receive(Item, "K", 5m, 60.00m);
        n8.Movements.Single().TotalCost.ShouldBe(300.0000m);
        h.Revaluation(n8).ShouldBeNull();
        Expect(h, "K", 5m, 300.0000m);
        ExpectLuc(h, "K", 60.0000m);
        h.AssertInvariants("N8");
        receipts += 300.0000m;

        var n9 = h.Issue(Item, "K", 5m);
        n9.Movements.Single().TotalCost.ShouldBe(300.0000m);
        h.Revaluation(n9).ShouldBeNull();
        Expect(h, "K", 0m, 0.0000m);
        ExpectLuc(h, "K", 60.0000m);
        h.AssertInvariants("N9");
        issues += 300.0000m;

        // Value conservation (spec :456): receipts = issues + revaluations.
        receipts.ShouldBe(1180.0000m);
        issues.ShouldBe(1154.0000m);
        revaluations.ShouldBe(26.0000m);
        receipts.ShouldBe(issues + revaluations);
        h.State(Item, "ADJ").OnHand.ShouldBe(0m);
    }

    [Fact]
    public void Vector_set_X_estimate_above_the_next_price()
    {
        var h = new LedgerHarness();
        h.AllowNegative("K2");

        var x1 = h.Receive(Item, "K2", 4m, 50.00m);
        x1.Movements.Single().TotalCost.ShouldBe(200.0000m);
        h.Revaluation(x1).ShouldBeNull();
        Expect(h, "K2", 4m, 200.0000m);
        ExpectLuc(h, "K2", 50.0000m);
        h.AssertInvariants("X1");

        var x2 = h.Issue(Item, "K2", 6m);
        x2.Movements.Single().TotalCost.ShouldBe(300.0000m);
        h.Revaluation(x2).ShouldBeNull();
        Expect(h, "K2", -2m, -100.0000m);
        ExpectLuc(h, "K2", 50.0000m);
        h.AssertInvariants("X2");

        // Without the revaluation, X3 would leave 1 / -70.0000 and X4 would post a cost of -70,
        // which the TotalCost >= 0 check refuses (spec :467).
        var x3 = h.Receive(Item, "K2", 3m, 10.00m);
        x3.Movements.Single(m => m.LineNo == 1).TotalCost.ShouldBe(30.0000m);
        var x3Reval = h.Revaluation(x3).ShouldNotBeNull();
        x3Reval.LineNo.ShouldBe(1001);
        x3Reval.FromLocationId.ShouldBe(h.LocationId("ADJ"));
        x3Reval.ToLocationId.ShouldBe(h.LocationId("K2"));
        x3Reval.Quantity.ShouldBe(0m);
        x3Reval.TotalCost.ShouldBe(80.0000m);
        Expect(h, "K2", 1m, 10.0000m);
        ExpectLuc(h, "K2", 10.0000m);
        h.AssertInvariants("X3");

        var x4 = h.Issue(Item, "K2", 1m);
        x4.Movements.Single().TotalCost.ShouldBe(10.0000m);
        h.Revaluation(x4).ShouldBeNull();
        Expect(h, "K2", 0m, 0.0000m);
        ExpectLuc(h, "K2", 10.0000m);
        h.AssertInvariants("X4");
    }

    // Set U (plan D2): item base pc, unit case = 12. Quantities reach the rules in base units; the inflow is costed in the
    // entered unit, so 1 case @ 500.00 costs 500.0000, not 12 × Round4(500 / 12) = 500.0004.
    [Fact]
    public void Vector_set_U_units()
    {
        var h = new LedgerHarness();

        var u1 = h.Receive(Item, "U", 12m, 500.00m, unitFactor: 12m);
        u1.Movements.Single().Quantity.ShouldBe(12m);
        u1.Movements.Single().TotalCost.ShouldBe(500.0000m);
        Expect(h, "U", 12m, 500.0000m);
        ExpectLuc(h, "U", 41.6667m);
        h.AssertInvariants("U1");

        var u2 = h.Receive(Item, "U", 30m, 480.00m, unitFactor: 12m);
        u2.Movements.Single().Quantity.ShouldBe(30m);
        u2.Movements.Single().TotalCost.ShouldBe(1200.0000m);
        Expect(h, "U", 42m, 1700.0000m);
        ExpectLuc(h, "U", 40.4762m);
        h.AssertInvariants("U2");

        var u3 = h.Issue(Item, "U", 6m, unitFactor: 12m);
        u3.Movements.Single().Quantity.ShouldBe(6m);
        u3.Movements.Single().TotalCost.ShouldBe(242.8571m);
        Expect(h, "U", 36m, 1457.1429m);
        ExpectLuc(h, "U", 40.4762m);
        h.AssertInvariants("U3");

        var u4 = h.Issue(Item, "U", 36m);
        u4.Movements.Single().TotalCost.ShouldBe(1457.1429m);
        Expect(h, "U", 0m, 0.0000m);
        ExpectLuc(h, "U", 40.4762m);
        h.AssertInvariants("U4");

        // Received 1,700.0000 = issued 242.8571 + 1,457.1429.
        (u1.Movements.Single().TotalCost + u2.Movements.Single().TotalCost)
            .ShouldBe(u3.Movements.Single().TotalCost + u4.Movements.Single().TotalCost);
    }

    public static TheoryData<decimal, decimal> FactorOneCases => new()
    {
        { 1m, 10.00m },
        { 2m, 10.01m },
        { 3m, 10.0067m },
        { 0.5m, 0.0001m },
    };

    [Theory]
    [MemberData(nameof(FactorOneCases))]
    public void Unit_factor_1_matches_the_old_inflow_cost(decimal qty, decimal unitCost)
    {
        var h = new LedgerHarness();

        var posted = h.Receive(Item, "U", qty, unitCost, unitFactor: 1m);

        posted.Movements.Single().TotalCost.ShouldBe(CostingPolicy.InflowCost(qty, unitCost));
    }

    // N1-N4 at location K with the toggle on; returns the N4 posting.
    private static Posted PostN1ToN4(LedgerHarness h)
    {
        h.AllowNegative("K");
        h.Receive(Item, "K", 4m, 50.00m);
        h.Issue(Item, "K", 6m);
        h.Issue(Item, "K", 1m);
        Expect(h, "K", -3m, -150.0000m);
        ExpectLuc(h, "K", 50.0000m);
        var n4 = h.Receive(Item, "K", 10m, 56.00m);
        Expect(h, "K", 7m, 392.0000m);
        return n4;
    }

    [Fact]
    public void Reversal_restores_a_negative_balance_exactly()
    {
        var h = new LedgerHarness();
        var n4 = PostN1ToN4(h);
        h.Revaluation(n4).ShouldNotBeNull().TotalCost.ShouldBe(18.0000m);
        h.AssertInvariants("N4");

        var reversal = h.Reverse(n4.DocumentId);

        reversal.Movements.Count.ShouldBe(2);
        var k = h.LocationId("K");
        var sup = h.LocationId("SUP");
        var adj = h.LocationId("ADJ");

        var line1 = reversal.Movements.Single(m => m.LineNo == 1);
        line1.FromLocationId.ShouldBe(k);
        line1.ToLocationId.ShouldBe(sup);
        line1.Quantity.ShouldBe(10m);
        line1.TotalCost.ShouldBe(560.0000m);
        line1.ReversesMovementId.ShouldBe(n4.Movements.Single(m => m.LineNo == 1).Id);

        var line1001 = reversal.Movements.Single(m => m.LineNo == 1001);
        line1001.FromLocationId.ShouldBe(adj);
        line1001.ToLocationId.ShouldBe(k);
        line1001.Quantity.ShouldBe(0m);
        line1001.TotalCost.ShouldBe(18.0000m);
        line1001.ReversesMovementId.ShouldBe(h.Revaluation(n4)!.Id);

        Expect(h, "K", -3m, -150.0000m);
        ExpectLuc(h, "K", 50.0000m);
        h.State(Item, "ADJ").OnHand.ShouldBe(0m);
        h.AssertInvariants("reversal");
    }

    [Fact]
    public void Reversal_after_the_toggle_is_turned_off_is_rejected()
    {
        var h = new LedgerHarness();
        var n4 = PostN1ToN4(h);
        h.AssertInvariants("N4");
        h.AllowNegative("K", false);

        var movements = h.MovementCount;
        var balances = h.BalanceCount;

        var ex = Should.Throw<InsufficientStockException>(() => h.Reverse(n4.DocumentId));
        ex.Available.ShouldBe(7m);
        ex.Requested.ShouldBe(10m);

        h.MovementCount.ShouldBe(movements);
        h.BalanceCount.ShouldBe(balances);
        Expect(h, "K", 7m, 392.0000m);
        ExpectLuc(h, "K", 56.0000m);
        h.AssertInvariants("rejected reversal");
    }

    [Fact]
    public void Issue_past_zero_with_the_toggle_off_is_still_rejected()
    {
        var h = new LedgerHarness();
        h.Receive(Item, "K", 4m, 50.00m);
        var movements = h.MovementCount;
        var balances = h.BalanceCount;

        var ex = Should.Throw<InsufficientStockException>(() => h.Issue(Item, "K", 6m));
        ex.Available.ShouldBe(4m);
        ex.Requested.ShouldBe(6m);

        h.MovementCount.ShouldBe(movements);
        h.BalanceCount.ShouldBe(balances);
        Expect(h, "K", 4m, 200.0000m);
        h.AssertInvariants("toggle off");
    }

    [Fact]
    public void Rejected_first_touch_leaves_no_state()
    {
        var h = new LedgerHarness();
        h.Receive("X", "A", 1m, 10.00m);
        var balances = h.BalanceCount;
        var movements = h.MovementCount;

        var issue = Should.Throw<InsufficientStockException>(() => h.Issue("Y", "E", 1m));
        issue.Available.ShouldBe(0m);
        issue.Requested.ShouldBe(1m);

        Should.Throw<InsufficientStockException>(() => h.Transfer("X", "A", "F", 2m));

        h.BalanceCount.ShouldBe(balances);
        h.MovementCount.ShouldBe(movements);
        h.AssertInvariants("first-touch");
    }
}