using Inventory.Contracts;
using Inventory.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Inventory;

internal sealed class InventoryModule(InventoryDbContext db, TimeProvider clock) : IInventoryModule
{
    private static readonly (string Code, string Name, LocationKind Kind)[] VirtualLocations =
    [
        ("SUP", "Supplier", LocationKind.Supplier),
        ("CUS", "Customer", LocationKind.Customer),
        ("ADJ", "Adjustment", LocationKind.Adjustment),
    ];

    public async Task ProvisionTenantAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Locations
            .Where(l => l.Kind != LocationKind.Physical)
            .Select(l => l.Code)
            .ToListAsync(cancellationToken);

        foreach (var (code, name, kind) in VirtualLocations.Where(v => !existing.Contains(v.Code)))
        {
            db.Locations.Add(new Location
            {
                Code = code,
                Name = name,
                Kind = kind,
                AllowNegativeStock = false,
                IsActive = true,
                CreatedAt = clock.GetUtcNow(),
                CreatedBy = null,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}