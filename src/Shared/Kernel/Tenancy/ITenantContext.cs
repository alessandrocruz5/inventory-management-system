namespace Kernel.Tenancy;

/// <summary>The tenant the current request or scope acts as.</summary>
public interface ITenantContext
{
    /// <summary>True once the request or scope has a tenant.</summary>
    bool HasTenant { get; }

    /// <summary>The current tenant. Throws <see cref="InvalidOperationException"/> when <see cref="HasTenant"/> is false.</summary>
    Guid TenantId { get; }
}