using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Documents.ListDocuments;

internal static class ListDocumentsEndpoint
{
    public static RouteHandlerBuilder MapListDocuments(this RouteGroupBuilder documents) => documents.MapGet(
        "",
        ([AsParameters] ListDocumentsRequest request, ListDocumentsHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, cancellationToken));
}