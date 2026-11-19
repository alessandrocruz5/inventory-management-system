using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Stocktakes.PostStocktake;

internal static class PostStocktakeEndpoint
{
    /// <summary>Admin only (plan D11). No body: the stocktake's own lines are the posting, under a server-derived key (D10).</summary>
    public static RouteHandlerBuilder MapPostStocktake(this RouteGroupBuilder stocktakes) => stocktakes.MapPost(
        "/{id:guid}/post",
        (Guid id, PostStocktakeHandler handler, HttpContext http, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, http, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}