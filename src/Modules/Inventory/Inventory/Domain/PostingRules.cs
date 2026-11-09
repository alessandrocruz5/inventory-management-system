namespace Inventory.Domain;

/// <summary>The document kinds that post through step 8 (spec §6). Reversals use <see cref="PostingRules.Reverse"/>.</summary>
internal enum PostingKind
{
    Receipt,
    Issue,
    Transfer,
    Adjustment,
}

/// <summary>A request line, which the handler resolves to its locations (spec §6 table); Quantity is always > 0.</summary>
internal sealed record PostingLine(int LineNo, Guid ItemId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, decimal? UnitCost);

/// <summary>Movements in insert order and the new state of every touched balance. LastMovementId is left for the engine.</summary>
internal sealed record PostingResult(IReadOnlyList<PlannedMovement> Movements, IReadOnlyList<Balance> Balances);

/// <summary>
/// Step 8 of the posting engine (spec §6): turns lines, or an original document's movements, plus the current balances
/// into planned movements and new balances. Pure: it never mutates its inputs, so a throw leaves nothing behind.
/// </summary>
internal static class PostingRules
{
    public static PostingResult Post(PostingKind kind, IReadOnlyList<PostingLine> lines, IReadOnlyDictionary<BalanceKey, Balance> balances)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(balances);

        if (lines.Count == 0)
        {
            throw new ArgumentException("A posting needs at least one line.", nameof(lines));
        }

