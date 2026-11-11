using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Posting.PostReceipt;

internal static class PostReceiptEndpoint
{
    /// <summary>No role policy: Staff and Admin may both post receipts (spec §9).</summary>
    public static RouteHandlerBuilder MapPostReceipt(this RouteGroupBuilder documents) => documents.MapPost(
        "/receipts",
        (PostReceiptRequest request, PostReceiptHandler handler, HttpContext http, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, http, cancellationToken));
}