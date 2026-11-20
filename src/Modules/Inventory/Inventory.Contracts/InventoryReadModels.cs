namespace Inventory.Contracts;

/// <summary>One page of items and the total that match.</summary>
public sealed record ItemList(IReadOnlyList<ItemSummary> Items, int Total);

/// <summary>An item without its balances.</summary>
public sealed record ItemSummary(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive);

/// <summary>An item with its balances at physical locations and its units.</summary>
public sealed record ItemSnapshot(
    Guid Id,
    string Sku,
    string Name,
    string BaseUnit,
    bool IsActive,
    IReadOnlyList<ItemBalanceSnapshot> Balances,
    IReadOnlyList<ItemUnitSnapshot> Units);

/// <summary>An item's on-hand quantity and value at one physical location.</summary>
public sealed record ItemBalanceSnapshot(
    Guid LocationId,
    string LocationCode,
    string LocationName,
    decimal OnHand,
    decimal StockValue,
    decimal? AverageCost,
    decimal? LastUnitCost);

/// <summary>An alternative unit: one <see cref="Code"/> is <see cref="Factor"/> base units.</summary>
public sealed record ItemUnitSnapshot(string Code, decimal Factor, bool IsActive);

/// <summary>A location. Virtual locations (SUP, CUS, ADJ) have <see cref="IsPhysical"/> false.</summary>
public sealed record LocationSummary(Guid Id, string Code, string Name, string Kind, bool IsPhysical, bool AllowNegativeStock, bool IsActive);

/// <summary>One page of stock rows, the total that match, and the value of all of them.</summary>
public sealed record StockSnapshot(IReadOnlyList<StockRowSnapshot> Rows, int Total, decimal TotalStockValue);

/// <summary>An item's balance at one physical location.</summary>
public sealed record StockRowSnapshot(
    Guid ItemId,
    string Sku,
    string ItemName,
    string BaseUnit,
    Guid LocationId,
    string LocationCode,
    string LocationName,
    decimal OnHand,
    decimal StockValue,
    decimal? AverageCost,
    decimal? LastUnitCost);

/// <summary>One page of an item's movements, newest first, and the total.</summary>
public sealed record MovementList(IReadOnlyList<MovementSnapshot> Movements, int Total);

/// <summary>One leg of a movement at a location, with the running balance after it.</summary>
public sealed record MovementSnapshot(
    long MovementId,
    Guid DocumentId,
    string DocumentType,
    DateTimeOffset PostedAt,
    string Direction,
    string LocationCode,
    string LocationName,
    string CounterpartLocationCode,
    string CounterpartLocationName,
    decimal QuantityChange,
    decimal ValueChange,
    decimal RunningOnHand,
    decimal RunningValue);

/// <summary>One page of documents, newest first, and the total that match.</summary>
public sealed record DocumentList(IReadOnlyList<DocumentSummarySnapshot> Documents, int Total);

/// <summary>A posted document without its lines.</summary>
public sealed record DocumentSummarySnapshot(
    Guid Id,
    string Type,
    string? ReasonCode,
    string? Reference,
    DateTimeOffset PostedAt,
    int LineCount,
    Guid? ReversesDocumentId,
    Guid? ReversedByDocumentId);

/// <summary>A posted document with its lines.</summary>
public sealed record DocumentSnapshot(
    Guid Id,
    string Type,
    string? ReasonCode,
    string? Reference,
    string? Notes,
    DateTimeOffset PostedAt,
    Guid? ReversesDocumentId,
    Guid? ReversedByDocumentId,
    IReadOnlyList<DocumentLineSnapshot> Lines);

/// <summary>One document line: a quantity moved from one location to another.</summary>
public sealed record DocumentLineSnapshot(
    int LineNo,
    Guid ItemId,
    string Sku,
    string ItemName,
    string BaseUnit,
    string FromLocationCode,
    string FromLocationName,
    string ToLocationCode,
    string ToLocationName,
    decimal Quantity,
    decimal? UnitCost,
    decimal TotalCost);

/// <summary>The result of the ledger invariant checks.</summary>
public sealed record LedgerHealthSnapshot(DateTimeOffset CheckedAt, bool Healthy, IReadOnlyList<InvariantSnapshot> Invariants);

/// <summary>One invariant check and how many rows fail it.</summary>
public sealed record InvariantSnapshot(string Id, string Name, bool Passed, int FailureCount);