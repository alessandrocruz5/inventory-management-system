namespace Inventory.Domain;

/// <summary>A business rejection: it aborts the whole posting and maps to ProblemDetails (spec §9).</summary>
internal abstract class PostingRejectedException(string message) : Exception(message);

/// <summary>Maps to 422 insufficient-stock (itemId, locationId, available, requested).</summary>
internal sealed class InsufficientStockException(Guid itemId, Guid locationId, decimal available, decimal requested)
    : PostingRejectedException($"Insufficient stock: {requested} requested, {available} available.")
{
    public Guid ItemId => itemId;

    public Guid LocationId => locationId;

    public decimal Available => available;

    public decimal Requested => requested;
}

/// <summary>A positive adjustment line without a unit cost while OnHand &lt;= 0. Maps to 400 validation on lines[i].unitCost.</summary>
internal sealed class UnitCostRequiredException(int lineNo)
    : PostingRejectedException($"Line {lineNo}: a unit cost is required when there is no stock on hand to average.")
{
    public int LineNo => lineNo;
}

/// <summary>Maps to 409 reversal-not-latest; the engine resolves <see cref="BlockingMovementId"/> to its document.</summary>
internal sealed class ReversalNotLatestException(Guid itemId, Guid locationId, long blockingMovementId)
    : PostingRejectedException($"Movement {blockingMovementId} was posted later for this item and location; reverse it first.")
{
    public Guid ItemId => itemId;

    public Guid LocationId => locationId;

    public long BlockingMovementId => blockingMovementId;
}