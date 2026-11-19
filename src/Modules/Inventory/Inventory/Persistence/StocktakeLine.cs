namespace Inventory.Persistence;

/// <summary>
/// One item of a stocktake (plan D8, D13), in base units. Uncounted until a count stores <see cref="CountedQuantity"/>
/// together with the balance's <c>OnHand</c> and <c>LastMovementId</c> read at that moment; posting uses
/// <c>CountedQuantity − ExpectedQuantity</c>. All count fields are null together or set together (<c>CK_StocktakeLines_Counted</c>).
/// </summary>
internal sealed class StocktakeLine : ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid StocktakeId { get; set; }

    public Guid ItemId { get; set; }

    /// <summary>The balance's <c>OnHand</c> when counted; may be negative at a location that allows it.</summary>
    public decimal? ExpectedQuantity { get; set; }

    /// <summary>The balance's <c>LastMovementId</c> when counted; null when the balance never existed.</summary>
    public long? ExpectedLastMovementId { get; set; }

    public decimal? CountedQuantity { get; set; }

    /// <summary>Per base unit; used only for a positive delta (plan D9).</summary>
    public decimal? UnitCost { get; set; }

    public DateTimeOffset? CountedAt { get; set; }

    public Guid? CountedBy { get; set; }
}