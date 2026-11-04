using Kernel.Hosting;

using Microsoft.EntityFrameworkCore;

namespace Inventory.Persistence;

/// <summary>The only place Inventory's migrations are applied (see the T12 migrate rule).</summary>
internal sealed class InventoryMigrator(InventoryDbContext db) : IModuleMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);
}