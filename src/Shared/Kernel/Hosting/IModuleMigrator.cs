namespace Kernel.Hosting;

/// <summary>Applies one module's migrations. Implemented once per module, run only by <c>Api migrate</c>.</summary>
public interface IModuleMigrator
{
    /// <summary>Applies the module's pending migrations.</summary>
    Task MigrateAsync(CancellationToken cancellationToken);
}