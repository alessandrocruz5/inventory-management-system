using Inventory.Persistence;

namespace Inventory.Features.Posting;

/// <summary>
/// A posted document as every posting route returns it, with the same shape for a <c>201</c> and a replay (plan D3):
/// its movements in <c>LineNo</c> order and the physical balances it touched, as of <see cref="BalancesAsOf"/>.
/// </summary>
internal sealed record DocumentResponse(
    Guid Id,
    string Type,
    string? ReasonCode,
    string? Reference,
    string? Notes,
    DateTimeOffset PostedAt,
    Guid PostedBy,
    IReadOnlyList<MovementResponse> Movements,
    IReadOnlyList<BalanceResponse> Balances,
    DateTimeOffset BalancesAsOf)
{
    public static DocumentResponse From(PostedOutcome outcome) => new(
        outcome.Document.Id,
        outcome.Document.Type.ToString(),
        outcome.Document.ReasonCode?.ToString(),
        outcome.Document.Reference,
        outcome.Document.Notes,
        outcome.Document.PostedAt,
        outcome.Document.PostedBy,
        outcome.Movements.OrderBy(m => m.LineNo).Select(MovementResponse.From).ToList(),
        outcome.Balances.Select(BalanceResponse.From).ToList(),
        outcome.BalancesAsOf);
}

internal sealed record MovementResponse(
    long Id,
    int LineNo,
    Guid ItemId,
    Guid FromLocationId,
    Guid ToLocationId,
    decimal Quantity,
    decimal TotalCost,
    long? ReversesMovementId)
{
    public static MovementResponse From(StockMovement movement) => new(
        movement.Id,
        movement.LineNo,
        movement.ItemId,
        movement.FromLocationId,
        movement.ToLocationId,
        movement.Quantity,
        movement.TotalCost,
        movement.ReversesMovementId);
}

internal sealed record BalanceResponse(Guid ItemId, Guid LocationId, decimal OnHand, decimal StockValue, decimal? LastUnitCost)
{
    public static BalanceResponse From(StockBalance balance) =>
        new(balance.ItemId, balance.LocationId, balance.OnHand, balance.StockValue, balance.LastUnitCost);
}