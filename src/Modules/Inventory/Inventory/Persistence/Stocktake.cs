namespace Inventory.Persistence;

/// <summary>
/// A count of one physical location (plan D8): a mutable work document, not ledger data. Posting it makes one
/// <c>CountCorrection</c> adjustment, recorded in <see cref="PostedDocumentId"/>; a Posted stocktake with no differences has none (D13).
/// </summary>
internal sealed class Stocktake : ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public StocktakeStatus Status { get; set; }

    public string? Notes { get; set; }

    /// <summary>Set only when <see cref="Status"/> is Posted (<c>CK_Stocktakes_PostedDocument</c>).</summary>
    public Guid? PostedDocumentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];
}