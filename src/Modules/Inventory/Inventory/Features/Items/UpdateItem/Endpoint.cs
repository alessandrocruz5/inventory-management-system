using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Items.UpdateItem;

internal static class UpdateItemEndpoint
{
    public static RouteHandlerBuilder MapUpdateItem(this RouteGroupBuilder items) => items.MapPut(
        "/{id:guid}",
        (Guid id, UpdateItemRequest request, UpdateItemHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, request, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}