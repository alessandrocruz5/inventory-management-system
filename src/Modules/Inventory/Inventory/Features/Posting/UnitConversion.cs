using Inventory.Domain;
using Inventory.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Posting;

/// <summary>Plan D4/D5: a line's unit resolves to a factor of the item's base unit; quantities convert exactly or not at all.</summary>
internal static class UnitConversion
{
    /// <summary>
    /// <paramref name="quantity"/> × <paramref name="factor"/> when it is exact to 4 dp and within
    /// <see cref="PostingValidation.MaxAmount"/> in magnitude; otherwise null. Never rounds (D4). The sign is kept, so an
    /// adjustment's signed delta converts as-is.
    /// </summary>
    public static decimal? ToBase(decimal quantity, decimal factor)
    {
        // A validated quantity and a unit's factor are each at most MaxAmount with 4 dp (13 significant digits), so the
        // product's at most 26 significant digits fit decimal exactly: the comparison below sees the true product.
        var baseQuantity = quantity * factor;
        return CostingPolicy.Round4(baseQuantity) == baseQuantity && Math.Abs(baseQuantity) <= PostingValidation.MaxAmount
            ? baseQuantity
            : null;
    }

    /// <summary>The active alternate units of these items, keyed (ItemId, Code). Codes are stored lowercase.</summary>
    public static async Task<Dictionary<(Guid ItemId, string Code), decimal>> ActiveFactorsAsync(
        InventoryDbContext db, IReadOnlyCollection<Guid> itemIds, CancellationToken ct) =>
        await db.ItemUnits
            .AsNoTracking()
            .Where(u => itemIds.Contains(u.ItemId) && u.IsActive)
            .ToDictionaryAsync(u => (u.ItemId, u.Code), u => u.Factor, ct);
}