using Inventory.Domain;

using Shouldly;

namespace Inventory.UnitTests;

/// <summary>A posted document: its id and its movements, in insert order.</summary>
internal sealed record Posted(Guid DocumentId, IReadOnlyList<LedgerMovement> Movements);

/// <summary>
/// An in-memory stand-in for the posting engine (spec §6): it builds the step 5 working balances, calls
/// <see cref="PostingRules"/>, and commits only when the rules return, as the engine's transaction does.
/// </summary>
internal sealed class LedgerHarness
{
    private static readonly string[] VirtualNames = ["SUP", "CUS", "ADJ"];

    private readonly Dictionary<string, Guid> _locations = [];
    private readonly Dictionary<Guid, bool> _isVirtual = [];
    private readonly Dictionary<string, Guid> _items = [];
    private readonly Dictionary<BalanceKey, Balance> _balances = [];
    private readonly List<LedgerMovement> _movements = [];
    private long _nextId = 1;

    public LedgerHarness()
    {
        foreach (var name in VirtualNames)
        {
            var id = Guid.NewGuid();
            _locations[name] = id;
            _isVirtual[id] = true;
        }
    }

    public int BalanceCount => _balances.Count;

    public int MovementCount => _movements.Count;

    public Posted Receive(string item, string loc, decimal qty, decimal unitCost) =>
        Post(PostingKind.Receipt, item, "SUP", loc, qty, unitCost);

    public Posted Issue(string item, string loc, decimal qty) =>
        Post(PostingKind.Issue, item, loc, "CUS", qty, null);

    public Posted Transfer(string item, string from, string to, decimal qty) =>
        Post(PostingKind.Transfer, item, from, to, qty, null);

    public Posted Adjust(string item, string loc, decimal quantityDelta, decimal? unitCost = null) =>
        quantityDelta > 0
            ? Post(PostingKind.Adjustment, item, "ADJ", loc, quantityDelta, unitCost)
            : Post(PostingKind.Adjustment, item, loc, "ADJ", -quantityDelta, unitCost);

