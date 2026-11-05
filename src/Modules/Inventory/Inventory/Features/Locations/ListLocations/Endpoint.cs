using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Locations.ListLocations;

internal static class ListLocationsEndpoint
{
    public static RouteHandlerBuilder MapListLocations(this RouteGroupBuilder locations) => locations.MapGet(
        "",
        ([AsParameters] ListLocationsRequest request, ListLocationsHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, cancellationToken));
}