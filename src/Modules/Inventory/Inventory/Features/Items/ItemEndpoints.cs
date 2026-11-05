using Inventory.Features.Items.CreateItem;
using Inventory.Features.Items.DeactivateItem;
using Inventory.Features.Items.GetItem;
using Inventory.Features.Items.ListItems;
using Inventory.Features.Items.UpdateItem;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Items;

internal static class ItemEndpoints
{
    /// <summary>Maps <c>/items</c> under the caller's group, which already requires a signed-in user (Api Program.cs).</summary>
    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder api)
    {
        var items = api.MapGroup("/items");
        items.MapListItems();
        items.MapGetItem();
        items.MapCreateItem();
        items.MapUpdateItem();
        items.MapDeactivateItem();
        return api;
    }
}