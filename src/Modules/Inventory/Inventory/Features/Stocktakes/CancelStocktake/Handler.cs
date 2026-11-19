using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Stocktakes.CancelStocktake;

/// <summary>Cancels a Draft stocktake, which frees its location's open slot. A Posting one is resolved by posting again (plan D10).</summary>
internal sealed class CancelStocktakeHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock, StocktakeReader reader)
{
    public async Task<Results<Ok<StocktakeDetailResponse>, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var failure = await db.Database.CreateExecutionStrategy().ExecuteAsync(ct => CancelAsync(id, ct), cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        return await reader.ReadAsync(id, cancellationToken) is { } detail ? TypedResults.Ok(detail) : Problems.NotFound();
    }

    private async Task<ProblemHttpResult?> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        // A retry starts clean: a row tracked by the failed attempt would hide the fresh read.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // D15: the same U lock as counting and posting, so a cancel can't slip between posting's steps.
        var stocktake = await db.Stocktakes
            .FromSql($"SELECT * FROM [inventory].[Stocktakes] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(cancellationToken);
        if (stocktake is null)
        {
            return Problems.NotFound();
        }

        // Already cancelled: nothing to change, so the call is idempotent.
        if (stocktake.Status == StocktakeStatus.Cancelled)
        {
            return null;
        }

        if (stocktake.Status != StocktakeStatus.Draft)
        {
            return Problems.StocktakeNotOpen(stocktake.Status);
        }

        stocktake.Status = StocktakeStatus.Cancelled;
        stocktake.UpdatedAt = clock.GetUtcNow();
        stocktake.UpdatedBy = user.UserId;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }
}