namespace Inventory.Features.Items.ListItems;

internal sealed record ListItemsRequest(string? Search, bool? Active, int? Page, int? PageSize);