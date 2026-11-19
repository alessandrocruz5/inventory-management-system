using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Items.Units.CreateItemUnit;

internal static class CreateItemUnitEndpoint
{
    public static RouteHandlerBuilder MapCreateItemUnit(this RouteGroupBuilder items) => items.MapPost(
        "/{id:guid}/units",
        (Guid id, CreateItemUnitRequest request, CreateItemUnitHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, request, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}