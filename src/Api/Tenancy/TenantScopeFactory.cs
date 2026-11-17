using Kernel.Tenancy;

namespace Api.Tenancy;

/// <summary>Opens a DI scope, and so a new DbContext and connection, acting as one tenant. Used outside requests: seed, tests.</summary>
internal sealed class TenantScopeFactory(IServiceScopeFactory scopeFactory) : ITenantScopeFactory
{
    public ITenantScope BeginScope(Guid tenantId)
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId);
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        return new TenantScope(scope);
    }

    private sealed class TenantScope(AsyncServiceScope scope) : ITenantScope
    {
        public IServiceProvider Services => scope.ServiceProvider;

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}