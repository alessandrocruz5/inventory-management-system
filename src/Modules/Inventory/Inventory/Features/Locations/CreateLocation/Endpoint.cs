using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Locations.CreateLocation;

internal static class CreateLocationEndpoint
{
    public static RouteHandlerBuilder MapCreateLocation(this RouteGroupBuilder locations) => locations.MapPost(
        "",
        (CreateLocationRequest request, CreateLocationHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}