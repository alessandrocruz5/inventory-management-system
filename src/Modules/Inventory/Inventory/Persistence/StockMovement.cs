namespace Inventory.Persistence;

internal sealed class StockMovement : ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>Taken from <c>inventory.MovementIds</c> before the insert.</summary>
    public long Id { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>Request line n; its revaluation, if any, is 1000 + n.</summary>
    public int LineNo { get; set; }

    public Guid ItemId { get; set; }

    public Guid FromLocationId { get; set; }

    public Guid ToLocationId { get; set; }

    /// <summary>0 only on a revaluation movement (spec §7).</summary>
    public decimal Quantity { get; set; }

    public decimal TotalCost { get; set; }

    public long? ReversesMovementId { get; set; }

    public DateTimeOffset PostedAt { get; set; }

    public Guid PostedBy { get; set; }
}