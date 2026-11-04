namespace Platform.Tenants;

internal sealed class Tenant
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}