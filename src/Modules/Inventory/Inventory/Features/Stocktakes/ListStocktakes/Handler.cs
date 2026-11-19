using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Stocktakes.ListStocktakes;

/// <summary>Stocktakes, newest first, optionally of one status or location.</summary>
internal sealed class ListStocktakesHandler(InventoryDbContext db)
{
    private static readonly string[] StatusNames = Enum.GetNames<StocktakeStatus>();

    private sealed record Row(Guid Id, Guid LocationId, string LocationCode, string LocationName, StocktakeStatus Status, string? Notes,
        DateTimeOffset CreatedAt, Guid CreatedBy, int LineCount, int CountedCount, Guid? PostedDocumentId);

    public async Task<Results<Ok<PagedResponse<StocktakeSummaryResponse>>, ValidationProblem>> HandleAsync(
        ListStocktakesRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (page, pageSize) = Paging.Resolve(request.Page, request.PageSize, errors);

        // Exact, ordinal names, not Enum.TryParse: it would accept "1", and "draft" when ignoring case.
        StocktakeStatus? status = null;
        if (request.Status is not null)
        {
            if (StatusNames.Contains(request.Status, StringComparer.Ordinal))
            {
                status = Enum.Parse<StocktakeStatus>(request.Status);
            }
            else
            {
                errors["status"] = [$"Choose one of: {string.Join(", ", StatusNames)}."];
            }
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var query = db.Stocktakes.AsNoTracking();
        if (status is { } wanted)
        {
            query = query.Where(s => s.Status == wanted);
        }

        if (request.LocationId is { } locationId)
        {
            query = query.Where(s => s.LocationId == locationId);
        }

        var result = await (
            from s in query
            join l in db.Locations on s.LocationId equals l.Id
            orderby s.CreatedAt descending, s.Id descending
            select new Row(
                s.Id,
                s.LocationId,
                l.Code,
                l.Name,
                s.Status,
                s.Notes,
                s.CreatedAt,
                s.CreatedBy,
                db.StocktakeLines.Count(x => x.StocktakeId == s.Id),
                db.StocktakeLines.Count(x => x.StocktakeId == s.Id && x.CountedQuantity != null),
                s.PostedDocumentId))
            .ToPageAsync(page, pageSize, cancellationToken);

        return TypedResults.Ok(new PagedResponse<StocktakeSummaryResponse>(
            [.. result.Items.Select(r => new StocktakeSummaryResponse(
                r.Id, r.LocationId, r.LocationCode, r.LocationName, r.Status.ToString(), r.Notes, r.CreatedAt, r.CreatedBy,
                r.LineCount, r.CountedCount, r.PostedDocumentId))],
            result.Page,
            result.PageSize,
            result.Total));
    }
}