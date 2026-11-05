namespace Inventory.Persistence;

internal sealed class Item : ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public required string Sku { get; set; }

    public required string Name { get; set; }

    public required string BaseUnit { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null when the item was seeded.</summary>
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];
}