using Inventory.Domain;

using Shouldly;

namespace Inventory.UnitTests;

public sealed class PostingRulesTests
{
    private static readonly Guid Item = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid OtherItem = Guid.Parse("11111111-0000-0000-0000-000000000002");
    private static readonly Guid Sup = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid Cus = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid Adj = Guid.Parse("22222222-0000-0000-0000-000000000003");
    private static readonly Guid A = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("33333333-0000-0000-0000-000000000002");
    private static readonly Guid D1 = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly Guid D2 = Guid.Parse("44444444-0000-0000-0000-000000000002");
    private static readonly Guid D3 = Guid.Parse("44444444-0000-0000-0000-000000000003");

    private static Balance Virtual(Guid location, decimal onHand = 0m, long? lastMovementId = null, Guid? item = null) =>
        new(item ?? Item, location, IsVirtual: true, AllowNegative: true, onHand, 0m, null, lastMovementId);

    private static Balance Physical(
        Guid location,
        decimal onHand = 0m,
        decimal value = 0m,
        decimal? luc = null,
        long? lastMovementId = null,
        bool allowNegative = false,
        Guid? item = null) =>
        new(item ?? Item, location, IsVirtual: false, allowNegative, onHand, value, luc, lastMovementId);

    private static Dictionary<BalanceKey, Balance> BalancesOf(params Balance[] balances) =>
        balances.ToDictionary(b => b.Key);

    private static PostingLine Line(Guid from, Guid to, decimal quantity, decimal? unitCost = null, int lineNo = 1, Guid? item = null) =>
        new(lineNo, item ?? Item, from, to, quantity, unitCost);

    private static LedgerMovement Move(long id, Guid doc, Guid from, Guid to, decimal quantity, decimal cost, long? reverses = null) =>
        new(id, doc, LineNo: 1, Item, from, to, quantity, cost, reverses);

    private static Balance Of(PostingResult result, Guid location, Guid? item = null) =>
        result.Balances.Single(b => b.ItemId == (item ?? Item) && b.LocationId == location);

    private static Dictionary<BalanceKey, IReadOnlyList<LedgerMovement>> Histories(params (Guid Location, LedgerMovement[] Movements)[] histories) =>
        histories.ToDictionary(h => new BalanceKey(Item, h.Location), IReadOnlyList<LedgerMovement> (h) => h.Movements);

    // Post: Receipt

    [Fact]
    public void Receipt_moves_stock_from_the_supplier_at_the_line_cost()
    {
        var sup = Virtual(Sup, lastMovementId: 3);
        var a = Physical(A, lastMovementId: 2);
        var balances = BalancesOf(sup, a);

        var result = PostingRules.Post(PostingKind.Receipt, [Line(Sup, A, 10m, 100m)], balances);

        result.Movements.ShouldBe([new PlannedMovement(1, Item, Sup, A, 10m, 1000.0000m, null)]);

        var newA = Of(result, A);
        newA.OnHand.ShouldBe(10m);
        newA.StockValue.ShouldBe(1000m);
        newA.LastUnitCost.ShouldBe(100m);
        newA.LastMovementId.ShouldBe(2);

        var newSup = Of(result, Sup);
        newSup.OnHand.ShouldBe(-10m);
        newSup.StockValue.ShouldBe(0m);
        newSup.LastUnitCost.ShouldBeNull();
        newSup.LastMovementId.ShouldBe(3);

        balances.Count.ShouldBe(2);
        balances[sup.Key].ShouldBe(sup);
        balances[a.Key].ShouldBe(a);
    }

    // Post: Issue

    [Fact]
    public void Issue_takes_a_proportional_share_of_the_value()
    {
        var balances = BalancesOf(Physical(A, 20m, 2200m, luc: 110m), Virtual(Cus));

        var result = PostingRules.Post(PostingKind.Issue, [Line(A, Cus, 15m)], balances);

        result.Movements.Single().TotalCost.ShouldBe(1650m);
        var a = Of(result, A);
        a.OnHand.ShouldBe(5m);
        a.StockValue.ShouldBe(550m);
        a.LastUnitCost.ShouldBe(110m);
        Of(result, Cus).OnHand.ShouldBe(15m);
        Of(result, Cus).StockValue.ShouldBe(0m);
    }

