using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Items.Units.DeactivateItemUnit;

internal static class DeactivateItemUnitEndpoint
{
    public static RouteHandlerBuilder MapDeactivateItemUnit(this RouteGroupBuilder items) => items.MapPost(
        "/{id:guid}/units/{code}/deactivate",
        (Guid id, string code, DeactivateItemUnitHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, code, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}