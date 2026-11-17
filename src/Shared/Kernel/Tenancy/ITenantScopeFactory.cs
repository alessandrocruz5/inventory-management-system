namespace Kernel.Tenancy;

/// <summary>Opens tenant-bound DI scopes outside requests.</summary>
public interface ITenantScopeFactory
{
    /// <summary>Opens a new DI scope (and so a new DB connection) acting as the tenant. Used outside requests: seed, tests.</summary>
    ITenantScope BeginScope(Guid tenantId);
}

/// <summary>A DI scope acting as one tenant.</summary>
public interface ITenantScope : IAsyncDisposable
{
    /// <summary>The scope's service provider.</summary>
    IServiceProvider Services { get; }
}