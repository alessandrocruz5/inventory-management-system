using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Stocktakes.StartStocktake;

internal static class StartStocktakeEndpoint
{
    /// <summary>Any signed-in user may start a count (plan D11).</summary>
    public static RouteHandlerBuilder MapStartStocktake(this RouteGroupBuilder stocktakes) => stocktakes.MapPost(
        "",
        (StartStocktakeRequest request, StartStocktakeHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, cancellationToken));
}