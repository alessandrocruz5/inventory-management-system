using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Reversals.ReverseDocument;

internal static class ReverseDocumentEndpoint
{
    /// <summary>Admin only (spec §9); Staff gets 403. The body is empty and never bound.</summary>
    public static RouteHandlerBuilder MapReverseDocument(this RouteGroupBuilder documents) => documents.MapPost(
        "/{id:guid}/reversal",
        (Guid id, ReverseDocumentHandler handler, HttpContext http, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, http, cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}