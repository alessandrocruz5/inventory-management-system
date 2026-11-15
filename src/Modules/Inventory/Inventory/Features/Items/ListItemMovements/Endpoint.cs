using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Items.ListItemMovements;

internal static class ListItemMovementsEndpoint
{
    public static RouteHandlerBuilder MapListItemMovements(this RouteGroupBuilder items) => items.MapGet(
        "/{id:guid}/movements",
        (Guid id, [AsParameters] ListItemMovementsRequest request, ListItemMovementsHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(id, request, cancellationToken));
}