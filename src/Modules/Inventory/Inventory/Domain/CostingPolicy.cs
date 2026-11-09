namespace Inventory.Domain;

/// <summary>The moving-average costing rules (spec §7). Pure: no I/O and no state.</summary>
internal static class CostingPolicy
{
    /// <summary>Rounds to 4 decimal places, midpoints away from zero (spec §7, R4). Never banker's rounding.</summary>
    public static decimal Round4(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>The cost of a receipt or positive adjustment line that carries a unit cost (spec §7).</summary>
    public static decimal InflowCost(decimal quantity, decimal unitCost) => Round4(quantity * unitCost);

    /// <summary>
    /// The cost of a positive adjustment line without a unit cost: the current average, multiplied before rounding
    /// (spec §7, A3). Requires <c>balance.OnHand &gt; 0</c>; the posting rules check first and reject with
    /// <see cref="UnitCostRequiredException"/>.
    /// </summary>
    public static decimal DefaultAdjustmentCost(decimal quantity, Balance balance)
    {
        if (balance.OnHand <= 0)
        {
            throw new InvalidOperationException(
                $"A default adjustment cost needs stock on hand; OnHand is {balance.OnHand}.");
        }

        return Round4(quantity * balance.StockValue / balance.OnHand);
    }

    /// <summary>
    /// The cost of an issue, transfer-out or negative adjustment line from a physical balance (spec §7). Taking the
    /// whole balance takes its whole value, so rounding never leaves a residue.
    /// </summary>
    public static decimal OutflowCost(decimal quantity, Balance balance)
    {
        if (quantity > balance.OnHand)
        {
            // S10: when AllowNegative, estimate instead (spec §7)
            throw new InsufficientStockException(balance.ItemId, balance.LocationId, balance.OnHand, quantity);
        }

        return quantity == balance.OnHand
            ? balance.StockValue
            : Round4(quantity * balance.StockValue / balance.OnHand);
    }

    /// <summary>
    /// The balance's <c>LastUnitCost</c> after an inflow that isn't a reversal, and after its revaluation, from the
    /// balance's new totals (spec §7).
    /// </summary>
    public static decimal LastUnitCostAfterInflow(decimal onHandAfter, decimal stockValueAfter, decimal quantity, decimal cost) =>
        onHandAfter > 0 ? Round4(stockValueAfter / onHandAfter) : Round4(cost / quantity);

    /// <summary>
    /// The ledger definition of <c>LastUnitCost</c> (spec §7, §6 INV-5): the balance's average right after its last
    /// inflow that isn't a reversal and wasn't reversed, together with that inflow's revaluation. Used by reversals
    /// (S9) and must match S11's SQL. <paramref name="history"/> is every movement of the item that touches the
    /// location, in any order.
    /// </summary>
    public static decimal? LastUnitCost(BalanceKey key, IEnumerable<LedgerMovement> history)
    {
        var movements = history.OrderBy(m => m.Id).ToList();

        foreach (var m in movements)
        {
            if (m.ItemId != key.ItemId)
            {
                throw new ArgumentException($"Movement {m.Id} is for item {m.ItemId}, not {key.ItemId}.", nameof(history));
            }

            if (m.FromLocationId != key.LocationId && m.ToLocationId != key.LocationId)
            {
                throw new ArgumentException($"Movement {m.Id} doesn't touch location {key.LocationId}.", nameof(history));
            }
        }

        var reversed = movements
            .Where(m => m.ReversesMovementId is not null)
            .Select(m => m.ReversesMovementId!.Value)
            .ToHashSet();

        var inflow = movements.LastOrDefault(m =>
            m.ToLocationId == key.LocationId
            && m.Quantity > 0
            && m.ReversesMovementId is null
            && !reversed.Contains(m.Id));

        if (inflow is null)
        {
            return null;
        }

        var end = movements.Where(m => m.DocumentId == inflow.DocumentId).Max(m => m.Id);

        decimal onHand = 0;
        decimal value = 0;
        foreach (var m in movements.Where(m => m.Id <= end))
        {
            if (m.ToLocationId == key.LocationId)
            {
                onHand += m.Quantity;
                value += m.TotalCost;
            }
            else
            {
                onHand -= m.Quantity;
                value -= m.TotalCost;
            }
        }

        return LastUnitCostAfterInflow(onHand, value, inflow.Quantity, inflow.TotalCost);
    }
}