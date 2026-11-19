using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Stocktakes.StartStocktake;

/// <summary>Starts a count of one physical location, prefilled with its stocked items (plan D11–D13).</summary>
internal sealed class StartStocktakeHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock, StocktakeReader reader)
{
    private const int MaxNotesLength = 500;

    public async Task<Results<Created<StocktakeDetailResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        StartStocktakeRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.LocationId is null)
        {
            errors["locationId"] = ["Choose a location."];
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > MaxNotesLength })
        {
            errors["notes"] = [$"Notes must be {MaxNotesLength} characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var locationId = request.LocationId!.Value;

        // Another tenant's id is invisible here, so it is a 400 like any unknown id (spec §11, T5).
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == locationId, cancellationToken);
        if (location is not { Kind: LocationKind.Physical })
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["locationId"] = ["Choose an existing physical location."] });
        }

        if (!location.IsActive)
        {
            return Problems.InactiveLocation(locationId);
        }

        var (id, created) = await db.Database.CreateExecutionStrategy()
            .ExecuteAsync(ct => InsertAsync(locationId, notes, ct), cancellationToken);
        if (!created)
        {
            return id is { } openId ? Problems.StocktakeOpenExists(openId) : Problems.ConcurrencyConflict();
        }

        // Read after commit, outside the strategy: a retry of a committed insert would report its own stocktake as the open one.
        var detail = await reader.ReadAsync(id!.Value, cancellationToken);
        return TypedResults.Created($"/api/stocktakes/{id}", detail);
    }

    /// <summary>Inserts the stocktake and its prefill; on <c>UX_Stocktakes_Open</c>, returns the open stocktake's id instead.</summary>
    private async Task<(Guid? Id, bool Created)> InsertAsync(Guid locationId, string? notes, CancellationToken cancellationToken)
    {
        // A retry starts clean: rows tracked by the failed attempt would be inserted again.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var stocktake = new Stocktake
        {
            LocationId = locationId,
            Status = StocktakeStatus.Draft,
            Notes = notes,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user.UserId,
        };
        db.Stocktakes.Add(stocktake);

        // D13: every active item with a physical balance here that isn't zero, uncounted.
        var itemIds = await (
            from b in db.StockBalances
            where b.LocationId == locationId && !b.IsVirtual && b.OnHand != 0
            join i in db.Items on b.ItemId equals i.Id
            where i.IsActive
            select b.ItemId).ToListAsync(cancellationToken);
        db.StocktakeLines.AddRange(itemIds.Select(itemId => new StocktakeLine { StocktakeId = stocktake.Id, ItemId = itemId }));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (Problems.IsUniqueViolation(ex))
        {
            // D12: a racing or earlier start holds the location's open slot. The transaction rolls back on dispose.
            db.ChangeTracker.Clear();
            var open = await db.Stocktakes.AsNoTracking()
                .Where(s => s.LocationId == locationId && (s.Status == StocktakeStatus.Draft || s.Status == StocktakeStatus.Posting))
                .Select(s => (Guid?)s.Id)
                .SingleOrDefaultAsync(cancellationToken);
            return (open, false);
        }

        await transaction.CommitAsync(cancellationToken);
        return (stocktake.Id, true);
    }
}