namespace Inventory.Persistence;

internal sealed class StockBalance : ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid ItemId { get; set; }

    public Guid LocationId { get; set; }

    /// <summary>Copied from the location: its kind is not Physical.</summary>
    public bool IsVirtual { get; set; }

    /// <summary>Mirrors <c>Locations.AllowNegativeStock</c> (CLAUDE.md Hidden coupling).</summary>
    public bool AllowNegative { get; set; }

    public decimal OnHand { get; set; }

    public decimal StockValue { get; set; }

    public decimal? LastUnitCost { get; set; }

    public long? LastMovementId { get; set; }

    public byte[] RowVersion { get; set; } = [];
}