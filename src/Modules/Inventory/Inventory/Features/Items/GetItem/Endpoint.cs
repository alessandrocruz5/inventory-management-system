using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Items.GetItem;

internal static class GetItemEndpoint
{
    public static RouteHandlerBuilder MapGetItem(this RouteGroupBuilder items) => items.MapGet(
        "/{id:guid}",
        (Guid id, GetItemHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, cancellationToken));
}