        var lineNos = new HashSet<int>();
        var touched = new HashSet<BalanceKey>();
        foreach (var line in lines)
        {
            if (!lineNos.Add(line.LineNo))
            {
                throw new ArgumentException($"Line {line.LineNo} appears more than once.", nameof(lines));
            }

            if (line.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lines), line.Quantity, $"Line {line.LineNo}: the quantity must be greater than 0.");
            }

            if (line.UnitCost < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lines), line.UnitCost, $"Line {line.LineNo}: the unit cost must not be negative.");
            }

            if (line.FromLocationId == line.ToLocationId)
            {
                throw new ArgumentException($"Line {line.LineNo}: the from and to locations are the same.", nameof(lines));
            }

            var fromKey = new BalanceKey(line.ItemId, line.FromLocationId);
            var toKey = new BalanceKey(line.ItemId, line.ToLocationId);
            var source = Find(balances, fromKey);
            var target = Find(balances, toKey);

            if (!touched.Add(fromKey) || !touched.Add(toKey))
            {
                throw new ArgumentException(
                    $"Line {line.LineNo}: item {line.ItemId} is already on another line; each item appears once per document.",
                    nameof(lines));
            }

            CheckDirection(kind, line, source, target);
        }

        var working = new Dictionary<BalanceKey, Balance>();
        var order = new List<BalanceKey>();
        var movements = new List<PlannedMovement>(lines.Count);

        Balance Current(BalanceKey key) => working.TryGetValue(key, out var balance) ? balance : balances[key];

        void Store(Balance balance)
        {
            if (working.TryAdd(balance.Key, balance))
            {
                order.Add(balance.Key);
            }
            else
            {
                working[balance.Key] = balance;
            }
        }

        foreach (var line in lines.OrderBy(l => l.LineNo))
        {
            var source = Current(new BalanceKey(line.ItemId, line.FromLocationId));
            var target = Current(new BalanceKey(line.ItemId, line.ToLocationId));
            var qty = line.Quantity;

            decimal cost;
            if (!source.IsVirtual)
            {
                cost = CostingPolicy.OutflowCost(qty, source);
            }
            else if (line.UnitCost is { } unitCost)
            {
                cost = CostingPolicy.InflowCost(qty, unitCost);
            }
            else
            {
                // Only a positive adjustment reaches here (CheckDirection).
                cost = target.OnHand > 0
                    ? CostingPolicy.DefaultAdjustmentCost(qty, target)
                    : throw new UnitCostRequiredException(line.LineNo);
            }

            Store(source.IsVirtual
                ? source with { OnHand = source.OnHand - qty }
                : source with { OnHand = source.OnHand - qty, StockValue = source.StockValue - cost });

            if (target.IsVirtual)
            {
                Store(target with { OnHand = target.OnHand + qty });
            }
            else
            {
                if (target.OnHand < 0)
                {
                    // S10: settle with CostingPolicy.Revaluation (spec §7)
                    throw new InvalidOperationException(
                        $"Line {line.LineNo}: an inflow into a negative balance needs a revaluation, which S10 adds.");
                }

                var onHand = target.OnHand + qty;
                var value = target.StockValue + cost;
                Store(target with
                {
                    OnHand = onHand,
                    StockValue = value,
                    LastUnitCost = CostingPolicy.LastUnitCostAfterInflow(onHand, value, qty, cost),
                });
            }

            movements.Add(new PlannedMovement(line.LineNo, line.ItemId, line.FromLocationId, line.ToLocationId, qty, cost, null));
        }

        return new PostingResult(movements, order.Select(k => working[k]).ToList());
    }

    public static PostingResult Reverse(
        IReadOnlyList<LedgerMovement> original,
        IReadOnlyDictionary<BalanceKey, Balance> balances,
        IReadOnlyDictionary<BalanceKey, IReadOnlyList<LedgerMovement>> histories)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentNullException.ThrowIfNull(histories);

        if (original.Count == 0)
        {
            throw new ArgumentException("A reversal needs the original's movements.", nameof(original));
        }

        var documentId = original[0].DocumentId;
        foreach (var m in original)
        {
            if (m.DocumentId != documentId)
            {
                throw new ArgumentException("The original's movements must all belong to one document.", nameof(original));
            }

            if (m.ReversesMovementId is not null)
            {
                throw new ArgumentException($"Movement {m.Id} is itself a reversal and can't be reversed.", nameof(original));
            }
        }

        var ordered = original.OrderBy(m => m.Id).ToList();

        var keys = new List<BalanceKey>();
        foreach (var m in ordered)
        {
            foreach (var key in new[] { new BalanceKey(m.ItemId, m.FromLocationId), new BalanceKey(m.ItemId, m.ToLocationId) })
            {
                if (!keys.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }

        var working = new Dictionary<BalanceKey, Balance>();
        foreach (var key in keys)
        {
            var balance = Find(balances, key);
            if (!balance.IsVirtual && !histories.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"No history for item {key.ItemId} at location {key.LocationId}; the engine reads every physical one.");
            }

            working[key] = balance;
        }

        foreach (var key in keys)
        {
            var balance = working[key];
            if (balance.IsVirtual)
            {
                continue;
            }

            var expected = ordered
                .Where(m => m.ItemId == key.ItemId && (m.FromLocationId == key.LocationId || m.ToLocationId == key.LocationId))
                .Max(m => m.Id);

            if (balance.LastMovementId is not { } last)
            {
                throw new InvalidOperationException(
                    $"The balance of item {key.ItemId} at location {key.LocationId} has movements but no LastMovementId.");
            }

            if (last != expected)
            {
                throw new ReversalNotLatestException(key.ItemId, key.LocationId, last);
            }
        }

        var movements = new List<PlannedMovement>(ordered.Count);
        foreach (var m in ordered)
        {
            var qty = m.Quantity;
            var cost = m.TotalCost;

            var fromKey = new BalanceKey(m.ItemId, m.ToLocationId);
            var source = working[fromKey];
            if (source.IsVirtual)
            {
                working[fromKey] = source with { OnHand = source.OnHand - qty };
            }
            else
            {
                // A reversal goes through step 8 like any posting (spec §6).
                if (!source.AllowNegative && qty > source.OnHand)
                {
                    throw new InsufficientStockException(source.ItemId, source.LocationId, source.OnHand, qty);
                }

                working[fromKey] = source with { OnHand = source.OnHand - qty, StockValue = source.StockValue - cost };
            }

            var toKey = new BalanceKey(m.ItemId, m.FromLocationId);
            var target = working[toKey];
            working[toKey] = target.IsVirtual
                ? target with { OnHand = target.OnHand + qty }
                : target with { OnHand = target.OnHand + qty, StockValue = target.StockValue + cost };

            movements.Add(new PlannedMovement(m.LineNo, m.ItemId, m.ToLocationId, m.FromLocationId, qty, cost, m.Id));
        }

        // Latest-only means no movement after the original touches any of its physical balances, so the original and
        // its mirror are adjacent on every balance they touch. Their quantities and costs cancel, and LastUnitCost
        // never picks a reversed inflow or a reversal, so folding the history without the original gives the same
        // answer as folding the full history after this reversal.
        var originalIds = ordered.Select(m => m.Id).ToHashSet();
        foreach (var key in keys)
        {
            var balance = working[key];
            if (!balance.IsVirtual)
            {
                working[key] = balance with
                {
                    LastUnitCost = CostingPolicy.LastUnitCost(key, histories[key].Where(h => !originalIds.Contains(h.Id))),
                };
            }
        }

        return new PostingResult(movements, keys.Select(k => working[k]).ToList());
    }

    private static Balance Find(IReadOnlyDictionary<BalanceKey, Balance> balances, BalanceKey key) =>
        balances.TryGetValue(key, out var balance)
            ? balance
            : throw new InvalidOperationException(
                $"No balance for item {key.ItemId} at location {key.LocationId}; step 5 creates every touched balance.");

    private static void CheckDirection(PostingKind kind, PostingLine line, Balance source, Balance target)
    {
        var inflow = source.IsVirtual && !target.IsVirtual;
        var outflow = !source.IsVirtual && target.IsVirtual;
        var internalMove = !source.IsVirtual && !target.IsVirtual;
        var hasCost = line.UnitCost is not null;

        var valid = kind switch
        {
            PostingKind.Receipt => inflow && hasCost,
            PostingKind.Issue => outflow && !hasCost,
            PostingKind.Transfer => internalMove && !hasCost,
            PostingKind.Adjustment => inflow || (outflow && !hasCost),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown posting kind."),
        };

        if (!valid)
        {
            throw new ArgumentException(
                $"Line {line.LineNo}: a {kind} can't move stock from a {(source.IsVirtual ? "virtual" : "physical")} to a "
                + $"{(target.IsVirtual ? "virtual" : "physical")} location {(hasCost ? "with" : "without")} a unit cost.",
                nameof(line));
        }
    }
}