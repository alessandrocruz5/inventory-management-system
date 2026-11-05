using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.GetItem;

/// <summary>The item only; S8 adds its balance per physical location (spec audit fix (b)).</summary>
internal sealed class GetItemHandler(InventoryDbContext db)
{
    public async Task<Results<Ok<ItemResponse>, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return Problems.NotFound();
        }

        return TypedResults.Ok(ItemResponse.From(item));
    }
}