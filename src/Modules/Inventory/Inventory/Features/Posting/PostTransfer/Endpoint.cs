using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Posting.PostTransfer;

internal static class PostTransferEndpoint
{
    /// <summary>No role policy: Staff and Admin may both post transfers (spec §9).</summary>
    public static RouteHandlerBuilder MapPostTransfer(this RouteGroupBuilder documents) => documents.MapPost(
        "/transfers",
        (PostTransferRequest request, PostTransferHandler handler, HttpContext http, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, http, cancellationToken));
}