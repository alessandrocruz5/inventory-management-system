using Kernel.Hosting;

using Microsoft.Extensions.Configuration;

using Platform.Identity;
using Platform.Tenants;

namespace Platform.Seeding;

/// <summary>Two tenants with one Admin and one Staff each. Inventory's own seeder adds branches and items.</summary>
internal sealed class PlatformSeeder(TenantProvisioner provisioner, IConfiguration config) : IDataSeeder
{
    public static readonly Guid DemoTenantId = SeedTenants.Demo;

    public static readonly Guid IsolationTenantId = SeedTenants.Isolation;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var password = config["Seed:Password"];
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Seed:Password is required");
        }

        await provisioner.EnsureRolesAsync(cancellationToken);

        await provisioner.CreateAsync(DemoTenantId, "Demo Retail", cancellationToken);
        await provisioner.CreateAsync(IsolationTenantId, "Isolation Tenant", cancellationToken);

        await provisioner.EnsureUserAsync(DemoTenantId, "admin@demo.test", Roles.Admin, password, cancellationToken);
        await provisioner.EnsureUserAsync(DemoTenantId, "staff@demo.test", Roles.Staff, password, cancellationToken);
        await provisioner.EnsureUserAsync(IsolationTenantId, "admin@isolation.test", Roles.Admin, password, cancellationToken);
        await provisioner.EnsureUserAsync(IsolationTenantId, "staff@isolation.test", Roles.Staff, password, cancellationToken);
    }
}