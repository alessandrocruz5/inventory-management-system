using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Locations.UpdateLocation;

internal static class UpdateLocationEndpoint
{
    public static RouteHandlerBuilder MapUpdateLocation(this RouteGroupBuilder locations) => locations.MapPut(
        "/{id:guid}",
        (Guid id, UpdateLocationRequest request, UpdateLocationHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, request, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}