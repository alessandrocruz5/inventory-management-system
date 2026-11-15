using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.ListItemMovements;

/// <summary>The item's ledger history as a stock card: one row per movement per physical location, with running balances (spec §9).</summary>
internal sealed class ListItemMovementsHandler(InventoryDbContext db, ITenantContext tenant)
{
    public async Task<Results<Ok<PagedResponse<ItemMovementResponse>>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        ListItemMovementsRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (page, pageSize) = Paging.Resolve(request.Page, request.PageSize, errors);
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        // Through EF, so the named tenant filter applies: a foreign item id is invisible, hence 404 (spec §8).
        if (!await db.Items.AnyAsync(i => i.Id == id, cancellationToken))
        {
            return Problems.NotFound();
        }

        var tenantId = tenant.TenantId;

        // Each movement splits into an In row (To side) and an Out row (From side); virtual locations get no row.
        // The window runs in the derived table, before paging, so a later page's running values span the whole history.
        // No ORDER BY, CTE or trailing semicolon: EF wraps SqlQuery in a derived table to compose the LINQ below.
        // TenantId is explicit on every table: EF's tenant filter does not apply to SqlQuery on an unmapped type.
        var rows = db.Database.SqlQuery<ItemMovementRow>($"""
            SELECT e.MovementId, e.DocumentId, d.[Type] AS DocumentType, e.[LineNo], e.PostedAt, e.PostedBy, e.Direction,
                   e.LocationId, l.Code AS LocationCode, l.Name AS LocationName,
                   e.CounterpartLocationId, c.Code AS CounterpartLocationCode, c.Name AS CounterpartLocationName,
                   e.QuantityChange, e.ValueChange,
                   SUM(e.QuantityChange) OVER (PARTITION BY e.LocationId ORDER BY e.MovementId ROWS UNBOUNDED PRECEDING) AS RunningOnHand,
                   SUM(e.ValueChange) OVER (PARTITION BY e.LocationId ORDER BY e.MovementId ROWS UNBOUNDED PRECEDING) AS RunningValue
            FROM (
                SELECT m.Id AS MovementId, m.DocumentId, m.[LineNo], m.PostedAt, m.PostedBy, 'In' AS Direction,
                       m.ToLocationId AS LocationId, m.FromLocationId AS CounterpartLocationId,
                       m.Quantity AS QuantityChange, m.TotalCost AS ValueChange
                FROM [inventory].[StockMovements] AS m
                WHERE m.TenantId = {tenantId} AND m.ItemId = {id}
                UNION ALL
                SELECT m.Id, m.DocumentId, m.[LineNo], m.PostedAt, m.PostedBy, 'Out',
                       m.FromLocationId, m.ToLocationId, -m.Quantity, -m.TotalCost
                FROM [inventory].[StockMovements] AS m
                WHERE m.TenantId = {tenantId} AND m.ItemId = {id}
            ) AS e
            JOIN [inventory].[Locations] AS l ON l.TenantId = {tenantId} AND l.Id = e.LocationId AND l.Kind = 'Physical'
            JOIN [inventory].[Locations] AS c ON c.TenantId = {tenantId} AND c.Id = e.CounterpartLocationId
            JOIN [inventory].[StockDocuments] AS d ON d.TenantId = {tenantId} AND d.Id = e.DocumentId
            """);

        var result = await rows
            .OrderByDescending(r => r.MovementId)
            .ThenBy(r => r.Direction)
            .ToPageAsync(page, pageSize, cancellationToken);

        return TypedResults.Ok(new PagedResponse<ItemMovementResponse>(
            [.. result.Items.Select(r => new ItemMovementResponse(
                r.MovementId, r.DocumentId, r.DocumentType, r.LineNo, r.PostedAt, r.PostedBy, r.Direction,
                r.LocationId, r.LocationCode, r.LocationName,
                r.CounterpartLocationId, r.CounterpartLocationCode, r.CounterpartLocationName,
                r.QuantityChange, r.ValueChange, r.RunningOnHand, r.RunningValue))],
            result.Page,
            result.PageSize,
            result.Total));
    }

    private sealed class ItemMovementRow
    {
        public long MovementId { get; init; }

        public Guid DocumentId { get; init; }

        public string DocumentType { get; init; } = "";

        public int LineNo { get; init; }

        public DateTimeOffset PostedAt { get; init; }

        public Guid PostedBy { get; init; }

        public string Direction { get; init; } = "";

        public Guid LocationId { get; init; }

        public string LocationCode { get; init; } = "";

        public string LocationName { get; init; } = "";

        public Guid CounterpartLocationId { get; init; }

        public string CounterpartLocationCode { get; init; } = "";

        public string CounterpartLocationName { get; init; } = "";

        public decimal QuantityChange { get; init; }

        public decimal ValueChange { get; init; }

        public decimal RunningOnHand { get; init; }

        public decimal RunningValue { get; init; }
    }
}