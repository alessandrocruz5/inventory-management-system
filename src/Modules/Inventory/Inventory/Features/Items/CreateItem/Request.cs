namespace Inventory.Features.Items.CreateItem;

internal sealed record CreateItemRequest(string? Sku, string? Name, string? BaseUnit);