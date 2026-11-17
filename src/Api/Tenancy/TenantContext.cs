using Kernel.Tenancy;

namespace Api.Tenancy;

/// <summary>The scope's tenant, set once by <see cref="TenantMiddleware"/> or <see cref="TenantScopeFactory"/>. A scope never switches tenant (spec §8).</summary>
internal sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public bool HasTenant => _tenantId.HasValue;

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException("This scope has no tenant.");

    internal void Set(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new InvalidOperationException("The tenant id must not be empty.");
        }

        if (_tenantId.HasValue)
        {
            throw new InvalidOperationException("This scope already has a tenant.");
        }

        _tenantId = tenantId;
    }
}