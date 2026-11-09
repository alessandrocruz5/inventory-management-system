namespace Inventory.Persistence;

internal sealed class StockDocument : ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public required StockDocumentType Type { get; set; }

    /// <summary>Set if and only if <see cref="Type"/> is <see cref="StockDocumentType.Reversal"/>.</summary>
    public Guid? ReversesDocumentId { get; set; }

    /// <summary>Required on an adjustment, null on every other type.</summary>
    public AdjustmentReason? ReasonCode { get; set; }

    public string? Reference { get; set; }

    public string? Notes { get; set; }

    public required string IdempotencyKey { get; set; }

    /// <summary>SHA-256 of the canonical request (spec §6, step 2).</summary>
    public required byte[] RequestHash { get; set; }

    public DateTimeOffset PostedAt { get; set; }

    public Guid PostedBy { get; set; }
}