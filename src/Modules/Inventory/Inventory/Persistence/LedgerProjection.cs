using System.Data.SqlTypes;

using Inventory.Domain;

using Microsoft.EntityFrameworkCore;

namespace Inventory.Persistence;

/// <summary>A balance's four values, as stored in <c>StockBalances</c> or as derived from the ledger.</summary>
internal sealed record BalanceValues(decimal OnHand, decimal StockValue, decimal? LastUnitCost, long? LastMovementId);

/// <summary>One <c>(item, location)</c> key of the tenant: its stored balance next to the value the ledger gives it.</summary>
internal sealed record ProjectedBalance(
    Guid ItemId,
    string Sku,
    Guid LocationId,
    string LocationCode,
    bool IsVirtual,
    bool AllowNegativeStock,
    BalanceValues? Stored,
    BalanceValues Ledger)
{
    /// <summary>True when the row is missing or any of its values differs from the ledger.</summary>
    public bool Differs => Stored != Ledger;
}

/// <summary>
/// The tenant's balances projected from <c>StockMovements</c> (spec §6 rebuild and INV-1/INV-5, §7), shared by S11's
/// health check and rebuild. One statement sums the ledger exactly per key and returns the inputs of
/// <c>LastUnitCost</c> without dividing (plan D2): T-SQL decimal division cuts the scale to 6, so it couldn't be proven
/// equal to C#. The one division is <see cref="CostingPolicy.LastUnitCostAfterInflow"/>'s, so the projected
/// <c>LastUnitCost</c> equals <see cref="CostingPolicy.LastUnitCost"/> by construction. A balance row with no movements
/// projects to 0 / 0 / null / null (IMS-14). It takes no lock: under RCSI the single statement reads one snapshot.
/// </summary>
internal static class LedgerProjection
{
    /// <summary>Every key that has a balance row or a movement, in clustered key order (ItemId, then LocationId).</summary>
    public static async Task<List<ProjectedBalance>> ReadAsync(InventoryDbContext db, Guid tenantId, CancellationToken ct)
    {
        // Each movement splits into an In leg on its To side and an Out leg on its From side.
        // The inflow follows CostingPolicy.LastUnitCost rule for rule: the latest In leg with Quantity > 0 that is not a
        // reversal and was not reversed; its end is the key's last leg in the inflow's document, so its revaluation counts.
        // No LINQ is composed on top, so EF sends the statement as written and the CTE is allowed.
        // TenantId is explicit on every table: EF's tenant filter does not apply to SqlQuery on an unmapped type.
        var rows = await db.Database.SqlQuery<Row>($"""
            WITH legs AS (
                SELECT m.Id, m.DocumentId, m.ItemId, m.ToLocationId AS LocationId, 1 AS IsIn,
                       m.Quantity AS QuantityChange, m.TotalCost AS ValueChange, m.Quantity, m.TotalCost, m.ReversesMovementId
                FROM [inventory].[StockMovements] AS m
                WHERE m.TenantId = {tenantId}
                UNION ALL
                SELECT m.Id, m.DocumentId, m.ItemId, m.FromLocationId, 0,
                       -m.Quantity, -m.TotalCost, m.Quantity, m.TotalCost, m.ReversesMovementId
                FROM [inventory].[StockMovements] AS m
                WHERE m.TenantId = {tenantId}
            ),
            sums AS (
                SELECT g.ItemId, g.LocationId,
                       SUM(g.QuantityChange) AS LedgerOnHand, SUM(g.ValueChange) AS LedgerValue, MAX(g.Id) AS LedgerLastMovementId
                FROM legs AS g
                GROUP BY g.ItemId, g.LocationId
            ),
            keys AS (
                SELECT b.ItemId, b.LocationId
                FROM [inventory].[StockBalances] AS b
                WHERE b.TenantId = {tenantId}
                UNION
                SELECT s.ItemId, s.LocationId
                FROM sums AS s
            )
            SELECT k.ItemId, i.Sku, k.LocationId, l.Code AS LocationCode,
                   CAST(IIF(l.Kind <> 'Physical', 1, 0) AS bit) AS IsVirtual, l.AllowNegativeStock,
                   CAST(IIF(b.ItemId IS NULL, 0, 1) AS bit) AS HasRow,
                   b.OnHand AS StoredOnHand, b.StockValue AS StoredValue,
                   b.LastUnitCost AS StoredLastUnitCost, b.LastMovementId AS StoredLastMovementId,
                   ISNULL(s.LedgerOnHand, 0) AS LedgerOnHand, ISNULL(s.LedgerValue, 0) AS LedgerValue, s.LedgerLastMovementId,
                   f.InflowQuantity, f.InflowCost, a.OnHandAtInflow, a.ValueAtInflow
            FROM keys AS k
            JOIN [inventory].[Items] AS i ON i.TenantId = {tenantId} AND i.Id = k.ItemId
            JOIN [inventory].[Locations] AS l ON l.TenantId = {tenantId} AND l.Id = k.LocationId
            LEFT JOIN [inventory].[StockBalances] AS b
                ON b.TenantId = {tenantId} AND b.ItemId = k.ItemId AND b.LocationId = k.LocationId
            LEFT JOIN sums AS s ON s.ItemId = k.ItemId AND s.LocationId = k.LocationId
            OUTER APPLY (
                SELECT TOP (1) g.Id, g.DocumentId, g.Quantity AS InflowQuantity, g.TotalCost AS InflowCost
                FROM legs AS g
                WHERE l.Kind = 'Physical'
                  AND g.ItemId = k.ItemId AND g.LocationId = k.LocationId
                  AND g.IsIn = 1 AND g.Quantity > 0 AND g.ReversesMovementId IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM [inventory].[StockMovements] AS r
                      WHERE r.TenantId = {tenantId} AND r.ReversesMovementId = g.Id)
                ORDER BY g.Id DESC
            ) AS f
            OUTER APPLY (
                SELECT MAX(e.Id) AS EndId
                FROM legs AS e
                WHERE e.ItemId = k.ItemId AND e.LocationId = k.LocationId AND e.DocumentId = f.DocumentId
            ) AS x
            OUTER APPLY (
                SELECT SUM(g.QuantityChange) AS OnHandAtInflow, SUM(g.ValueChange) AS ValueAtInflow
                FROM legs AS g
                WHERE g.ItemId = k.ItemId AND g.LocationId = k.LocationId AND g.Id <= x.EndId
            ) AS a
            """).ToListAsync(ct);

        return rows
            .Select(r => new ProjectedBalance(
                r.ItemId,
                r.Sku,
                r.LocationId,
                r.LocationCode,
                r.IsVirtual,
                r.AllowNegativeStock,
                r.HasRow ? new BalanceValues(r.StoredOnHand!.Value, r.StoredValue!.Value, r.StoredLastUnitCost, r.StoredLastMovementId) : null,
                Ledger(r)))
            .OrderBy(p => new SqlGuid(p.ItemId))
            .ThenBy(p => new SqlGuid(p.LocationId))
            .ToList();
    }

