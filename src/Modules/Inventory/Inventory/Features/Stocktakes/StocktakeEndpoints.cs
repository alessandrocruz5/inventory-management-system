using Inventory.Features.Stocktakes.CancelStocktake;
using Inventory.Features.Stocktakes.CountStocktakeLine;
using Inventory.Features.Stocktakes.GetStocktake;
using Inventory.Features.Stocktakes.ListStocktakes;
using Inventory.Features.Stocktakes.PostStocktake;
using Inventory.Features.Stocktakes.StartStocktake;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Stocktakes;

internal static class StocktakeEndpoints
{
    /// <summary>Maps <c>/stocktakes</c> under the caller's group, which already requires a signed-in user (Api Program.cs).</summary>
    public static IEndpointRouteBuilder MapStocktakeEndpoints(this IEndpointRouteBuilder api)
    {
        var stocktakes = api.MapGroup("/stocktakes");
        stocktakes.MapListStocktakes();
        stocktakes.MapGetStocktake();
        stocktakes.MapStartStocktake();
        stocktakes.MapCountStocktakeLine();
        stocktakes.MapCancelStocktake();
        stocktakes.MapPostStocktake();
        return api;
    }
}