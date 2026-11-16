using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Ledger.GetLedgerHealth;

/// <summary>
/// INV-1 to INV-5 for the current tenant (spec §6, plan D3). It takes no lock and opens no transaction: each check is
/// one statement, so under RCSI each reads one consistent snapshot while postings continue. There are four statements:
/// the projection (INV-1 and INV-5), INV-2, INV-3 and INV-4.
/// TenantId is explicit on every table: EF's tenant filter does not apply to SqlQuery on an unmapped type.
/// </summary>
internal sealed class GetLedgerHealthHandler(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)
{
    private const int MaxFailures = 100;

    public async Task<Ok<LedgerHealthResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;

        var (projection, lastUnitCost) = await ProjectionAsync(tenantId, cancellationToken);
        var conservation = await ConservationAsync(tenantId, cancellationToken);
        var physical = await PhysicalSanityAsync(tenantId, cancellationToken);
        var mirror = await ReversalMirrorAsync(tenantId, cancellationToken);

        List<InvariantResult> invariants =
        [
            Result("INV-1", "Projection", projection),
            Result("INV-2", "Conservation", conservation),
            Result("INV-3", "Physical sanity", physical),
            Result("INV-4", "Reversal mirror", mirror),
            Result("INV-5", "Last unit cost", lastUnitCost),
        ];

