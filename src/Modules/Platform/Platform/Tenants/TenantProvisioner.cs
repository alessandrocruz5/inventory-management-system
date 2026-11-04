using Inventory.Contracts;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;
using Platform.Persistence;

namespace Platform.Tenants;

/// <summary>Creates tenants, their inventory and their users. Every method is idempotent.</summary>
internal sealed class TenantProvisioner(
    PlatformDbContext db,
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    ITenantScopeFactory scopes,
    TimeProvider clock)
{
    public async Task CreateAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == id, cancellationToken))
        {
            db.Tenants.Add(new Tenant { Id = id, Name = name, CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
        }

        // A new scope, so the inventory work runs on a new connection acting as this tenant (spec §8).
        await using var scope = scopes.BeginScope(id);
        await scope.Services.GetRequiredService<IInventoryModule>().ProvisionTenantAsync(cancellationToken);
    }

    public async Task<AppUser> EnsureUserAsync(
        Guid tenantId, string email, string role, string password, CancellationToken cancellationToken)
    {
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = tenantId };
        Check(await users.CreateAsync(user, password), $"create user {email}");
        Check(await users.AddToRoleAsync(user, role), $"add {email} to {role}");
        return user;
    }

    public async Task EnsureRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var name in new[] { Roles.Admin, Roles.Staff })
        {
            if (!await roles.RoleExistsAsync(name))
            {
                Check(await roles.CreateAsync(new IdentityRole<Guid>(name)), $"create role {name}");
            }
        }
    }

    // Only the error codes: a description can echo the password.
    private static void Check(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not {action}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }
    }
}