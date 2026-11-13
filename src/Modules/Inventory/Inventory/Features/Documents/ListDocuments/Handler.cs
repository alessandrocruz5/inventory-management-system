using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Documents.ListDocuments;

internal sealed class ListDocumentsHandler(InventoryDbContext db)
{
    private static readonly string[] TypeNames = Enum.GetNames<StockDocumentType>();

    /// <summary>A revaluation movement is <c>LineNo</c> 1000 + n (spec §5); it is not a document line.</summary>
    private const int FirstRevaluationLineNo = 1000;

    private sealed record Row(Guid Id, StockDocumentType Type, AdjustmentReason? ReasonCode, string? Reference, DateTimeOffset PostedAt,
        Guid PostedBy, int LineCount, Guid? ReversesDocumentId, Guid? ReversedByDocumentId);

    public async Task<Results<Ok<PagedResponse<DocumentSummaryResponse>>, ValidationProblem>> HandleAsync(
        ListDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var (page, pageSize) = Paging.Resolve(request.Page, request.PageSize, errors);

        // Exact, ordinal names, not Enum.TryParse: it would accept "1", and "receipt" when ignoring case.
        StockDocumentType? type = null;
        if (request.Type is not null)
        {
            if (TypeNames.Contains(request.Type, StringComparer.Ordinal))
            {
                type = Enum.Parse<StockDocumentType>(request.Type);
            }
            else
            {
                errors["type"] = [$"Choose one of: {string.Join(", ", TypeNames)}."];
            }
        }

        if (request.PostedFrom is { } from && request.PostedTo is { } to && to <= from)
        {
            errors["postedTo"] = ["The end must be after the start."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var query = db.StockDocuments.AsNoTracking();
        if (type is { } wanted)
        {
            query = query.Where(d => d.Type == wanted);
        }

        if (request.PostedFrom is { } postedFrom)
        {
            query = query.Where(d => d.PostedAt >= postedFrom);
        }

        if (request.PostedTo is { } postedTo)
        {
            query = query.Where(d => d.PostedAt < postedTo);
        }

        var result = await query
            .OrderByDescending(d => d.PostedAt)
            .ThenByDescending(d => d.Id)
            .Select(d => new Row(
                d.Id,
                d.Type,
                d.ReasonCode,
                d.Reference,
                d.PostedAt,
                d.PostedBy,
                db.StockMovements.Count(m => m.DocumentId == d.Id && m.LineNo < FirstRevaluationLineNo),
                d.ReversesDocumentId,
                db.StockDocuments.Where(r => r.ReversesDocumentId == d.Id).Select(r => (Guid?)r.Id).FirstOrDefault()))
            .ToPageAsync(page, pageSize, cancellationToken);

        return TypedResults.Ok(new PagedResponse<DocumentSummaryResponse>(
            [.. result.Items.Select(r => new DocumentSummaryResponse(
                r.Id, r.Type.ToString(), r.ReasonCode?.ToString(), r.Reference, r.PostedAt, r.PostedBy, r.LineCount,
                r.ReversesDocumentId, r.ReversedByDocumentId))],
            result.Page,
            result.PageSize,
            result.Total));
    }
}