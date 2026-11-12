using Inventory.Domain;

using Microsoft.Data.SqlClient;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// The ledger invariants INV-1, INV-2, INV-3 and INV-5 (spec §6), folded from the tenant's <c>StockMovements</c> and
/// checked against its <c>StockBalances</c>. It ports the in-memory oracle <c>LedgerHarness.AssertInvariants</c> from
/// the unit tests rule for rule, reading the rows from SQL instead. INV-4 (the reversal mirror) is added with reversals
/// in S9, and S11's health endpoint is the product check; this is the test oracle.
/// </summary>
internal static class LedgerInvariants
{
    private sealed record BalanceRow(
        Guid ItemId, Guid LocationId, bool IsVirtual, bool AllowNegative, decimal OnHand, decimal StockValue, decimal? LastUnitCost, long? LastMovementId);

    /// <summary>
    /// Asserts the invariants over every row <paramref name="tenantConnection"/> can see. It comes from
    /// <see cref="LedgerRows.OpenAsync"/>, so RLS limits it to one tenant.
    /// </summary>
    public static async Task AssertHoldAsync(SqlConnection tenantConnection, CancellationToken ct)
    {
        var movements = await MovementsAsync(tenantConnection, ct);
        var balances = await BalancesAsync(tenantConnection, ct);
        var failures = new List<string>();

        foreach (var b in balances)
        {
            var key = new BalanceKey(b.ItemId, b.LocationId);

            // Only the item's movements that touch the location: CostingPolicy.LastUnitCost throws on any other.
            var touching = movements
                .Where(m => m.ItemId == b.ItemId && (m.FromLocationId == b.LocationId || m.ToLocationId == b.LocationId))
                .ToList();
            var qtyIn = touching.Where(m => m.ToLocationId == b.LocationId).Sum(m => m.Quantity);
            var qtyOut = touching.Where(m => m.FromLocationId == b.LocationId).Sum(m => m.Quantity);
            var costIn = touching.Where(m => m.ToLocationId == b.LocationId).Sum(m => m.TotalCost);
            var costOut = touching.Where(m => m.FromLocationId == b.LocationId).Sum(m => m.TotalCost);
            var lastMovementId = touching.Count == 0 ? (long?)null : touching.Max(m => m.Id);

            // INV-1
            Check(b.OnHand == qtyIn - qtyOut, $"INV-1 OnHand of {key}: {b.OnHand}, ledger {qtyIn - qtyOut}");
            Check(b.LastMovementId == lastMovementId, $"INV-1 LastMovementId of {key}: {b.LastMovementId}, ledger {lastMovementId}");
            if (b.IsVirtual)
            {
                Check(b.StockValue == 0m, $"INV-1 virtual StockValue of {key}: {b.StockValue}");
                Check(b.LastUnitCost is null, $"INV-1 virtual LastUnitCost of {key}: {b.LastUnitCost}");
                continue;
            }

            Check(b.StockValue == costIn - costOut, $"INV-1 StockValue of {key}: {b.StockValue}, ledger {costIn - costOut}");

            // INV-3
            if (b.OnHand >= 0m)
            {
                Check(b.StockValue >= 0m, $"INV-3 StockValue of {key}: {b.StockValue} with OnHand {b.OnHand}");
            }

            if (b.OnHand == 0m)
            {
                Check(b.StockValue == 0m, $"INV-3 empty balance {key} has value {b.StockValue}");
            }

            if (!b.AllowNegative)
            {
                Check(b.OnHand >= 0m, $"INV-3 OnHand of {key}: {b.OnHand} without AllowNegative");
            }

            // INV-5
            var lastUnitCost = CostingPolicy.LastUnitCost(key, touching);
            Check(b.LastUnitCost == lastUnitCost, $"INV-5 LastUnitCost of {key}: {b.LastUnitCost}, ledger {lastUnitCost}");
        }

        // INV-1: every (item, location) a movement touches has a balance row.
        var keys = balances.Select(b => new BalanceKey(b.ItemId, b.LocationId)).ToHashSet();
        foreach (var missing in movements
            .SelectMany(m => new[] { new BalanceKey(m.ItemId, m.FromLocationId), new BalanceKey(m.ItemId, m.ToLocationId) })
            .Distinct()
            .Where(k => !keys.Contains(k)))
        {
            failures.Add($"INV-1 no balance row for {missing}");
        }

        // INV-2
        foreach (var group in balances.GroupBy(b => b.ItemId))
        {
            var sum = group.Sum(b => b.OnHand);
            Check(sum == 0m, $"INV-2 OnHand of item {group.Key} sums to {sum}");
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));

        void Check(bool holds, string failure)
        {
            if (!holds)
            {
                failures.Add(failure);
            }
        }
    }

    private static async Task<List<LedgerMovement>> MovementsAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT Id, DocumentId, [LineNo], ItemId, FromLocationId, ToLocationId, Quantity, TotalCost, ReversesMovementId
            FROM inventory.StockMovements
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var movements = new List<LedgerMovement>();
        while (await reader.ReadAsync(ct))
        {
            movements.Add(new LedgerMovement(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8)));
        }

        return movements;
    }

    private static async Task<List<BalanceRow>> BalancesAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT ItemId, LocationId, IsVirtual, AllowNegative, OnHand, StockValue, LastUnitCost, LastMovementId
            FROM inventory.StockBalances
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var balances = new List<BalanceRow>();
        while (await reader.ReadAsync(ct))
        {
            balances.Add(new BalanceRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7)));
        }

        return balances;
    }
}