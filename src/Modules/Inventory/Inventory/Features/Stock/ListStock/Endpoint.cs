using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Stock.ListStock;

internal static class ListStockEndpoint
{
    public static RouteHandlerBuilder MapListStock(this RouteGroupBuilder stock) => stock.MapGet(
        "",
        ([AsParameters] ListStockRequest request, ListStockHandler handler, CancellationToken cancellationToken) =>
            handler.HandleAsync(request, cancellationToken));
}