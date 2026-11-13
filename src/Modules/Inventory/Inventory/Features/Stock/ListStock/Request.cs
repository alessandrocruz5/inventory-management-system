namespace Inventory.Features.Stock.ListStock;

internal sealed record ListStockRequest(Guid? ItemId, Guid? LocationId, int? Page, int? PageSize);