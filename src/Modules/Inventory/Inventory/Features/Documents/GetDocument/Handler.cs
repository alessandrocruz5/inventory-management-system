using Inventory.Domain;
using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Documents.GetDocument;

/// <summary>One document with every movement as a line; another tenant's id is invisible, so it is a 404.</summary>
internal sealed class GetDocumentHandler(InventoryDbContext db)
{
    public async Task<Results<Ok<DocumentDetailResponse>, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.StockDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return Problems.NotFound();
        }

        var reversedBy = await db.StockDocuments
            .Where(r => r.ReversesDocumentId == id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var rows = await (
            from m in db.StockMovements.AsNoTracking()
            where m.DocumentId == id
            join i in db.Items on m.ItemId equals i.Id
            join f in db.Locations on m.FromLocationId equals f.Id
            join t in db.Locations on m.ToLocationId equals t.Id
            orderby m.LineNo
            select new
            {
                m.Id,
                m.LineNo,
                m.ItemId,
                i.Sku,
                ItemName = i.Name,
                i.BaseUnit,
                m.FromLocationId,
                FromCode = f.Code,
                FromName = f.Name,
                m.ToLocationId,
                ToCode = t.Code,
                ToName = t.Name,
                m.Quantity,
                m.TotalCost,
                m.ReversesMovementId,
            }).ToListAsync(cancellationToken);

        var lines = rows.Select(r => new DocumentLineResponse(
            r.Id, r.LineNo, r.ItemId, r.Sku, r.ItemName, r.BaseUnit,
            r.FromLocationId, r.FromCode, r.FromName, r.ToLocationId, r.ToCode, r.ToName,
            r.Quantity,
            r.Quantity > 0 ? CostingPolicy.Round4(r.TotalCost / r.Quantity) : null,
            r.TotalCost,
            r.ReversesMovementId)).ToList();

        return TypedResults.Ok(new DocumentDetailResponse(
            document.Id,
            document.Type.ToString(),
            document.ReasonCode?.ToString(),
            document.Reference,
            document.Notes,
            document.PostedAt,
            document.PostedBy,
            document.ReversesDocumentId,
            reversedBy,
            lines));
    }
}