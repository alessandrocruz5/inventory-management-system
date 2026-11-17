using System.Data;

using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Inventory.Features.Ledger.RebuildLedger;

/// <summary>
/// Spec §6 Rebuild, plan D4: under the tenant's ledger lock in <see cref="LedgerLockMode.Exclusive"/> mode, compares
/// every balance with <see cref="LedgerProjection"/> and overwrites the drifted ones in the same transaction, returning
/// the differences. A healthy ledger writes nothing. Only <c>StockBalances</c> is written, and only its four
/// ledger-derived columns; <c>IsVirtual</c> and <c>AllowNegative</c> stay as they are.
/// A <c>547</c> here means a balance row was corrupted by hand into a state the ledger can't restore under the current
/// toggle (plan Assumption); the transaction rolls back.
/// </summary>
internal sealed class RebuildLedgerHandler(
    InventoryDbContext db, ITenantContext tenant, ICurrentUser user, TimeProvider clock, ILogger<RebuildLedgerHandler> logger)
{
    public async Task<Results<Ok<LedgerRebuildResponse>, ProblemHttpResult>> HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(AttemptAsync, cancellationToken);
        }
        catch (RetryLimitExceededException)
        {
            return Problems.ConcurrencyConflict();
        }
    }

    private async Task<Results<Ok<LedgerRebuildResponse>, ProblemHttpResult>> AttemptAsync(CancellationToken cancellationToken)
    {
        // A retry starts clean: an entity tracked by the failed attempt must not leak into this one.
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // First statement: no other lock is held, so the applock can't be in a deadlock cycle (LedgerLock).
        var tenantId = tenant.TenantId;
        await LedgerLock.AcquireAsync(db.Database, tenantId, LedgerLockMode.Exclusive, cancellationToken);

        var rows = await LedgerProjection.ReadAsync(db, tenantId, cancellationToken);
        var drifted = rows.Where(r => r.Differs).ToList();

        // No set-based UPDATE … FROM over the tenant: key-targeted writes lock only the drifted rows, in clustered order.
        foreach (var row in drifted)
        {
            var ledger = row.Ledger;
            // Decimals go as decimal(19,4) parameters: EF's value-based mapping would send decimal(18,2) and round them.
            var onHand = DecimalParameter("@onHand", ledger.OnHand);
            var stockValue = DecimalParameter("@stockValue", ledger.StockValue);
            var lastUnitCost = DecimalParameter("@lastUnitCost", ledger.LastUnitCost);
            int affected;
            if (row.Stored is null)
            {
                // TenantId comes from the column default (the session context); the BLOCK predicate checks it.
                affected = await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO [inventory].[StockBalances] ([ItemId], [LocationId], [IsVirtual], [AllowNegative], [OnHand], [StockValue], [LastUnitCost], [LastMovementId])
                    SELECT {row.ItemId}, l.[Id], IIF(l.[Kind] <> 'Physical', 1, 0), l.[AllowNegativeStock], {onHand}, {stockValue}, {lastUnitCost}, {ledger.LastMovementId}
                    FROM [inventory].[Locations] AS l
                    WHERE l.[TenantId] = {tenantId} AND l.[Id] = {row.LocationId}
                    """, cancellationToken);
            }
            else
            {
                // No rowversion check (plan D4): the applock excludes postings, and a toggle writes only AllowNegative.
                affected = await db.Database.ExecuteSqlAsync($"""
                    UPDATE [inventory].[StockBalances]
                    SET [OnHand] = {onHand}, [StockValue] = {stockValue}, [LastUnitCost] = {lastUnitCost}, [LastMovementId] = {ledger.LastMovementId}
                    WHERE [TenantId] = {tenantId} AND [ItemId] = {row.ItemId} AND [LocationId] = {row.LocationId}
                    """, cancellationToken);
            }

            if (affected != 1)
            {
                throw new InvalidOperationException(
                    $"Rebuild wrote {affected} rows for balance ({row.ItemId}, {row.LocationId}), expected 1.");
            }
        }

        // With no drift nothing was written, and the commit only releases the lock.
        await tx.CommitAsync(cancellationToken);

        if (drifted.Count > 0)
        {
            logger.LogWarning(
                "Ledger rebuild for tenant {TenantId} by user {UserId} fixed {Count} balances", tenantId, user.UserId, drifted.Count);
        }

        var differences = drifted
            .OrderBy(r => r.Sku, StringComparer.Ordinal)
            .ThenBy(r => r.LocationCode, StringComparer.Ordinal)
            .Select(r => new BalanceDifference(
                r.ItemId,
                r.Sku,
                r.LocationId,
                r.LocationCode,
                Inserted: r.Stored is null,
                Before: r.Stored is { } stored ? Snapshot(stored) : null,
                After: Snapshot(r.Ledger)))
            .ToList();

        return TypedResults.Ok(new LedgerRebuildResponse(clock.GetUtcNow(), rows.Count, differences));
    }

    private static SqlParameter DecimalParameter(string name, decimal? value) =>
        new(name, SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = (object?)value ?? DBNull.Value };

    private static BalanceSnapshot Snapshot(BalanceValues values) =>
        new(values.OnHand, values.StockValue, values.LastUnitCost, values.LastMovementId);
}