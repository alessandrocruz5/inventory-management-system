using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inventory.Persistence;

/// <summary>
/// Stamps the scope's tenant on every added tenant-owned row, so handlers never set <c>TenantId</c> (spec §8).
/// Stateless: it reads the tenant from the intercepted context, and throws before any SQL runs on a missing or foreign tenant.
/// </summary>
internal sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly TenantSaveChangesInterceptor Instance = new();

    private TenantSaveChangesInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is not InventoryDbContext db)
        {
            return;
        }

        // Modified rows need no check: TenantId is part of every tenant key, and EF refuses to change a key.
        foreach (var entry in db.ChangeTracker.Entries<ITenantOwned>().Where(e => e.State == EntityState.Added))
        {
            if (!db.Tenant.HasTenant)
            {
                throw new InvalidOperationException("Tenant-owned rows can only be added inside a tenant scope.");
            }

            var tenantId = db.Tenant.TenantId;
            var property = entry.Property(e => e.TenantId);
            if (property.CurrentValue == Guid.Empty)
            {
                property.CurrentValue = tenantId;
            }
            else if (property.CurrentValue != tenantId)
            {
                throw new InvalidOperationException("A tenant-owned row was added for another tenant.");
            }
        }
    }
}