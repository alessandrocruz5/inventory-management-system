using Kernel.Hosting;

namespace Api.Setup;

/// <summary><c>Api seed</c>: runs every <see cref="IDataSeeder"/> as <c>inventory_app</c>, then exits.</summary>
internal static class SeedCommand
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }
}