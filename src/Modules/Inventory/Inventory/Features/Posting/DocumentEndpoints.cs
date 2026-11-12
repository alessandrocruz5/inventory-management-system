using Inventory.Features.Posting.PostAdjustment;
using Inventory.Features.Posting.PostIssue;
using Inventory.Features.Posting.PostReceipt;
using Inventory.Features.Posting.PostTransfer;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Posting;

internal static class DocumentEndpoints
{
    /// <summary>Maps <c>/documents</c> under the caller's group, which already requires a signed-in user (Api Program.cs).</summary>
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder api)
    {
        var documents = api.MapGroup("/documents");
        documents.MapPostReceipt();
        documents.MapPostIssue();
        documents.MapPostTransfer();
        documents.MapPostAdjustment();
        return api;
    }
}