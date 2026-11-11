using System.Data;
using System.Data.SqlTypes;
using System.Globalization;

using Inventory.Domain;

using Kernel.Tenancy;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Persistence;

/// <summary>
/// The ledger's write path (spec §6): the step 2 lookup and steps 3–10. <see cref="PostingRules"/> decides; this class
/// owns the transaction, the locks and the SQL. Documents and movements are only ever inserted, never changed.
/// </summary>
internal sealed class PostingEngine(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)
{
    // The decimal(19,4) maximum. Equals PostingValidation.MaxColumn; Persistence doesn't reference Features.
    private const decimal MaxColumn = 999_999_999_999_999.9999m;

    /// <summary>
    /// Step 2: null when no document holds the key. The same hash replays the stored document with the current physical
    /// balances it touched (plan D3); a different hash is the key's reuse by another request.
    /// </summary>
    public async Task<PostingOutcome?> FindExistingAsync(string key, byte[] hash, CancellationToken ct)
    {
        var document = await db.StockDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.IdempotencyKey == key, ct);
        if (document is null)
        {
            return null;
        }

        if (!document.RequestHash.AsSpan().SequenceEqual(hash))
        {
            return new KeyReusedOutcome(document.Id);
        }

        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.DocumentId == document.Id)
            .OrderBy(m => m.LineNo)
            .ToListAsync(ct);

        var touched = movements.SelectMany(TouchedKeys).ToHashSet();
        var itemIds = touched.Select(k => k.ItemId).Distinct().ToList();
        var locationIds = touched.Select(k => k.LocationId).Distinct().ToList();
        var balances = await db.StockBalances.AsNoTracking()
            .Where(b => !b.IsVirtual && itemIds.Contains(b.ItemId) && locationIds.Contains(b.LocationId))
            .ToListAsync(ct);

        return new PostedOutcome(
            document,
            movements,
            PhysicalInKeyOrder(balances.Where(b => touched.Contains(new BalanceKey(b.ItemId, b.LocationId)))),
            Replayed: true,
            BalancesAsOf: clock.GetUtcNow());
    }

    /// <summary>
    /// Steps 3–10 in one transaction, inside the execution strategy so a deadlock (1205) retries up to 3 times (spec §6).
    /// Nothing is written unless the result is a <see cref="PostedOutcome"/> that isn't a replay.
    /// </summary>
    public async Task<PostingOutcome> PostAsync(PostingCommand command, CancellationToken ct)
    {
        PostingOutcome? outcome;
        try
        {
            outcome = await db.Database.CreateExecutionStrategy().ExecuteAsync(token => AttemptAsync(command, token), ct);

            // Null: a parallel duplicate committed the key first, so a row must exist now. Answer as step 2 would.
            outcome ??= await FindExistingAsync(command.IdempotencyKey, command.RequestHash, ct)
                ?? throw new InvalidOperationException(
                    $"Idempotency key '{command.IdempotencyKey}' hit a unique violation, but no document holds it.");
        }
        catch (RetryLimitExceededException)
        {
            outcome = new RetriesExhaustedOutcome();
        }

        if (outcome is not PostedOutcome { Replayed: false })
        {
            // A rolled-back attempt leaves its rows tracked; drop them so no later SaveChanges in this scope writes them.
            db.ChangeTracker.Clear();
        }

        return outcome;
    }

    /// <summary>One attempt at steps 3–10. Returns null when step 4 lost the key to a parallel duplicate.</summary>
    private async Task<PostingOutcome?> AttemptAsync(PostingCommand command, CancellationToken ct)
    {
        // A retry starts clean: an entity tracked by the failed attempt must not leak into this one.
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();

        // READ COMMITTED, with snapshot reads (RCSI is on).
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Step 3: the tenant's ledger lock. Postings share it; only S11's rebuild takes it exclusively.
        var lockResult = (await db.Database.SqlQueryRaw<int>(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Shared', @LockOwner = 'Transaction'; SELECT @r AS [Value];",
                new SqlParameter("@res", SqlDbType.NVarChar, 255) { Value = $"ledger:{tenant.TenantId:D}" })
            .ToListAsync(ct)).Single();
        if (lockResult < 0)
        {
            throw new InvalidOperationException($"sp_getapplock returned {lockResult} for the ledger lock.");
        }

        // Step 4: the document, final at insert because the trigger forbids changing it. Inserting it claims the key.
        var document = new StockDocument
        {
            Type = command.DocumentType,
            ReasonCode = command.ReasonCode,
            ReversesDocumentId = null,
            Reference = command.Reference,
            Notes = command.Notes,
            IdempotencyKey = command.IdempotencyKey,
            RequestHash = command.RequestHash,
            PostedAt = now,
            PostedBy = command.PostedBy,
        };
        db.StockDocuments.Add(document);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A parallel duplicate blocked us on the unique index, then committed. Returning without a commit rolls back.
            return null;
        }

        // Step 5: make sure every touched balance row exists, in clustered key order so concurrent inserts can't deadlock.
        var keys = command.Lines.SelectMany(l => TouchedKeys(l.ItemId, l.FromLocationId, l.ToLocationId))
            .Distinct()
            .OrderBy(k => new SqlGuid(k.ItemId))
            .ThenBy(k => new SqlGuid(k.LocationId))
            .ToList();
        foreach (var key in keys)
        {
            try
            {
                // TenantId comes from the column default (the session context); the BLOCK predicate checks it.
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO [inventory].[StockBalances] ([ItemId], [LocationId], [IsVirtual], [AllowNegative], [OnHand], [StockValue])
                    SELECT {key.ItemId}, l.[Id], IIF(l.[Kind] <> 'Physical', 1, 0), l.[AllowNegativeStock], 0, 0
                    FROM [inventory].[Locations] AS l
                    WHERE l.[Id] = {key.LocationId}
                      AND NOT EXISTS (SELECT 1 FROM [inventory].[StockBalances] AS b WHERE b.[ItemId] = {key.ItemId} AND b.[LocationId] = {key.LocationId});
                    """, ct);
            }
            catch (SqlException e) when (e.Number is 2601 or 2627)
            {
                // A concurrent posting inserted the row first. The error ends only the statement; the transaction goes on.
            }
        }

        // Step 6: lock every touched balance in one statement. The parameterized IN seek on the clustered key
        // (TenantId, ItemId, LocationId) orders the locks (spec §6), and INDEX pins that key: FORCESEEK alone could pick
        // IX_Balances_Location and lock in (LocationId, ItemId) order. RLS supplies the TenantId seek.
        var itemIds = keys.Select(k => k.ItemId).Distinct().OrderBy(id => new SqlGuid(id)).ToList();
        var locationIds = keys.Select(k => k.LocationId).Distinct().OrderBy(id => new SqlGuid(id)).ToList();
        var (lockLocations, lockLocationParameters) = InList("@l", locationIds);
        var (lockItems, lockItemParameters) = InList("@i", itemIds);

        // EF1003: the only concatenated text is the generated placeholders (@l0, @i0, …); every id is a SqlParameter.
#pragma warning disable EF1003
        var rows = await db.StockBalances
            .FromSqlRaw(
                "SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, ROWLOCK, FORCESEEK, INDEX(PK_StockBalances)) "
                + "WHERE [LocationId] IN (" + lockLocations + ") AND [ItemId] IN (" + lockItems + ")",
                [.. lockLocationParameters, .. lockItemParameters])
            .AsTracking()
            .ToListAsync(ct);

        // Every kind moves all its lines between exactly two locations: a receipt, issue or adjustment between the
        // location and its counterpart (an adjustment's lines may point either way), a transfer from A to B. So items ×
        // locations is the touched set.
        if (rows.Count != itemIds.Count * locationIds.Count)
        {
            throw new InvalidOperationException(
                $"Locked {rows.Count} balances, expected {itemIds.Count} items × {locationIds.Count} locations.");
        }

        // Step 7: re-read the master data with shared locks, so a deactivation that committed since step 1 is seen. The
        // filter stays in the SQL with FORCESEEK: a scan would S-lock every row, other tenants' included.
        var (readItems, readItemParameters) = InList("@i", itemIds);
        var items = (await db.Items
                .FromSqlRaw(
                    "SELECT * FROM [inventory].[Items] WITH (READCOMMITTEDLOCK, FORCESEEK) WHERE [Id] IN (" + readItems + ")",
                    readItemParameters)
                .AsNoTracking()
                .ToListAsync(ct))
            .ToDictionary(i => i.Id);
        var (readLocations, readLocationParameters) = InList("@l", locationIds);
        var locations = (await db.Locations
                .FromSqlRaw(
                    "SELECT * FROM [inventory].[Locations] WITH (READCOMMITTEDLOCK, FORCESEEK) WHERE [Id] IN (" + readLocations + ")",
                    readLocationParameters)
                .AsNoTracking()
                .ToListAsync(ct))
            .ToDictionary(l => l.Id);
#pragma warning restore EF1003

        var lines = command.Lines.OrderBy(l => l.LineNo).ToList();
        foreach (var line in lines)
        {
            if (!Found(items, line.ItemId, "item").IsActive)
            {
                return new InactiveItemOutcome(line.ItemId);
            }
        }

        foreach (var locationId in lines.SelectMany(l => new[] { l.FromLocationId, l.ToLocationId }))
        {
            if (!Found(locations, locationId, "location").IsActive)
            {
                return new InactiveLocationOutcome(locationId);
            }
        }

        // IsVirtual and AllowNegative come from this read, not from the row (spec §6 step 7).
        var balances = rows.ToDictionary(
            r => new BalanceKey(r.ItemId, r.LocationId),
            r => new Balance(
                r.ItemId,
                r.LocationId,
                IsVirtual: locations[r.LocationId].Kind != LocationKind.Physical,
                AllowNegative: locations[r.LocationId].AllowNegativeStock,
                r.OnHand,
                r.StockValue,
                r.LastUnitCost,
                r.LastMovementId));

        // Step 8: costs and new balances, in C#.
        PostingResult result;
        try
        {
            result = PostingRules.Post(command.Kind, command.Lines, balances);
        }
        catch (PostingRejectedException rejection)
        {
            return new RejectedOutcome(rejection);
        }

        // Plan D1: a value outside decimal(19,4) would fail the write with 8115, a 500.
        if (result.Balances.FirstOrDefault(b => Math.Abs(b.OnHand) > MaxColumn || Math.Abs(b.StockValue) > MaxColumn) is { } outOfRange)
        {
            return new BalanceOutOfRangeOutcome(outOfRange.ItemId, outOfRange.LocationId);
        }

        // Step 9: ids in LineNo order (Movements' order), the movement inserts and the balance updates, in one SaveChanges.
        var firstId = await TakeMovementIdsAsync(result.Movements.Count, ct);
        var movements = result.Movements
            .Select((m, i) => new StockMovement
            {
                Id = firstId + i,
                DocumentId = document.Id,
                LineNo = m.LineNo,
                ItemId = m.ItemId,
                FromLocationId = m.FromLocationId,
                ToLocationId = m.ToLocationId,
                Quantity = m.Quantity,
                TotalCost = m.TotalCost,
                ReversesMovementId = m.ReversesMovementId,
                PostedAt = now,
                PostedBy = command.PostedBy,
            })
            .ToList();
        db.StockMovements.AddRange(movements);

        // Each balance's last new movement, virtual balances included.
        var lastMovementIds = new Dictionary<BalanceKey, long>();
        foreach (var movement in movements)
        {
            foreach (var key in TouchedKeys(movement))
            {
                lastMovementIds[key] = lastMovementIds.TryGetValue(key, out var id) ? Math.Max(id, movement.Id) : movement.Id;
            }
        }

        var tracked = rows.ToDictionary(r => new BalanceKey(r.ItemId, r.LocationId));
        foreach (var balance in result.Balances)
        {
            var row = tracked[balance.Key];
            row.OnHand = balance.OnHand;
            row.StockValue = balance.StockValue;
            row.LastUnitCost = balance.LastUnitCost;
            row.AllowNegative = locations[row.LocationId].AllowNegativeStock;
            row.LastMovementId = lastMovementIds[balance.Key];
        }

        await db.SaveChangesAsync(ct);

        // Step 10.
        await tx.CommitAsync(ct);
        return new PostedOutcome(document, movements, PhysicalInKeyOrder(rows), Replayed: false, BalancesAsOf: now);
    }

    /// <summary>
    /// Takes <paramref name="count"/> contiguous ids from <c>inventory.MovementIds</c> in one call and returns the first.
    /// <c>inventory_app</c> has UPDATE on the sequence, which <c>sp_sequence_get_range</c> requires.
    /// </summary>
    private async Task<long> TakeMovementIdsAsync(int count, CancellationToken ct)
    {
        var first = new SqlParameter("@first", SqlDbType.Variant) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC sys.sp_sequence_get_range @sequence_name = N'inventory.MovementIds', @range_size = @n, @range_first_value = @first OUTPUT;",
            [new SqlParameter("@n", SqlDbType.BigInt) { Value = (long)count }, first],
            ct);
        return Convert.ToInt64(first.Value, CultureInfo.InvariantCulture);
    }

    /// <summary>"@p0, @p1, …" for the ids in their given order, with one <c>uniqueidentifier</c> parameter each.</summary>
    private static (string List, SqlParameter[] Parameters) InList(string prefix, IReadOnlyList<Guid> ids)
    {
        var parameters = ids
            .Select((id, i) => new SqlParameter(prefix + i.ToString(CultureInfo.InvariantCulture), SqlDbType.UniqueIdentifier) { Value = id })
            .ToArray();
        return (string.Join(", ", parameters.Select(p => p.ParameterName)), parameters);
    }

    private static T Found<T>(Dictionary<Guid, T> rows, Guid id, string what) =>
        rows.TryGetValue(id, out var row)
            ? row
            : throw new InvalidOperationException($"The {what} {id} wasn't found at step 7; the caller checks existence first.");

    private static BalanceKey[] TouchedKeys(StockMovement m) => TouchedKeys(m.ItemId, m.FromLocationId, m.ToLocationId);

    private static BalanceKey[] TouchedKeys(Guid itemId, Guid fromLocationId, Guid toLocationId) =>
        [new BalanceKey(itemId, fromLocationId), new BalanceKey(itemId, toLocationId)];

    /// <summary>The physical balances in clustered key order: <c>ItemId</c>, then <c>LocationId</c>, as SQL Server sorts them.</summary>
    private static List<StockBalance> PhysicalInKeyOrder(IEnumerable<StockBalance> balances) =>
        balances
            .Where(b => !b.IsVirtual)
            .OrderBy(b => new SqlGuid(b.ItemId))
            .ThenBy(b => new SqlGuid(b.LocationId))
            .ToList();
}