    [Fact]
    public void Issue_of_the_whole_balance_takes_the_whole_value()
    {
        var balances = BalancesOf(Physical(A, 6m, 720m, luc: 120m), Virtual(Cus));

        var result = PostingRules.Post(PostingKind.Issue, [Line(A, Cus, 6m)], balances);

        result.Movements.Single().TotalCost.ShouldBe(720m);
        Of(result, A).OnHand.ShouldBe(0m);
        Of(result, A).StockValue.ShouldBe(0m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Issue_above_on_hand_throws_insufficient_stock(bool allowNegative)
    {
        var balances = BalancesOf(Physical(A, 6m, 720m, luc: 120m, allowNegative: allowNegative), Virtual(Cus));

        var ex = Should.Throw<InsufficientStockException>(
            () => PostingRules.Post(PostingKind.Issue, [Line(A, Cus, 7m)], balances));

        ex.ItemId.ShouldBe(Item);
        ex.LocationId.ShouldBe(A);
        ex.Available.ShouldBe(6m);
        ex.Requested.ShouldBe(7m);
    }

    // Post: Transfer

    [Fact]
    public void Transfer_carries_the_outflow_cost_to_the_destination()
    {
        var balances = BalancesOf(Physical(A, 10m, 1200m, luc: 120m), Physical(B));

        var result = PostingRules.Post(PostingKind.Transfer, [Line(A, B, 4m)], balances);

        result.Movements.ShouldBe([new PlannedMovement(1, Item, A, B, 4m, 480m, null)]);
        var a = Of(result, A);
        a.OnHand.ShouldBe(6m);
        a.StockValue.ShouldBe(720m);
        a.LastUnitCost.ShouldBe(120m);
        var b = Of(result, B);
        b.OnHand.ShouldBe(4m);
        b.StockValue.ShouldBe(480m);
        b.LastUnitCost.ShouldBe(120m);
    }

    // Post: Adjustment

    [Fact]
    public void Positive_adjustment_with_a_unit_cost_uses_it()
    {
        var balances = BalancesOf(Virtual(Adj), Physical(A));

        var result = PostingRules.Post(PostingKind.Adjustment, [Line(Adj, A, 2m, 12.50m)], balances);

        result.Movements.Single().TotalCost.ShouldBe(25m);
        Of(result, A).OnHand.ShouldBe(2m);
        Of(result, A).StockValue.ShouldBe(25m);
        Of(result, A).LastUnitCost.ShouldBe(12.5m);
        Of(result, Adj).OnHand.ShouldBe(-2m);
    }

    [Fact]
    public void Positive_adjustment_without_a_unit_cost_uses_the_current_average()
    {
        var balances = BalancesOf(Virtual(Adj), Physical(A, 3m, 30.02m, luc: 10.0067m));

        var result = PostingRules.Post(PostingKind.Adjustment, [Line(Adj, A, 3m)], balances);

        result.Movements.Single().TotalCost.ShouldBe(30.0200m);
        Of(result, A).OnHand.ShouldBe(6m);
        Of(result, A).StockValue.ShouldBe(60.04m);
        Of(result, A).LastUnitCost.ShouldBe(10.0067m);
    }

    [Fact]
    public void Positive_adjustment_without_a_unit_cost_or_stock_throws_unit_cost_required()
    {
        var balances = BalancesOf(Virtual(Adj), Physical(A));

        var ex = Should.Throw<UnitCostRequiredException>(
            () => PostingRules.Post(PostingKind.Adjustment, [Line(Adj, A, 3m, lineNo: 4)], balances));

        ex.LineNo.ShouldBe(4);
    }

    [Fact]
    public void Negative_adjustment_takes_the_outflow_cost()
    {
        var balances = BalancesOf(Physical(A, 6m, 60.04m, luc: 10.0067m), Virtual(Adj));

        var result = PostingRules.Post(PostingKind.Adjustment, [Line(A, Adj, 1m)], balances);

        result.Movements.ShouldBe([new PlannedMovement(1, Item, A, Adj, 1m, 10.0067m, null)]);
        Of(result, A).OnHand.ShouldBe(5m);
        Of(result, A).StockValue.ShouldBe(50.0333m);
        Of(result, A).LastUnitCost.ShouldBe(10.0067m);
        Of(result, Adj).OnHand.ShouldBe(1m);
    }

    // Post: multi-line

    [Fact]
    public void Multi_line_receipt_returns_movements_in_line_order()
    {
        var balances = BalancesOf(
            Virtual(Sup),
            Physical(A),
            Virtual(Sup, item: OtherItem),
            Physical(A, item: OtherItem));

        PostingLine[] lines =
        [
            Line(Sup, A, 2m, 5m, lineNo: 2, item: OtherItem),
            Line(Sup, A, 10m, 100m, lineNo: 1),
        ];

        var result = PostingRules.Post(PostingKind.Receipt, lines, balances);

        result.Movements.ShouldBe(
        [
            new PlannedMovement(1, Item, Sup, A, 10m, 1000m, null),
            new PlannedMovement(2, OtherItem, Sup, A, 2m, 10m, null),
        ]);
        result.Balances.Count.ShouldBe(4);
        Of(result, A, OtherItem).StockValue.ShouldBe(10m);
    }

    [Fact]
    public void Multi_line_issue_with_a_short_second_line_throws_and_changes_nothing()
    {
        var a = Physical(A, 10m, 1000m, luc: 100m);
        var otherA = Physical(A, 1m, 5m, luc: 5m, item: OtherItem);
        var balances = BalancesOf(a, Virtual(Cus), otherA, Virtual(Cus, item: OtherItem));

        PostingLine[] lines =
        [
            Line(A, Cus, 4m, lineNo: 1),
            Line(A, Cus, 2m, lineNo: 2, item: OtherItem),
        ];

        Should.Throw<InsufficientStockException>(() => PostingRules.Post(PostingKind.Issue, lines, balances));

        balances[a.Key].ShouldBe(a);
        balances[otherA.Key].ShouldBe(otherA);
    }

    // Post: guards

    [Fact]
    public void Post_with_a_missing_balance_throws_invalid_operation()
    {
        var balances = BalancesOf(Virtual(Sup));

        Should.Throw<InvalidOperationException>(
            () => PostingRules.Post(PostingKind.Receipt, [Line(Sup, A, 1m, 1m)], balances));
    }

    public static TheoryData<string, Guid, Guid, decimal?> WrongDirectionCases => new()
    {
        { nameof(PostingKind.Issue), A, B, null },
        { nameof(PostingKind.Receipt), Sup, A, null },
        { nameof(PostingKind.Issue), A, Cus, 10m },
        { nameof(PostingKind.Transfer), A, B, 10m },
        { nameof(PostingKind.Transfer), Sup, A, null },
        { nameof(PostingKind.Adjustment), A, Adj, 10m },
        { nameof(PostingKind.Receipt), Sup, Cus, 10m },
        { nameof(PostingKind.Adjustment), Adj, Sup, null },
    };

    [Theory]
    [MemberData(nameof(WrongDirectionCases))]
    public void Post_with_the_wrong_direction_or_unit_cost_throws_argument(string kind, Guid from, Guid to, decimal? unitCost)
    {
        var balances = BalancesOf(Virtual(Sup), Virtual(Cus), Virtual(Adj), Physical(A, 10m, 1000m, luc: 100m), Physical(B));

        Should.Throw<ArgumentException>(() => PostingRules.Post(Enum.Parse<PostingKind>(kind), [Line(from, to, 1m, unitCost)], balances));
    }

    [Fact]
    public void Post_touching_the_same_balance_on_two_lines_throws_argument()
    {
        var balances = BalancesOf(Virtual(Sup), Physical(A));

        PostingLine[] lines =
        [
            Line(Sup, A, 1m, 1m, lineNo: 1),
            Line(Sup, A, 2m, 1m, lineNo: 2),
        ];

        Should.Throw<ArgumentException>(() => PostingRules.Post(PostingKind.Receipt, lines, balances));
    }

    [Fact]
    public void Post_with_a_zero_quantity_throws_argument_out_of_range()
    {
        var balances = BalancesOf(Virtual(Sup), Physical(A));

        Should.Throw<ArgumentOutOfRangeException>(
            () => PostingRules.Post(PostingKind.Receipt, [Line(Sup, A, 0m, 1m)], balances));
    }

    [Fact]
    public void Post_from_and_to_the_same_location_throws_argument()
    {
        var balances = BalancesOf(Physical(A, 10m, 1000m, luc: 100m));

        Should.Throw<ArgumentException>(() => PostingRules.Post(PostingKind.Transfer, [Line(A, A, 1m)], balances));
    }

    [Fact]
    public void Receipt_into_a_negative_balance_throws_invalid_operation_until_S10()
    {
        var balances = BalancesOf(Virtual(Sup), Physical(A, -2m, 0m, allowNegative: true));

        Should.Throw<InvalidOperationException>(
            () => PostingRules.Post(PostingKind.Receipt, [Line(Sup, A, 5m, 10m)], balances));
    }

    // Reverse

    [Fact]
    public void Reverse_a_receipt_mirrors_it_and_recomputes_the_last_unit_cost()
    {
        var transferIn = Move(5, D1, A, B, 4m, 480m);
        var receipt = Move(6, D2, Sup, B, 6m, 750m);
        var b = Physical(B, 10m, 1230m, luc: 123m, lastMovementId: 6);
        var sup = Virtual(Sup, -20m, lastMovementId: 42);

        var result = PostingRules.Reverse(
            [receipt],
            BalancesOf(b, sup),
            Histories((B, [transferIn, receipt])));

        result.Movements.ShouldBe([new PlannedMovement(1, Item, B, Sup, 6m, 750m, 6)]);
        var newB = Of(result, B);
        newB.OnHand.ShouldBe(4m);
        newB.StockValue.ShouldBe(480m);
        newB.LastUnitCost.ShouldBe(120m);
        newB.LastMovementId.ShouldBe(6);
        Of(result, Sup).OnHand.ShouldBe(-14m);
        Of(result, Sup).StockValue.ShouldBe(0m);
    }

    [Fact]
    public void Reverse_an_issue_restores_quantity_and_value_and_keeps_the_last_unit_cost()
    {
        var receipt = Move(7, D1, Sup, A, 20m, 2200m);
        var issue = Move(8, D2, A, Cus, 15m, 1650m);

        var result = PostingRules.Reverse(
            [issue],
            BalancesOf(Physical(A, 5m, 550m, luc: 110m, lastMovementId: 8), Virtual(Cus, 15m)),
            Histories((A, [receipt, issue])));

        result.Movements.ShouldBe([new PlannedMovement(1, Item, Cus, A, 15m, 1650m, 8)]);
        var a = Of(result, A);
        a.OnHand.ShouldBe(20m);
        a.StockValue.ShouldBe(2200m);
        a.LastUnitCost.ShouldBe(110m);
        Of(result, Cus).OnHand.ShouldBe(0m);
    }

    [Fact]
    public void Reverse_a_transfer_restores_both_balances_and_recomputes_each_last_unit_cost()
    {
        var receiptA = Move(9, D1, Sup, A, 10m, 1200m);
        var receiptB = Move(10, D2, Sup, B, 2m, 300m);
        var transfer = Move(11, D3, A, B, 4m, 480m);

        var result = PostingRules.Reverse(
            [transfer],
            BalancesOf(
                Physical(A, 6m, 720m, luc: 120m, lastMovementId: 11),
                Physical(B, 6m, 780m, luc: 130m, lastMovementId: 11)),
            Histories((A, [receiptA, transfer]), (B, [receiptB, transfer])));

        result.Movements.ShouldBe([new PlannedMovement(1, Item, B, A, 4m, 480m, 11)]);
        var a = Of(result, A);
        a.OnHand.ShouldBe(10m);
        a.StockValue.ShouldBe(1200m);
        a.LastUnitCost.ShouldBe(120m);
        var b = Of(result, B);
        b.OnHand.ShouldBe(2m);
        b.StockValue.ShouldBe(300m);
        b.LastUnitCost.ShouldBe(150m);
    }

    [Fact]
    public void Reverse_when_a_later_movement_touched_the_balance_throws_not_latest()
    {
        var receipt = Move(6, D2, Sup, B, 6m, 750m);
        var later = Move(7, D3, B, Cus, 1m, 123m);

        var ex = Should.Throw<ReversalNotLatestException>(() => PostingRules.Reverse(
            [receipt],
            BalancesOf(Physical(B, 9m, 1107m, luc: 123m, lastMovementId: 7), Virtual(Sup)),
            Histories((B, [receipt, later]))));

        ex.ItemId.ShouldBe(Item);
        ex.LocationId.ShouldBe(B);
        ex.BlockingMovementId.ShouldBe(7);
    }

    [Fact]
    public void Reverse_more_than_the_balance_holds_throws_insufficient_stock()
    {
        var receipt = Move(6, D2, Sup, B, 6m, 750m);

        var ex = Should.Throw<InsufficientStockException>(() => PostingRules.Reverse(
            [receipt],
            BalancesOf(Physical(B, 4m, 500m, luc: 125m, lastMovementId: 6), Virtual(Sup)),
            Histories((B, [receipt]))));

        ex.LocationId.ShouldBe(B);
        ex.Available.ShouldBe(4m);
        ex.Requested.ShouldBe(6m);
    }

    [Fact]
    public void Reverse_a_reversal_throws_argument()
    {
        var reversal = Move(7, D3, B, Sup, 6m, 750m, reverses: 6);

        Should.Throw<ArgumentException>(() => PostingRules.Reverse(
            [reversal],
            BalancesOf(Physical(B, 4m, 480m, luc: 120m, lastMovementId: 7), Virtual(Sup)),
            Histories((B, [reversal]))));
    }

    [Fact]
    public void Reverse_movements_of_two_documents_throws_argument()
    {
        var first = Move(5, D1, Sup, A, 1m, 10m);
        var second = Move(6, D2, Sup, B, 1m, 10m);

        Should.Throw<ArgumentException>(() => PostingRules.Reverse(
            [first, second],
            BalancesOf(Physical(A, 1m, 10m, luc: 10m, lastMovementId: 5), Physical(B, 1m, 10m, luc: 10m, lastMovementId: 6), Virtual(Sup)),
            Histories((A, [first]), (B, [second]))));
    }

    [Fact]
    public void Reverse_without_a_physical_history_throws_invalid_operation()
    {
        var receipt = Move(6, D2, Sup, B, 6m, 750m);

        Should.Throw<InvalidOperationException>(() => PostingRules.Reverse(
            [receipt],
            BalancesOf(Physical(B, 10m, 1230m, luc: 123m, lastMovementId: 6), Virtual(Sup)),
            Histories()));
    }
}