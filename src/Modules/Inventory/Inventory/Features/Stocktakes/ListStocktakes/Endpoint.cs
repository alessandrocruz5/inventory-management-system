using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Stocktakes.ListStocktakes;

internal static class ListStocktakesEndpoint
{
    public static RouteHandlerBuilder MapListStocktakes(this RouteGroupBuilder stocktakes) => stocktakes.MapGet(
        "",
        ([AsParameters] ListStocktakesRequest request, ListStocktakesHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, cancellationToken));
}