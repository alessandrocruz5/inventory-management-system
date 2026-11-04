using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using Platform.Identity;
using Platform.Tenants;

namespace Platform.Persistence;

internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.ToTable("Tenants", "platform");
            tenant.HasKey(t => t.Id);
            tenant.Property(t => t.Name).HasMaxLength(200).IsRequired();
            tenant.Property(t => t.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<AppUser>(user =>
        {
            user.Property(u => u.TenantId).IsRequired();
            user.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
            user.HasIndex(u => u.TenantId);
        });
    }
}