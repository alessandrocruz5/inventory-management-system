using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Items.CreateItem;

internal static class CreateItemEndpoint
{
    public static RouteHandlerBuilder MapCreateItem(this RouteGroupBuilder items) => items.MapPost(
        "",
        (CreateItemRequest request, CreateItemHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}