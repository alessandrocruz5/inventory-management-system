using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Items.ListItems;

internal static class ListItemsEndpoint
{
    public static RouteHandlerBuilder MapListItems(this RouteGroupBuilder items) => items.MapGet(
        "",
        ([AsParameters] ListItemsRequest request, ListItemsHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, cancellationToken));
}