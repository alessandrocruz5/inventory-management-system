using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.CreateItem;

internal sealed class CreateItemHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static readonly string[] DuplicateSku = ["An item with this SKU already exists."];

    public async Task<Results<Created<ItemResponse>, ValidationProblem>> HandleAsync(
        CreateItemRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (sku, name, baseUnit) = ItemRules.Normalize(request.Sku, request.Name, request.BaseUnit, errors);
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        if (await db.Items.AnyAsync(i => i.Sku == sku, cancellationToken))
        {
            errors["sku"] = DuplicateSku;
            return Problems.Validation(errors);
        }

        // IsActive is set explicitly: HasDefaultValue(true) makes false EF's sentinel.
        var item = new Item
        {
            Sku = sku,
            Name = name,
            BaseUnit = baseUnit,
            IsActive = true,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user.UserId,
        };
        db.Items.Add(item);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (Problems.IsUniqueViolation(ex))
        {
            errors["sku"] = DuplicateSku;
            return Problems.Validation(errors);
        }

        return TypedResults.Created($"/api/items/{item.Id}", ItemResponse.From(item));
    }
}