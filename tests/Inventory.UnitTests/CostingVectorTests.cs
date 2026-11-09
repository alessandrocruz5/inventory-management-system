using Inventory.Domain;

using Shouldly;

namespace Inventory.UnitTests;

/// <summary>
/// T1 unit part (spec §11): vector sets V, R and A of spec §7 :395-440. Must match the spec tables (CLAUDE.md Hidden coupling).
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