        return TypedResults.Ok(new LedgerHealthResponse(clock.GetUtcNow(), invariants.All(i => i.Passed), invariants));
    }

    /// <summary>INV-1 and INV-5 from one read of <see cref="LedgerProjection"/>: expected is the ledger, actual the stored row (D8).</summary>
    private async Task<(List<InvariantFailure> Projection, List<InvariantFailure> LastUnitCost)> ProjectionAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var projection = new List<InvariantFailure>();
        var lastUnitCost = new List<InvariantFailure>();

        foreach (var row in await LedgerProjection.ReadAsync(db, tenantId, cancellationToken))
        {
            if (row.Stored is not { } stored)
            {
                projection.Add(Failure(row, "MissingBalance", null, null));
                continue;
            }

            var ledger = row.Ledger;
            if (stored.OnHand != ledger.OnHand)
            {
                projection.Add(Failure(row, "OnHand", ledger.OnHand, stored.OnHand));
            }

            if (stored.StockValue != ledger.StockValue)
            {
                projection.Add(Failure(row, "StockValue", ledger.StockValue, stored.StockValue));
            }

            if (stored.LastMovementId != ledger.LastMovementId)
            {
                projection.Add(Failure(row, "LastMovementId", ledger.LastMovementId, stored.LastMovementId));
            }

            // A virtual row's LastUnitCost is always null, so a value there is projection drift; a physical one is INV-5.
            if (stored.LastUnitCost != ledger.LastUnitCost)
            {
                (row.IsVirtual ? projection : lastUnitCost).Add(Failure(row, "LastUnitCost", ledger.LastUnitCost, stored.LastUnitCost));
            }
        }

        return (projection, lastUnitCost);

        static InvariantFailure Failure(ProjectedBalance row, string check, decimal? expected, decimal? actual) =>
            new(row.ItemId, row.Sku, row.LocationId, row.LocationCode, null, check, expected, actual);
    }

    /// <summary>INV-2: every item's OnHand sums to 0 across all its locations, virtual included.</summary>
    private async Task<List<InvariantFailure>> ConservationAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<ConservationRow>($"""
            SELECT b.ItemId, i.Sku, SUM(b.OnHand) AS Total
            FROM [inventory].[StockBalances] AS b
            JOIN [inventory].[Items] AS i ON i.TenantId = {tenantId} AND i.Id = b.ItemId
            WHERE b.TenantId = {tenantId}
            GROUP BY b.ItemId, i.Sku
            HAVING SUM(b.OnHand) <> 0
            """).ToListAsync(cancellationToken);

        return [.. rows.Select(r => new InvariantFailure(r.ItemId, r.Sku, null, null, null, "OnHandSum", 0m, r.Total))];
    }

    /// <summary>
    /// INV-3 on physical balances, read against the location rather than the balance's mirror (D7). Each row yields at
    /// most one failure, in priority order. The balance CHECKs (InventoryDbContext CK_StockBalances_Physical) already
    /// forbid the last two on a row whose mirror is right, so the reachable failure is a mirror that says allowed over a
    /// location that says no.
    /// </summary>
    private async Task<List<InvariantFailure>> PhysicalSanityAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<PhysicalRow>($"""
            SELECT b.ItemId, i.Sku, b.LocationId, l.Code AS LocationCode, c.[Check],
                   IIF(c.[Check] = 'NegativeNotAllowed', b.OnHand, b.StockValue) AS Actual
            FROM [inventory].[StockBalances] AS b
            JOIN [inventory].[Locations] AS l ON l.TenantId = {tenantId} AND l.Id = b.LocationId AND l.Kind = 'Physical'
            JOIN [inventory].[Items] AS i ON i.TenantId = {tenantId} AND i.Id = b.ItemId
            CROSS APPLY (
                SELECT CASE
                    WHEN b.OnHand < 0 AND l.AllowNegativeStock = 0 THEN 'NegativeNotAllowed'
                    WHEN b.OnHand >= 0 AND b.StockValue < 0 THEN 'ValueWithStock'
                    WHEN b.OnHand = 0 AND b.StockValue <> 0 THEN 'ValueWithoutStock'
                END AS [Check]
            ) AS c
            WHERE b.TenantId = {tenantId} AND c.[Check] IS NOT NULL
            """).ToListAsync(cancellationToken);

        return [.. rows.Select(r => new InvariantFailure(r.ItemId, r.Sku, r.LocationId, r.LocationCode, null, r.Check, null, r.Actual))];
    }

    /// <summary>
    /// INV-4, one row per (movement, check), matching the test oracle LedgerInvariants: a reversal mirrors its original's
    /// item, quantity and cost with From and To swapped, never reverses a reversal, and no movement is reversed twice.
    /// The FK guarantees the original exists. ReversedTwice reports the original, with the reversal count as actual.
    /// </summary>
    private async Task<List<InvariantFailure>> ReversalMirrorAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<MirrorRow>($"""
            SELECT f.MovementId, f.ItemId, i.Sku, f.[Check], f.Expected, f.Actual
            FROM (
                SELECT r.Id AS MovementId, r.ItemId, c.[Check], c.Expected, c.Actual
                FROM [inventory].[StockMovements] AS r
                JOIN [inventory].[StockMovements] AS o ON o.TenantId = {tenantId} AND o.Id = r.ReversesMovementId
                CROSS APPLY (VALUES
                    ('ReversesReversal', IIF(o.ReversesMovementId IS NOT NULL, 1, 0), CAST(NULL AS decimal(19, 4)), CAST(NULL AS decimal(19, 4))),
                    ('Item', IIF(r.ItemId <> o.ItemId, 1, 0), NULL, NULL),
                    ('Quantity', IIF(r.Quantity <> o.Quantity, 1, 0), o.Quantity, r.Quantity),
                    ('TotalCost', IIF(r.TotalCost <> o.TotalCost, 1, 0), o.TotalCost, r.TotalCost),
                    ('FromLocation', IIF(r.FromLocationId <> o.ToLocationId, 1, 0), NULL, NULL),
                    ('ToLocation', IIF(r.ToLocationId <> o.FromLocationId, 1, 0), NULL, NULL)
                ) AS c([Check], Failed, Expected, Actual)
                WHERE r.TenantId = {tenantId} AND r.ReversesMovementId IS NOT NULL AND c.Failed = 1
                UNION ALL
                SELECT o.Id, o.ItemId, 'ReversedTwice', NULL, CAST(g.Reversals AS decimal(19, 4))
                FROM (
                    SELECT m.ReversesMovementId, COUNT(*) AS Reversals
                    FROM [inventory].[StockMovements] AS m
                    WHERE m.TenantId = {tenantId} AND m.ReversesMovementId IS NOT NULL
                    GROUP BY m.ReversesMovementId
                    HAVING COUNT(*) > 1
                ) AS g
                JOIN [inventory].[StockMovements] AS o ON o.TenantId = {tenantId} AND o.Id = g.ReversesMovementId
            ) AS f
            JOIN [inventory].[Items] AS i ON i.TenantId = {tenantId} AND i.Id = f.ItemId
            """).ToListAsync(cancellationToken);

        return [.. rows.Select(r => new InvariantFailure(r.ItemId, r.Sku, null, null, r.MovementId, r.Check, r.Expected, r.Actual))];
    }

    private static InvariantResult Result(string id, string name, List<InvariantFailure> failures) => new(
        id,
        name,
        failures.Count == 0,
        failures.Count,
        [.. failures
            .OrderBy(f => f.Sku, StringComparer.Ordinal)
            .ThenBy(f => f.LocationCode, StringComparer.Ordinal)
            .ThenBy(f => f.MovementId)
            .ThenBy(f => f.Check, StringComparer.Ordinal)
            .Take(MaxFailures)]);

    private sealed class ConservationRow
    {
        public Guid ItemId { get; init; }

        public string Sku { get; init; } = "";

        public decimal Total { get; init; }
    }

    private sealed class PhysicalRow
    {
        public Guid ItemId { get; init; }

        public string Sku { get; init; } = "";

        public Guid LocationId { get; init; }

        public string LocationCode { get; init; } = "";

        public string Check { get; init; } = "";

        public decimal Actual { get; init; }
    }

    private sealed class MirrorRow
    {
        public long MovementId { get; init; }

        public Guid ItemId { get; init; }

        public string Sku { get; init; } = "";

        public string Check { get; init; } = "";

        public decimal? Expected { get; init; }

        public decimal? Actual { get; init; }
    }
}