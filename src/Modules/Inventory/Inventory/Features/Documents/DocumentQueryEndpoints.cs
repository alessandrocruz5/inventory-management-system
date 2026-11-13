using Inventory.Features.Documents.GetDocument;
using Inventory.Features.Documents.ListDocuments;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Documents;

internal static class DocumentQueryEndpoints
{
    /// <summary>Maps the read side of <c>/documents</c>; the posting routes share the prefix from Features/Posting/DocumentEndpoints.</summary>
    public static IEndpointRouteBuilder MapDocumentQueryEndpoints(this IEndpointRouteBuilder api)
    {
        var documents = api.MapGroup("/documents");
        documents.MapListDocuments();
        documents.MapGetDocument();
        return api;
    }
}