namespace Inventory.Persistence;

/// <summary>An alternate unit of an item: one <c>Code</c> is <c>Factor</c> base units (plan D3). Immutable except for deactivation.</summary>
internal sealed class ItemUnit : ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid ItemId { get; set; }

    public required string Code { get; set; }

    public decimal Factor { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null when the unit was seeded.</summary>
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}