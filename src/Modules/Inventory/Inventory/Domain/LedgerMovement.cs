namespace Inventory.Domain;

/// <summary>A posted <c>StockMovements</c> row, as the posting rules and the ledger folds see it.</summary>
internal sealed record LedgerMovement(
    long Id,
    Guid DocumentId,
    int LineNo,
    Guid ItemId,
    Guid FromLocationId,
    Guid ToLocationId,
    decimal Quantity,
    decimal TotalCost,
    long? ReversesMovementId);

/// <summary>A movement the posting rules decided on; the engine adds <c>Id</c>, <c>DocumentId</c>, <c>PostedAt</c> and <c>PostedBy</c>.</summary>
internal sealed record PlannedMovement(
    int LineNo,
    Guid ItemId,
    Guid FromLocationId,
    Guid ToLocationId,
    decimal Quantity,
    decimal TotalCost,
    long? ReversesMovementId);