using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.UpdateItem;

internal sealed class UpdateItemHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static readonly string[] DuplicateSku = ["An item with this SKU already exists."];
    private static readonly string[] SkuInUse = ["The SKU can't change once the item has stock movements."];
    private static readonly string[] BaseUnitInUse = ["The base unit can't change once the item has stock history or other units."];

    public async Task<Results<Ok<ItemResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        UpdateItemRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (sku, name, baseUnit) = ItemRules.Normalize(request.Sku, request.Name, request.BaseUnit, errors);
        if (request.Version is not { Length: > 0 })
        {
            errors["version"] = ["Version is required."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var version = request.Version!;

        // The SKU check below locks until commit, so this runs in a transaction inside the retrying strategy (TASKS.md, IMS-3).
        var attempt = 0;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            ct => UpdateAsync(id, sku, name, baseUnit, version, ++attempt > 1, ct), cancellationToken);
    }

    private async Task<Results<Ok<ItemResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        string sku,
        string name,
        string baseUnit,
        byte[] version,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        // A retry starts clean: a row tracked by the failed attempt would hide the fresh read.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return Problems.NotFound();
        }

        if (await db.Items.AnyAsync(i => i.Sku == sku && i.Id != id, cancellationToken))
        {
            errors["sku"] = DuplicateSku;
            return Problems.Validation(errors);
        }

        // Spec §5: the SKU is immutable once the item has a movement. HOLDLOCK range-locks the item's slice of
        // IX_Movements_Item until commit, so a first posting can't land between this check and the update.
        // FORCESEEK keeps that range to this item. No UPDLOCK: a shared range lock already blocks inserts, and
        // U locks would needlessly serialize concurrent SKU checks.
        // Plan D7: the base unit is immutable once the item has a movement or an alternate unit, which would otherwise
        // be reinterpreted. Same range lock as the SKU check; on ItemUnits, UPDLOCK + HOLDLOCK over the item's PK slice
        // blocks a concurrent unit insert until commit. CreateItemUnit U-locks the item row first, so the two can
        // deadlock (1205); the execution strategy retries the loser, as for a SKU edit racing a first posting
        // (TASKS.md IMS-6).
        var skuChanged = !string.Equals(item.Sku, sku, StringComparison.Ordinal);
        var baseUnitChanged = !string.Equals(item.BaseUnit, baseUnit, StringComparison.Ordinal);
        if (skuChanged || baseUnitChanged)
        {
            var hasMovement = await db.StockMovements
                .FromSql($"SELECT * FROM [inventory].[StockMovements] WITH (HOLDLOCK, FORCESEEK, INDEX([IX_Movements_Item])) WHERE [ItemId] = {id}")
                .AnyAsync(cancellationToken);
            if (skuChanged && hasMovement)
            {
                errors["sku"] = SkuInUse;
            }

            if (baseUnitChanged
                && (hasMovement
                    || await db.ItemUnits
                        .FromSql($"SELECT * FROM [inventory].[ItemUnits] WITH (UPDLOCK, HOLDLOCK, FORCESEEK) WHERE [ItemId] = {id}")
                        .AnyAsync(cancellationToken)))
            {
                errors["baseUnit"] = BaseUnitInUse;
            }

            if (errors.Count > 0)
            {
                return Problems.Validation(errors);
            }
        }

        db.Entry(item).Property(i => i.RowVersion).OriginalValue = version;
        item.Sku = sku;
        item.Name = name;
        item.BaseUnit = baseUnit;
        item.UpdatedAt = clock.GetUtcNow();
        item.UpdatedBy = user.UserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) when (isRetry)
        {
            // A commit can fail in transit after the server applied it; the strategy then reruns with the client's old
            // version, which no longer matches. If the row already holds this edit, that earlier attempt saved it.
            db.ChangeTracker.Clear();
            var saved = await db.Items.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (saved is null)
            {
                return Problems.NotFound();
            }

            if (!string.Equals(saved.Sku, sku, StringComparison.Ordinal)
                || !string.Equals(saved.Name, name, StringComparison.Ordinal)
                || !string.Equals(saved.BaseUnit, baseUnit, StringComparison.Ordinal))
            {
                return Problems.ConcurrencyConflict();
            }

            await transaction.CommitAsync(cancellationToken);
            return TypedResults.Ok(ItemResponse.From(saved));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.ConcurrencyConflict();
        }
        catch (DbUpdateException ex) when (Problems.IsUniqueViolation(ex))
        {
            errors["sku"] = DuplicateSku;
            return Problems.Validation(errors);
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(ItemResponse.From(item));
    }
}