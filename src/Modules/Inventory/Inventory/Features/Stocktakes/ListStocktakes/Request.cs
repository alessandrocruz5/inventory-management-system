namespace Inventory.Features.Stocktakes.ListStocktakes;

internal sealed record ListStocktakesRequest(int? Page, int? PageSize, string? Status, Guid? LocationId);