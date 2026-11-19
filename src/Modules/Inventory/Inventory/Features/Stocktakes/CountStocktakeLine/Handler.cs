using System.Globalization;

using Inventory.Domain;
using Inventory.Features.Posting;
using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Stocktakes.CountStocktakeLine;

/// <summary>
/// Records one count with the balance snapshot it was measured against, adding the line when the item isn't on the sheet
/// (plan D8, D15). Counts on one stocktake serialize on its U-locked row, which is also what posting takes first.
/// </summary>
internal sealed class CountStocktakeLineHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock, StocktakeReader reader)
{
    private const int MaxUnitLength = 16;

    private static readonly string MaxAmountText = PostingValidation.MaxAmount.ToString("N4", CultureInfo.InvariantCulture);

    public async Task<Results<Ok<StocktakeDetailResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        Guid itemId,
        CountStocktakeLineRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var quantity = Amount(request.Quantity, "quantity", "Quantity", errors);
        var unitCost = request.UnitCost is null ? null : Amount(request.UnitCost, "unitCost", "Unit cost", errors);
        var unit = request.Unit?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(unit))
        {
            unit = null;
        }
        else if (unit.Length > MaxUnitLength)
        {
            errors["unit"] = [$"Unit must be at most {MaxUnitLength} characters."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        // Counting changes only the line, so the outcome is a failure or null; the detail is read after commit.
        var failure = await db.Database.CreateExecutionStrategy()
            .ExecuteAsync(ct => CountAsync(id, itemId, quantity!.Value, unit, unitCost, ct), cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        return await reader.ReadAsync(id, cancellationToken) is { } detail ? TypedResults.Ok(detail) : Problems.NotFound();
    }

    private async Task<Results<Ok<StocktakeDetailResponse>, ValidationProblem, ProblemHttpResult>?> CountAsync(
        Guid id,
        Guid itemId,
        decimal quantity,
        string? unit,
        decimal? unitCost,
        CancellationToken cancellationToken)
    {
        // A retry starts clean: rows tracked by the failed attempt would hide the fresh reads.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // D15: the U lock serializes this against other counts and against posting's Draft -> Posting step.
        var stocktake = await db.Stocktakes
            .FromSql($"SELECT * FROM [inventory].[Stocktakes] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(cancellationToken);
        if (stocktake is null)
        {
            return Problems.NotFound();
        }

        if (stocktake.Status != StocktakeStatus.Draft)
        {
            return Problems.StocktakeNotOpen(stocktake.Status);
        }

        // Another tenant's item id is invisible here, so it is a 404 like any unknown route id (spec §11, T5).
        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return Problems.NotFound();
        }

        if (!item.IsActive)
        {
            return Problems.InactiveItem(itemId);
        }

        var factor = 1m;
        if (unit is not null && unit != item.BaseUnit)
        {
            var factors = await UnitConversion.ActiveFactorsAsync(db, [itemId], cancellationToken);
            if (!factors.TryGetValue((itemId, unit), out factor))
            {
                return Problems.Validation(new Dictionary<string, string[]> { ["unit"] = ["Choose one of this item's units."] });
            }
        }

        if (UnitConversion.ToBase(quantity, factor) is not { } counted)
        {
            return Problems.Validation(new Dictionary<string, string[]>
            {
                ["quantity"] = Math.Abs(quantity * factor) > PostingValidation.MaxAmount
                    ? [$"This is more than {MaxAmountText} {item.BaseUnit}."]
                    : [$"This doesn't convert to whole 0.0001 {item.BaseUnit}; enter it in {item.BaseUnit}."],
            });
        }

        // D8: the snapshot is a plain read of the balance as it is now (no lock hint; plan Assumptions).
        var balance = await db.StockBalances.AsNoTracking()
            .Where(b => b.ItemId == itemId && b.LocationId == stocktake.LocationId)
            .Select(b => new { b.OnHand, b.LastMovementId })
            .SingleOrDefaultAsync(cancellationToken);

        var line = await db.StocktakeLines.SingleOrDefaultAsync(l => l.StocktakeId == id && l.ItemId == itemId, cancellationToken);
        if (line is null)
        {
            line = new StocktakeLine { StocktakeId = id, ItemId = itemId };
            db.StocktakeLines.Add(line);
        }

        var now = clock.GetUtcNow();
        line.CountedQuantity = counted;
        line.ExpectedQuantity = balance?.OnHand ?? 0m;
        line.ExpectedLastMovementId = balance?.LastMovementId;
        line.UnitCost = unitCost;
        line.CountedAt = now;
        line.CountedBy = user.UserId;
        stocktake.UpdatedAt = now;
        stocktake.UpdatedBy = user.UserId;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    /// <summary>Returns the value when it is present, non-negative, in range and has at most 4 dp; otherwise adds an error.</summary>
    private static decimal? Amount(decimal? value, string key, string label, Dictionary<string, string[]> errors)
    {
        string? message = value switch
        {
            null => $"{label} is required.",
            < 0m => $"{label} must not be negative.",
            > PostingValidation.MaxAmount => $"{label} must be at most {MaxAmountText}.",
            { } v when CostingPolicy.Round4(v) != v => $"{label} can have at most 4 decimal places.",
            _ => null,
        };

        if (message is not null)
        {
            errors[key] = [message];
            return null;
        }

        return value;
    }
}