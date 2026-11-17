using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Posting.PostIssue;

internal static class PostIssueEndpoint
{
    /// <summary>No role policy: Staff and Admin may both post issues (spec §9).</summary>
    public static RouteHandlerBuilder MapPostIssue(this RouteGroupBuilder documents) => documents.MapPost(
        "/issues",
        (PostIssueRequest request, PostIssueHandler handler, HttpContext http, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, http, cancellationToken));
}