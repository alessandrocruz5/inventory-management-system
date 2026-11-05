using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Items.DeactivateItem;

internal static class DeactivateItemEndpoint
{
    public static RouteHandlerBuilder MapDeactivateItem(this RouteGroupBuilder items) => items.MapPost(
        "/{id:guid}/deactivate",
        (Guid id, DeactivateItemHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}