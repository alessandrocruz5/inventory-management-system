using Inventory.Persistence;

namespace Inventory.Features.Items;

/// <summary>An item as every Items endpoint returns it; <c>Version</c> is the rowversion an edit sends back.</summary>
internal sealed record ItemResponse(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive, byte[] Version)
{
    public static ItemResponse From(Item item) => new(item.Id, item.Sku, item.Name, item.BaseUnit, item.IsActive, item.RowVersion);
}