using Inventory.Features.Stock.ListStock;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Stock;

internal static class StockEndpoints
{
    /// <summary>Maps <c>/stock</c> under the caller's group, which already requires a signed-in user (Api Program.cs).</summary>
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder api)
    {
        var stock = api.MapGroup("/stock");
        stock.MapListStock();
        return api;
    }
}