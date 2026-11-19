using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Stocktakes.CancelStocktake;

internal static class CancelStocktakeEndpoint
{
    /// <summary>Admin only (plan D11).</summary>
    public static RouteHandlerBuilder MapCancelStocktake(this RouteGroupBuilder stocktakes) => stocktakes.MapPost(
        "/{id:guid}/cancel",
        (Guid id, CancelStocktakeHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}