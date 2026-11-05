using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.UpdateItem;

internal sealed class UpdateItemHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static readonly string[] DuplicateSku = ["An item with this SKU already exists."];

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

        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return Problems.NotFound();
        }

        // Spec §9 allows a SKU change only before the first movement; that check lands with the ledger tables (S5a).
        if (await db.Items.AnyAsync(i => i.Sku == sku && i.Id != id, cancellationToken))
        {
            errors["sku"] = DuplicateSku;
            return Problems.Validation(errors);
        }

        db.Entry(item).Property(i => i.RowVersion).OriginalValue = request.Version!;
        item.Sku = sku;
        item.Name = name;
        item.BaseUnit = baseUnit;
        item.UpdatedAt = clock.GetUtcNow();
        item.UpdatedBy = user.UserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
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

        return TypedResults.Ok(ItemResponse.From(item));
    }
}