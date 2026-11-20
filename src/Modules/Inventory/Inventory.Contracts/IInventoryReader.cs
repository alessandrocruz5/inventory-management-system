namespace Inventory.Contracts;

/// <summary>
/// Read-only view of Inventory for other modules: the same numbers the screens show.
/// Every method is tenant-scoped to the current request; a foreign id reads as missing.
/// No snapshot carries a user identity. The reader doesn't check roles: the caller does.
/// </summary>
public interface IInventoryReader
{
    /// <summary>Items whose SKU or name contains <paramref name="search"/>, ordered as on the items screen; at most <paramref name="max"/> (1..100). Tenant-scoped to the current request.</summary>
    Task<ItemList> SearchItemsAsync(string? search, bool includeInactive, int max, CancellationToken cancellationToken);

    /// <summary>One item with its physical balances and units, or null when it doesn't exist. Tenant-scoped to the current request; a foreign id reads as missing.</summary>
    Task<ItemSnapshot?> GetItemAsync(Guid itemId, CancellationToken cancellationToken);

    /// <summary>One item by SKU (case-insensitive), or null when there is none or <paramref name="sku"/> is blank. Tenant-scoped to the current request; a foreign SKU reads as missing.</summary>
    Task<ItemSnapshot?> GetItemBySkuAsync(string sku, CancellationToken cancellationToken);

    /// <summary>Every location, physical first, inactive ones included. Tenant-scoped to the current request.</summary>
    Task<IReadOnlyList<LocationSummary>> ListLocationsAsync(CancellationToken cancellationToken);

    /// <summary>Stock rows at physical locations, optionally for one item or location; at most <paramref name="max"/> (1..100). The total value covers every matching row. Tenant-scoped to the current request; a foreign id matches nothing.</summary>
    Task<StockSnapshot> GetStockAsync(Guid? itemId, Guid? locationId, int max, CancellationToken cancellationToken);

    /// <summary>An item's movements, newest first; at most <paramref name="max"/> (1..100). Null when the item doesn't exist. Tenant-scoped to the current request; a foreign id reads as missing.</summary>
    Task<MovementList?> GetItemMovementsAsync(Guid itemId, int max, CancellationToken cancellationToken);

    /// <summary>Posted documents, newest first, optionally by exact type name (Receipt, Issue, Transfer, Adjustment, Reversal) and posted-at range; at most <paramref name="max"/> (1..100). Tenant-scoped to the current request.</summary>
    Task<DocumentList> ListDocumentsAsync(string? type, DateTimeOffset? postedFrom, DateTimeOffset? postedTo, int max, CancellationToken cancellationToken);

    /// <summary>One document with its lines, or null when it doesn't exist. Tenant-scoped to the current request; a foreign id reads as missing.</summary>
    Task<DocumentSnapshot?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>
    /// The ledger invariant checks. Tenant-scoped to the current request. The HTTP route is Admin-only, but this method
    /// enforces no role: the caller must check it before calling.
    /// </summary>
    Task<LedgerHealthSnapshot> GetLedgerHealthAsync(CancellationToken cancellationToken);
}