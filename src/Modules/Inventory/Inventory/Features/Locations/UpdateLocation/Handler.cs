using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Locations.UpdateLocation;

internal sealed class UpdateLocationHandler(InventoryDbContext db, ICurrentUser user, ITenantContext tenant, TimeProvider clock)
{
    private static readonly string[] DuplicateCode = ["A location with this code already exists."];

    public async Task<Results<Ok<LocationResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        UpdateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (code, name) = LocationRules.Normalize(request.Code, request.Name, errors);
        if (request.Version is not { Length: > 0 })
        {
            errors["version"] = ["Version is required."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            token => UpdateAsync(id, request, code, name, token), cancellationToken);
    }

    // A toggle change locks balances until commit, so this runs in a transaction inside the retrying strategy (TASKS.md, IMS-3).
    private async Task<Results<Ok<LocationResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateLocationRequest request,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        // A retry starts clean: a row tracked by the failed attempt would hide the fresh read.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var location = await db.Locations.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return Problems.NotFound();
        }

        if (location.Kind != LocationKind.Physical)
        {
            errors["location"] = ["System locations can't be changed."];
            return Problems.Validation(errors);
        }

        if (await db.Locations.AnyAsync(l => l.Code == code && l.Id != id, cancellationToken))
        {
            errors["code"] = DuplicateCode;
            return Problems.Validation(errors);
        }

        if (request.AllowNegativeStock is { } allow && allow != location.AllowNegativeStock)
        {
            // Spec §6: lock the location's balances through IX_Balances_Location, then check them, then update the location.
            // HOLDLOCK also locks the empty key range, so no posting adds a balance here before we commit. A posting that
            // waits on these locks reads the new flag at step 7. The INDEX and FORCESEEK hints keep the range to this location.
            var balances = await db.StockBalances
                .FromSql($"SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, HOLDLOCK, FORCESEEK, INDEX([IX_Balances_Location])) WHERE [TenantId] = {tenant.TenantId} AND [LocationId] = {id}")
                .AsTracking()
                .ToListAsync(cancellationToken);
            if (!allow)
            {
                var negative = balances.Where(b => b.OnHand < 0).OrderBy(b => b.ItemId).ToList();
                if (negative.Count > 0)
                {
                    return Problems.NegativeBalancesExist(negative);
                }
            }

            // Per-row updates by primary key from tracked rows: a set-based UPDATE by LocationId could scan and U-lock
            // other locations' rows.
            foreach (var balance in balances)
            {
                balance.AllowNegative = allow;
            }

            location.AllowNegativeStock = allow;
        }

        db.Entry(location).Property(l => l.RowVersion).OriginalValue = request.Version!;
        location.Code = code;
        location.Name = name;
        location.UpdatedAt = clock.GetUtcNow();
        location.UpdatedBy = user.UserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // No commit: a stale version rolls back the balance updates too.
            return Problems.ConcurrencyConflict();
        }
        catch (DbUpdateException ex) when (Problems.IsUniqueViolation(ex))
        {
            errors["code"] = DuplicateCode;
            return Problems.Validation(errors);
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(LocationResponse.From(location));
    }
}