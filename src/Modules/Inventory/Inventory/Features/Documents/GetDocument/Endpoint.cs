using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Documents.GetDocument;

internal static class GetDocumentEndpoint
{
    public static RouteHandlerBuilder MapGetDocument(this RouteGroupBuilder documents) => documents.MapGet(
        "/{id:guid}",
        (Guid id, GetDocumentHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, cancellationToken));
}