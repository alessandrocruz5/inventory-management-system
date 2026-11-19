namespace Inventory.Features.Stocktakes.CountStocktakeLine;

/// <summary><see cref="Unit"/> is a code of the item's units (the base unit when null); <see cref="UnitCost"/> is per base unit.</summary>
internal sealed record CountStocktakeLineRequest(decimal? Quantity, string? Unit, decimal? UnitCost);