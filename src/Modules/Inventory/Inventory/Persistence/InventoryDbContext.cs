using Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Persistence;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenant) : DbContext(options)
{
    /// <summary>The scope's tenant. Interceptors read it each time they run, so no interceptor holds a tenant (spec §8).</summary>
    internal ITenantContext Tenant => tenant;

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Item> Items => Set<Item>();

    /// <summary>Read per query by the "Tenant" filter; throws in a scope with no tenant, so such a query fails loudly.</summary>
    private Guid CurrentTenantId => tenant.TenantId;

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
            ConfigureTenantOwned(location);
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

        modelBuilder.Entity<Item>(item =>
        {
            item.ToTable("Items");
            item.HasKey(i => new { i.TenantId, i.Id });
            ConfigureTenantOwned(item);
            item.Property(i => i.Id).ValueGeneratedOnAdd();
            item.Property(i => i.Sku).HasMaxLength(64).IsRequired();
            item.Property(i => i.Name).HasMaxLength(200).IsRequired();
            item.Property(i => i.BaseUnit).HasMaxLength(16).IsRequired();
            item.Property(i => i.IsActive).HasDefaultValue(true);
            item.Property(i => i.CreatedAt).IsRequired();
            item.Property(i => i.RowVersion).IsRowVersion();
            item.HasIndex(i => new { i.TenantId, i.Sku }).IsUnique().HasDatabaseName("UX_Items_Sku");
        });
    }

    private void ConfigureTenantOwned<T>(EntityTypeBuilder<T> entity) where T : class, ITenantOwned
    {
        entity.HasQueryFilter(TenantSchema.FilterName, e => e.TenantId == CurrentTenantId);
        entity.Property(e => e.TenantId).HasDefaultValueSql(TenantSchema.ColumnDefaultSql).ValueGeneratedNever();
    }
}