namespace Kernel.Hosting;

/// <summary>Seeds demo data. Run only by <c>Api seed</c>; must be idempotent.</summary>
public interface IDataSeeder
{
    /// <summary>Inserts whatever seed data is missing.</summary>
    Task SeedAsync(CancellationToken cancellationToken);
}