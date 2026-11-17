using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Posting.PostAdjustment;

internal static class PostAdjustmentEndpoint
{
    /// <summary>Admin only (spec §9); Staff gets 403.</summary>
    public static RouteHandlerBuilder MapPostAdjustment(this RouteGroupBuilder documents) => documents.MapPost(
        "/adjustments",
        (PostAdjustmentRequest request, PostAdjustmentHandler handler, HttpContext http, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, http, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}