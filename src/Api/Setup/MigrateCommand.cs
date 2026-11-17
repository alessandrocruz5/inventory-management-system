using Kernel.Hosting;

namespace Api.Setup;

/// <summary>
/// <c>Api migrate</c>: applies every module's migrations as the server admin, then ensures the <c>inventory_app</c>
/// principal. The serving API never migrates (spec §4).
/// </summary>
internal static class MigrateCommand
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration config, CancellationToken cancellationToken)
    {
        var password = config["InventoryApp:Password"];
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("InventoryApp:Password is required");
        }

        var adminConnectionString = config.GetConnectionString("inventory")
            ?? throw new InvalidOperationException("ConnectionStrings:inventory is required");

        await using (var scope = services.CreateAsyncScope())
        {
            // Registration order: Platform first, because its first migration turns RCSI on.
            foreach (var migrator in scope.ServiceProvider.GetServices<IModuleMigrator>())
            {
                await migrator.MigrateAsync(cancellationToken);
            }
        }

        await DatabasePrincipals.EnsureAsync(adminConnectionString, password, cancellationToken);
    }
}