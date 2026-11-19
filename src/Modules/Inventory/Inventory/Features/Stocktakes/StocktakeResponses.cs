namespace Inventory.Features.Stocktakes;

/// <summary>A stocktake as the list shows it.</summary>
internal sealed record StocktakeSummaryResponse(
    Guid Id,
    Guid LocationId,
    string LocationCode,
    string LocationName,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    int LineCount,
    int CountedCount,
    Guid? PostedDocumentId);

/// <summary>A stocktake with its lines in <c>Sku</c> order (plan D14).</summary>
internal sealed record StocktakeDetailResponse(
    Guid Id,
    Guid LocationId,
    string LocationCode,
    string LocationName,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? UpdatedAt,
    Guid? PostedDocumentId,
    IReadOnlyList<StocktakeLineResponse> Lines);

/// <summary>
/// One line, in base units. <see cref="Difference"/> is counted − expected once counted. <see cref="CurrentOnHand"/> and
/// <see cref="AverageCost"/> come from the balance now (0 and null without one); <see cref="MovedSinceCount"/> is true when a
/// posting touched that balance after the count.
/// </summary>
internal sealed record StocktakeLineResponse(
    Guid ItemId,
    string Sku,
    string Name,
    string BaseUnit,
    bool ItemIsActive,
    decimal? ExpectedQuantity,
    decimal? CountedQuantity,
    decimal? Difference,
    decimal? UnitCost,
    DateTimeOffset? CountedAt,
    Guid? CountedBy,
    decimal CurrentOnHand,
    decimal? AverageCost,
    bool MovedSinceCount);