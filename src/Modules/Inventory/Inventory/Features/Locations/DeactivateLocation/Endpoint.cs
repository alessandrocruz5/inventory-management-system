using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Locations.DeactivateLocation;

internal static class DeactivateLocationEndpoint
{
    public static RouteHandlerBuilder MapDeactivateLocation(this RouteGroupBuilder locations) => locations.MapPost(
        "/{id:guid}/deactivate",
        (Guid id, DeactivateLocationHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}