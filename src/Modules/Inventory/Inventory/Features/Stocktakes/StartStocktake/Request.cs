namespace Inventory.Features.Stocktakes.StartStocktake;

internal sealed record StartStocktakeRequest(Guid? LocationId, string? Notes);