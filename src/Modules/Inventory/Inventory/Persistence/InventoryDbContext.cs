using Microsoft.EntityFrameworkCore;

namespace Inventory.Persistence;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");

        modelBuilder.Entity<Location>(location =>
        {
            location.ToTable("Locations", t =>
            {
                t.HasCheckConstraint("CK_Locations_Kind", "[Kind] IN ('Physical', 'Supplier', 'Customer', 'Adjustment')");
                t.HasCheckConstraint("CK_Locations_NegativeOnlyPhysical", "[Kind] = 'Physical' OR [AllowNegativeStock] = 0");
            });
            location.HasKey(l => new { l.TenantId, l.Id });
            location.Property(l => l.Id).ValueGeneratedOnAdd();
            location.Property(l => l.Code).HasMaxLength(32).IsRequired();
            location.Property(l => l.Name).HasMaxLength(200).IsRequired();
            location.Property(l => l.Kind).HasConversion<string>().HasMaxLength(16).IsUnicode(false).IsRequired();
            location.Property(l => l.AllowNegativeStock).HasDefaultValue(false);
            location.Property(l => l.IsActive).HasDefaultValue(true);
            location.Property(l => l.CreatedAt).IsRequired();
            location.Property(l => l.RowVersion).IsRowVersion();
            location.HasIndex(l => new { l.TenantId, l.Code }).IsUnique().HasDatabaseName("UX_Locations_Code");
            location.HasIndex(l => new { l.TenantId, l.Kind }).IsUnique().HasFilter("[Kind] <> 'Physical'").HasDatabaseName("UX_Locations_VirtualKind");
        });
    }
}