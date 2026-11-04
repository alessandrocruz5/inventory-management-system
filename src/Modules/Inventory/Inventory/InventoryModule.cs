using Inventory.Contracts;
using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Inventory;

internal sealed class InventoryModule(InventoryDbContext db, ITenantContext tenant, TimeProvider clock) : IInventoryModule
{
    private static readonly (string Code, string Name, LocationKind Kind)[] VirtualLocations =
    [
        ("SUP", "Supplier", LocationKind.Supplier),
        ("CUS", "Customer", LocationKind.Customer),
        ("ADJ", "Adjustment", LocationKind.Adjustment),
    ];

    public async Task ProvisionTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;

        // S3: the tenant interceptor and named query filter replace the explicit TenantId below.
        var existing = await db.Locations
            .Where(l => l.TenantId == tenantId && l.Kind != LocationKind.Physical)
            .Select(l => l.Code)
            .ToListAsync(cancellationToken);

        foreach (var (code, name, kind) in VirtualLocations.Where(v => !existing.Contains(v.Code)))
        {
            db.Locations.Add(new Location
            {
                TenantId = tenantId,
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