using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Stocktakes.CountStocktakeLine;

internal static class CountStocktakeLineEndpoint
{
    /// <summary>Any signed-in user may count (plan D11).</summary>
    public static RouteHandlerBuilder MapCountStocktakeLine(this RouteGroupBuilder stocktakes) => stocktakes.MapPut(
        "/{id:guid}/lines/{itemId:guid}",
        (Guid id, Guid itemId, CountStocktakeLineRequest request, CountStocktakeLineHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, itemId, request, cancellationToken));
}