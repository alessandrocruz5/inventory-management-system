using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Stocktakes.GetStocktake;

internal static class GetStocktakeEndpoint
{
    public static RouteHandlerBuilder MapGetStocktake(this RouteGroupBuilder stocktakes) => stocktakes.MapGet(
        "/{id:guid}",
        (Guid id, GetStocktakeHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, cancellationToken));
}