    public Posted Reverse(Guid documentId)
    {
        var original = _movements.Where(m => m.DocumentId == documentId).ToList();

        var keys = new List<BalanceKey>();
        foreach (var m in original)
        {
            foreach (var key in new[] { new BalanceKey(m.ItemId, m.FromLocationId), new BalanceKey(m.ItemId, m.ToLocationId) })
            {
                if (!keys.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }

        var working = WorkingCopy(keys);
        var histories = new Dictionary<BalanceKey, IReadOnlyList<LedgerMovement>>();
        foreach (var key in keys.Where(k => !working[k].IsVirtual))
        {
            histories[key] = Touching(key).ToList();
        }

        return Commit(PostingRules.Reverse(original, working, histories));
    }

    public (decimal OnHand, decimal Value, decimal? Luc) State(string item, string loc)
    {
        var key = new BalanceKey(ItemId(item), LocationId(loc));
        return _balances.TryGetValue(key, out var b) ? (b.OnHand, b.StockValue, b.LastUnitCost) : (0m, 0m, null);
    }

    public void AssertInvariants(string step)
    {
        foreach (var b in _balances.Values)
        {
            var touching = Touching(b.Key).ToList();
            var qtyIn = touching.Where(m => m.ToLocationId == b.LocationId).Sum(m => m.Quantity);
            var qtyOut = touching.Where(m => m.FromLocationId == b.LocationId).Sum(m => m.Quantity);
            var costIn = touching.Where(m => m.ToLocationId == b.LocationId).Sum(m => m.TotalCost);
            var costOut = touching.Where(m => m.FromLocationId == b.LocationId).Sum(m => m.TotalCost);

            // INV-1
            b.OnHand.ShouldBe(qtyIn - qtyOut, $"{step} INV-1 OnHand");
            if (b.IsVirtual)
            {
                b.StockValue.ShouldBe(0m, $"{step} INV-1 virtual StockValue");
                b.LastUnitCost.ShouldBeNull($"{step} INV-1 virtual LastUnitCost");
            }
            else
            {
                b.StockValue.ShouldBe(costIn - costOut, $"{step} INV-1 StockValue");

                // INV-3 (all toggles are off)
                b.AllowNegative.ShouldBeFalse($"{step} INV-3 toggle");
                b.OnHand.ShouldBeGreaterThanOrEqualTo(0m, $"{step} INV-3 OnHand");
                b.StockValue.ShouldBeGreaterThanOrEqualTo(0m, $"{step} INV-3 StockValue");
                if (b.OnHand == 0m)
                {
                    b.StockValue.ShouldBe(0m, $"{step} INV-3 empty balance has value");
                }

                // INV-5
                b.LastUnitCost.ShouldBe(CostingPolicy.LastUnitCost(b.Key, touching), $"{step} INV-5 LastUnitCost");
            }
        }

        // INV-2
        foreach (var group in _balances.Values.GroupBy(b => b.ItemId))
        {
            group.Sum(b => b.OnHand).ShouldBe(0m, $"{step} INV-2 item sum");
        }

        foreach (var m in _movements)
        {
            // INV-4
            if (m.ReversesMovementId is { } originalId)
            {
                var original = _movements.Single(o => o.Id == originalId);
                m.ItemId.ShouldBe(original.ItemId, $"{step} INV-4 item of {m.Id}");
                m.Quantity.ShouldBe(original.Quantity, $"{step} INV-4 quantity of {m.Id}");
                m.TotalCost.ShouldBe(original.TotalCost, $"{step} INV-4 cost of {m.Id}");
                m.FromLocationId.ShouldBe(original.ToLocationId, $"{step} INV-4 from of {m.Id}");
                m.ToLocationId.ShouldBe(original.FromLocationId, $"{step} INV-4 to of {m.Id}");
            }

            // DB CHECK backstop (Persistence/InventoryDbContext.cs:112-114)
            m.TotalCost.ShouldBeGreaterThanOrEqualTo(0m, $"{step} CHECK TotalCost of {m.Id}");
            (m.Quantity > 0m || (m.Quantity == 0m && m.TotalCost > 0m)).ShouldBeTrue($"{step} CHECK Quantity of {m.Id}");
            (m.FromLocationId != m.ToLocationId).ShouldBeTrue($"{step} CHECK From != To of {m.Id}");
        }
    }

    private Posted Post(PostingKind kind, string item, string from, string to, decimal qty, decimal? unitCost)
    {
        var itemId = ItemId(item);
        var fromId = LocationId(from);
        var toId = LocationId(to);
        var line = new PostingLine(1, itemId, fromId, toId, qty, unitCost);

        var working = WorkingCopy([new BalanceKey(itemId, fromId), new BalanceKey(itemId, toId)]);
        return Commit(PostingRules.Post(kind, [line], working));
    }

    // Step 5: a copy of the state plus every missing touched balance. The state itself is untouched until commit.
    private Dictionary<BalanceKey, Balance> WorkingCopy(IEnumerable<BalanceKey> touched)
    {
        var working = new Dictionary<BalanceKey, Balance>(_balances);
        foreach (var key in touched)
        {
            if (!working.ContainsKey(key))
            {
                working[key] = new Balance(key.ItemId, key.LocationId, _isVirtual[key.LocationId], false, 0m, 0m, null, null);
            }
        }

        return working;
    }

    private Posted Commit(PostingResult result)
    {
        var documentId = Guid.NewGuid();
        var posted = new List<LedgerMovement>(result.Movements.Count);
        foreach (var p in result.Movements)
        {
            posted.Add(new LedgerMovement(
                _nextId++, documentId, p.LineNo, p.ItemId, p.FromLocationId, p.ToLocationId, p.Quantity, p.TotalCost, p.ReversesMovementId));
        }

        _movements.AddRange(posted);
        foreach (var balance in result.Balances)
        {
            var last = posted
                .Where(m => m.ItemId == balance.ItemId && (m.FromLocationId == balance.LocationId || m.ToLocationId == balance.LocationId))
                .Max(m => m.Id);
            _balances[balance.Key] = balance with { LastMovementId = last };
        }

        return new Posted(documentId, posted);
    }

    private IEnumerable<LedgerMovement> Touching(BalanceKey key) =>
        _movements.Where(m => m.ItemId == key.ItemId && (m.FromLocationId == key.LocationId || m.ToLocationId == key.LocationId));

    public Guid ItemId(string name)
    {
        if (!_items.TryGetValue(name, out var id))
        {
            _items[name] = id = Guid.NewGuid();
        }

        return id;
    }

    public Guid LocationId(string name)
    {
        if (!_locations.TryGetValue(name, out var id))
        {
            _locations[name] = id = Guid.NewGuid();
            _isVirtual[id] = false;
        }

        return id;
    }
}