    private static BalanceValues Ledger(Row r)
    {
        if (r.IsVirtual)
        {
            return new BalanceValues(r.LedgerOnHand, 0m, null, r.LedgerLastMovementId);
        }

        var lastUnitCost = r.InflowQuantity is { } quantity
            ? CostingPolicy.LastUnitCostAfterInflow(r.OnHandAtInflow!.Value, r.ValueAtInflow!.Value, quantity, r.InflowCost!.Value)
            : (decimal?)null;
        return new BalanceValues(r.LedgerOnHand, r.LedgerValue, lastUnitCost, r.LedgerLastMovementId);
    }

    private sealed class Row
    {
        public Guid ItemId { get; init; }

        public string Sku { get; init; } = "";

        public Guid LocationId { get; init; }

        public string LocationCode { get; init; } = "";

        public bool IsVirtual { get; init; }

        public bool AllowNegativeStock { get; init; }

        public bool HasRow { get; init; }

        public decimal? StoredOnHand { get; init; }

        public decimal? StoredValue { get; init; }

        public decimal? StoredLastUnitCost { get; init; }

        public long? StoredLastMovementId { get; init; }

        public decimal LedgerOnHand { get; init; }

        public decimal LedgerValue { get; init; }

        public long? LedgerLastMovementId { get; init; }

        public decimal? InflowQuantity { get; init; }

        public decimal? InflowCost { get; init; }

        public decimal? OnHandAtInflow { get; init; }

        public decimal? ValueAtInflow { get; init; }
    }
}