using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Inventory.Features.Stocktakes.GetStocktake;

internal sealed class GetStocktakeHandler(StocktakeReader reader)
{
    public async Task<Results<Ok<StocktakeDetailResponse>, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await reader.ReadAsync(id, cancellationToken) is { } detail ? TypedResults.Ok(detail) : Problems.NotFound();
}