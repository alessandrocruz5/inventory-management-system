using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; <c>migrations add</c> never opens this connection.</summary>
internal sealed class PlatformDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PlatformDbContext>()
        .UseSqlServer(
            "Server=localhost;Database=inventory;Trusted_Connection=False;TrustServerCertificate=True",
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "platform"))
        .Options);
}