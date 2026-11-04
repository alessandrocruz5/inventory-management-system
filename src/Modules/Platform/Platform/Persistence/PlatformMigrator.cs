using Kernel.Hosting;

using Microsoft.EntityFrameworkCore;

namespace Platform.Persistence;

/// <summary>The only place Platform's migrations are applied (see the T12 migrate rule).</summary>
internal sealed class PlatformMigrator(PlatformDbContext db) : IModuleMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);
}