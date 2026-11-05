namespace Inventory.Features.Items.UpdateItem;

internal sealed record UpdateItemRequest(string? Sku, string? Name, string? BaseUnit, byte[]? Version);