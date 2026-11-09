using Inventory.Domain;

using Shouldly;

namespace Inventory.UnitTests;

public sealed class CostingPolicyTests
{
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid OtherItem = Guid.NewGuid();
    private static readonly Guid Loc = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid Sup = Guid.NewGuid();
    private static readonly Guid Cus = Guid.NewGuid();
    private static readonly Guid D1 = Guid.NewGuid();
    private static readonly Guid D2 = Guid.NewGuid();
    private static readonly Guid D3 = Guid.NewGuid();

    private static Balance BalanceOf(decimal onHand, decimal value, decimal? luc = null, bool allowNegative = false) =>
        new(Item, Loc, IsVirtual: false, AllowNegative: allowNegative, onHand, value, luc, null);

    private static LedgerMovement Move(long id, Guid doc, Guid from, Guid to, decimal quantity, decimal cost, long? reverses = null, Guid? item = null) =>
        new(id, doc, LineNo: 1, item ?? Item, from, to, quantity, cost, reverses);

    private static BalanceKey KeyB => new(Item, B);

    public static TheoryData<decimal, decimal> Round4Cases => new()
    {
        { 10.00665m, 10.0067m },
        { -10.00665m, -10.0067m },
        { 10.006666m, 10.0067m },
    };

    [Theory]
    [MemberData(nameof(Round4Cases))]
    public void Round4_rounds_midpoints_away_from_zero(decimal value, decimal expected)
    {
        CostingPolicy.Round4(value).ShouldBe(expected);
    }

    [Fact]
    public void Round4_is_not_bankers_rounding()
    {
        CostingPolicy.Round4(10.00665m).ShouldNotBe(10.0066m);
    }

    public static TheoryData<decimal, decimal, decimal> InflowCases => new()
    {
        { 6m, 125m, 750m },
        { 2m, 10.01m, 20.02m },
        { 3m, 0.33335m, 1.0001m },
    };

    [Theory]
    [MemberData(nameof(InflowCases))]
    public void InflowCost_is_quantity_times_unit_cost_rounded_to_4dp(decimal quantity, decimal unitCost, decimal expected)
    {
        CostingPolicy.InflowCost(quantity, unitCost).ShouldBe(expected);
    }

    [Fact]
    public void DefaultAdjustmentCost_multiplies_before_rounding()
    {
        var cost = CostingPolicy.DefaultAdjustmentCost(3m, BalanceOf(3m, 30.02m));

        cost.ShouldBe(30.0200m);
        cost.ShouldNotBe(30.0201m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DefaultAdjustmentCost_without_stock_on_hand_throws(int onHand)
    {
        Should.Throw<InvalidOperationException>(() => CostingPolicy.DefaultAdjustmentCost(1m, BalanceOf(onHand, 0m)));
    }

    public static TheoryData<decimal, decimal, decimal, decimal> OutflowCases => new()
    {
        { 15m, 20m, 2200m, 1650m },
        { 1m, 3m, 30.02m, 10.0067m },
        { 1m, 2m, 20.0133m, 10.0067m },
    };

    [Theory]
    [MemberData(nameof(OutflowCases))]
    public void OutflowCost_is_proportional_and_rounded_to_4dp(decimal quantity, decimal onHand, decimal value, decimal expected)
    {
        CostingPolicy.OutflowCost(quantity, BalanceOf(onHand, value)).ShouldBe(expected);
    }

    public static TheoryData<decimal, decimal, decimal> RemainderCases => new()
    {
        { 1m, 1m, 10.0066m },
        { 6m, 6m, 720m },
    };

    [Theory]
    [MemberData(nameof(RemainderCases))]
    public void OutflowCost_of_the_whole_balance_takes_the_whole_value(decimal quantity, decimal onHand, decimal value)
    {
        CostingPolicy.OutflowCost(quantity, BalanceOf(onHand, value)).ShouldBe(value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutflowCost_above_on_hand_throws_insufficient_stock(bool allowNegative)
    {
        var ex = Should.Throw<InsufficientStockException>(
            () => CostingPolicy.OutflowCost(7m, BalanceOf(6m, 720m, allowNegative: allowNegative)));

        ex.ItemId.ShouldBe(Item);
        ex.LocationId.ShouldBe(Loc);
        ex.Available.ShouldBe(6m);
        ex.Requested.ShouldBe(7m);
    }

    [Fact]
    public void LastUnitCostAfterInflow_is_the_new_average()
    {
        CostingPolicy.LastUnitCostAfterInflow(10m, 1230m, 6m, 750m).ShouldBe(123m);
    }

    [Fact]
    public void LastUnitCostAfterInflow_with_nothing_on_hand_is_the_inflow_unit_cost()
    {
        CostingPolicy.LastUnitCostAfterInflow(0m, 0m, 2m, 120m).ShouldBe(60m);
    }

    [Fact]
    public void LastUnitCost_of_an_empty_history_is_null()
    {
        CostingPolicy.LastUnitCost(KeyB, []).ShouldBeNull();
    }

    [Fact]
    public void LastUnitCost_with_only_an_outflow_is_null()
    {
        CostingPolicy.LastUnitCost(KeyB, [Move(5, D1, B, Cus, 1m, 120m)]).ShouldBeNull();
    }

    [Fact]
    public void LastUnitCost_is_the_average_after_the_last_inflow()
    {
        LedgerMovement[] history =
        [
            Move(6, D2, Sup, B, 6m, 750m),
            Move(5, D1, A, B, 4m, 480m),
        ];

        CostingPolicy.LastUnitCost(KeyB, history).ShouldBe(123m);
    }

    [Fact]
    public void LastUnitCost_skips_a_reversed_inflow_and_its_reversal()
    {
        LedgerMovement[] history =
        [
            Move(5, D1, A, B, 4m, 480m),
            Move(6, D2, Sup, B, 6m, 750m),
            Move(7, D3, B, Sup, 6m, 750m, reverses: 6),
        ];

        CostingPolicy.LastUnitCost(KeyB, history).ShouldBe(120m);
    }

    [Fact]
    public void LastUnitCost_ignores_outflows_after_the_inflow()
    {
        LedgerMovement[] history =
        [
            Move(5, D1, A, B, 4m, 480m),
            Move(6, D2, B, Cus, 1m, 120m),
        ];

        CostingPolicy.LastUnitCost(KeyB, history).ShouldBe(120m);
    }

    [Fact]
    public void LastUnitCost_rejects_a_movement_of_another_item()
    {
        Should.Throw<ArgumentException>(
            () => CostingPolicy.LastUnitCost(KeyB, [Move(5, D1, A, B, 4m, 480m, item: OtherItem)]));
    }

    [Fact]
    public void LastUnitCost_rejects_a_movement_that_does_not_touch_the_location()
    {
        Should.Throw<ArgumentException>(
            () => CostingPolicy.LastUnitCost(KeyB, [Move(5, D1, A, B, 4m, 480m), Move(6, D2, Sup, A, 1m, 100m)]));
    }
}