using Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Inventory.Persistence;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenant) : DbContext(options)
{
    /// <summary>The scope's tenant. Interceptors read it each time they run, so no interceptor holds a tenant (spec §8).</summary>
    internal ITenantContext Tenant => tenant;

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    /// <summary>Read per query by the "Tenant" filter; throws in a scope with no tenant, so such a query fails loudly.</summary>
    private Guid CurrentTenantId => tenant.TenantId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Quantities and money are decimal(19,4) everywhere (CLAUDE.md conventions).
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);

        // The spec §5 sketch is the full index list; EF's per-FK indexes would add three unread indexes to StockMovements.
        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();
    }

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

        modelBuilder.HasSequence<long>("MovementIds").StartsAt(1).IncrementsBy(1);

        modelBuilder.Entity<StockDocument>(document =>
        {
            document.ToTable("StockDocuments", t =>
            {
                // Declared so SaveChanges skips the OUTPUT clause; the trigger SQL is in the Ledger migration (CLAUDE.md Hidden coupling).
                t.HasTrigger("TR_StockDocuments_AppendOnly");
                t.HasCheckConstraint("CK_StockDocuments_Type", "[Type] IN ('Receipt', 'Issue', 'Transfer', 'Adjustment', 'Reversal')");
                t.HasCheckConstraint("CK_StockDocuments_Reversal",
                    "([Type] = 'Reversal' AND [ReversesDocumentId] IS NOT NULL) OR ([Type] <> 'Reversal' AND [ReversesDocumentId] IS NULL)");
                // IS NOT NULL is needed: a CHECK passes when its expression is UNKNOWN.
                t.HasCheckConstraint("CK_StockDocuments_ReasonCode",
                    "([Type] = 'Adjustment' AND [ReasonCode] IS NOT NULL AND [ReasonCode] IN ('Damaged', 'Lost', 'Found', 'CountCorrection', 'Other')) " +
                    "OR ([Type] <> 'Adjustment' AND [ReasonCode] IS NULL)");
            });
            document.HasKey(d => new { d.TenantId, d.Id });
            ConfigureTenantOwned(document);
            document.Property(d => d.Id).ValueGeneratedOnAdd();
            document.Property(d => d.Type).HasConversion<string>().HasMaxLength(16).IsUnicode(false).IsRequired();
            document.Property(d => d.ReasonCode).HasConversion<string>().HasMaxLength(24).IsUnicode(false);
            document.Property(d => d.Reference).HasMaxLength(100);
            document.Property(d => d.Notes).HasMaxLength(500);
            document.Property(d => d.IdempotencyKey).HasMaxLength(64).IsUnicode(false).IsRequired();
            document.Property(d => d.RequestHash).HasMaxLength(32).IsFixedLength().IsRequired();
            document.HasIndex(d => new { d.TenantId, d.IdempotencyKey }).IsUnique().HasDatabaseName("UX_StockDocuments_IdempotencyKey");
            document.HasIndex(d => new { d.TenantId, d.ReversesDocumentId }).IsUnique()
                .HasFilter("[ReversesDocumentId] IS NOT NULL").HasDatabaseName("UX_StockDocuments_Reverses");
            document.HasOne<StockDocument>().WithMany().HasForeignKey(d => new { d.TenantId, d.ReversesDocumentId }).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<StockMovement>(movement =>
        {
            movement.ToTable("StockMovements", t =>
            {
                t.HasTrigger("TR_StockMovements_AppendOnly");
                t.HasCheckConstraint("CK_StockMovements_Locations", "[FromLocationId] <> [ToLocationId]");
                t.HasCheckConstraint("CK_StockMovements_Quantity", "[Quantity] > 0 OR ([Quantity] = 0 AND [TotalCost] > 0)");
                t.HasCheckConstraint("CK_StockMovements_TotalCost", "[TotalCost] >= 0");
            });
            movement.HasKey(m => new { m.TenantId, m.Id });
            ConfigureTenantOwned(movement);
            movement.Property(m => m.Id).ValueGeneratedNever();
            movement.HasIndex(m => new { m.TenantId, m.DocumentId, m.LineNo }).IsUnique().HasDatabaseName("UX_StockMovements_DocumentLine");
            movement.HasIndex(m => new { m.TenantId, m.ItemId, m.Id }).HasDatabaseName("IX_Movements_Item");
            movement.HasOne<StockDocument>().WithMany().HasForeignKey(m => new { m.TenantId, m.DocumentId }).OnDelete(DeleteBehavior.NoAction);
            movement.HasOne<Item>().WithMany().HasForeignKey(m => new { m.TenantId, m.ItemId }).OnDelete(DeleteBehavior.NoAction);
            movement.HasOne<Location>().WithMany().HasForeignKey(m => new { m.TenantId, m.FromLocationId }).OnDelete(DeleteBehavior.NoAction);
            movement.HasOne<Location>().WithMany().HasForeignKey(m => new { m.TenantId, m.ToLocationId }).OnDelete(DeleteBehavior.NoAction);
            movement.HasOne<StockMovement>().WithMany().HasForeignKey(m => new { m.TenantId, m.ReversesMovementId }).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<StockBalance>(balance =>
        {
            balance.ToTable("StockBalances", t =>
            {
                t.HasCheckConstraint("CK_StockBalances_Physical",
                    "[IsVirtual] = 1 OR ([OnHand] >= 0 AND [StockValue] >= 0 AND ([OnHand] <> 0 OR [StockValue] = 0)) OR ([OnHand] < 0 AND [AllowNegative] = 1)");
                t.HasCheckConstraint("CK_StockBalances_VirtualValue", "[IsVirtual] = 0 OR [StockValue] = 0");
            });
            balance.HasKey(b => new { b.TenantId, b.ItemId, b.LocationId });
            ConfigureTenantOwned(balance);
            balance.Property(b => b.OnHand).HasDefaultValue(0m);
            balance.Property(b => b.StockValue).HasDefaultValue(0m);
            balance.Property(b => b.RowVersion).IsRowVersion();
            balance.HasIndex(b => new { b.TenantId, b.LocationId }).HasDatabaseName("IX_Balances_Location");
            balance.HasOne<Item>().WithMany().HasForeignKey(b => new { b.TenantId, b.ItemId }).OnDelete(DeleteBehavior.NoAction);
            balance.HasOne<Location>().WithMany().HasForeignKey(b => new { b.TenantId, b.LocationId }).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private void ConfigureTenantOwned<T>(EntityTypeBuilder<T> entity) where T : class, ITenantOwned
    {
        entity.HasQueryFilter(TenantSchema.FilterName, e => e.TenantId == CurrentTenantId);
        entity.Property(e => e.TenantId).HasDefaultValueSql(TenantSchema.ColumnDefaultSql).ValueGeneratedNever();
    }
}