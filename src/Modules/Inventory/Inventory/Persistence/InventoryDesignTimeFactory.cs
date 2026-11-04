using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventory.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; <c>migrations add</c> never opens this connection.</summary>
internal sealed class InventoryDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<InventoryDbContext>()
        .UseSqlServer(
            "Server=localhost;Database=inventory;Trusted_Connection=False;TrustServerCertificate=True",
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
        .Options);
}