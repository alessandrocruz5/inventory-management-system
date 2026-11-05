using Inventory.Features.Locations.CreateLocation;
using Inventory.Features.Locations.DeactivateLocation;
using Inventory.Features.Locations.ListLocations;
using Inventory.Features.Locations.UpdateLocation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Locations;

internal static class LocationEndpoints
{
    /// <summary>Maps <c>/locations</c> under the caller's group, which already requires a signed-in user (Api Program.cs).</summary>
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder api)
    {
        var locations = api.MapGroup("/locations");
        locations.MapListLocations();
        locations.MapCreateLocation();
        locations.MapUpdateLocation();
        locations.MapDeactivateLocation();
        return api;
    }
}