using Inventory.Contracts;
using Inventory.Features;
using Inventory.Features.Documents.GetDocument;
using Inventory.Features.Documents.ListDocuments;
using Inventory.Features.Items.GetItem;
using Inventory.Features.Items.ListItemMovements;
using Inventory.Features.Items.ListItems;
using Inventory.Features.Ledger.GetLedgerHealth;
using Inventory.Features.Locations.ListLocations;
using Inventory.Features.Stock.ListStock;
using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory;

/// <summary>
/// <see cref="IInventoryReader"/> over the screens' own read handlers, so other modules see exactly what the API returns.
/// It runs in the request's scope, where the tenant filter and RLS apply. It maps no user identity and checks no role.
/// </summary>
internal sealed class InventoryReader(
    InventoryDbContext db,
    ListItemsHandler listItems,
    GetItemHandler getItem,
    ListLocationsHandler listLocations,
    ListStockHandler listStock,
    ListItemMovementsHandler listItemMovements,
    ListDocumentsHandler listDocuments,
    GetDocumentHandler getDocument,
    GetLedgerHealthHandler getLedgerHealth) : IInventoryReader
{
    public async Task<ItemList> SearchItemsAsync(string? search, bool includeInactive, int max, CancellationToken cancellationToken)
    {
        CheckMax(max);
        var result = await listItems.HandleAsync(
            new ListItemsRequest(search, includeInactive ? null : true, Page: 1, PageSize: max), cancellationToken);
        var page = result.Result switch
        {
            Ok<PagedResponse<Features.Items.ItemResponse>> ok => ok.Value!,
            ValidationProblem invalid => throw Invalid(invalid),
            _ => throw Unexpected(result.Result),
        };
        return new ItemList(page.Items.Select(i => new ItemSummary(i.Id, i.Sku, i.Name, i.BaseUnit, i.IsActive)).ToList(), page.Total);
    }

    public async Task<ItemSnapshot?> GetItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var result = await getItem.HandleAsync(itemId, cancellationToken);
        var item = result.Result switch
        {
            Ok<Features.Items.ItemDetailResponse> ok => ok.Value!,
            ProblemHttpResult { StatusCode: StatusCodes.Status404NotFound } => null,
            _ => throw Unexpected(result.Result),
        };
        if (item is null)
        {
            return null;
        }

        return new ItemSnapshot(
            item.Id,
            item.Sku,
            item.Name,
            item.BaseUnit,
            item.IsActive,
            item.Balances.Select(b => new ItemBalanceSnapshot(
                b.LocationId, b.LocationCode, b.LocationName, b.OnHand, b.StockValue, b.AverageCost, b.LastUnitCost)).ToList(),
            item.Units.Select(u => new ItemUnitSnapshot(u.Code, u.Factor, u.IsActive)).ToList());
    }

    public async Task<ItemSnapshot?> GetItemBySkuAsync(string sku, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }

        // The named tenant filter applies, SKUs are unique per tenant, and the column collation ignores case.
        var id = await db.Items.AsNoTracking()
            .Where(i => i.Sku == sku)
            .Select(i => (Guid?)i.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return id is { } itemId ? await GetItemAsync(itemId, cancellationToken) : null;
    }

    public async Task<IReadOnlyList<LocationSummary>> ListLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = new List<LocationSummary>();
        for (var pageNo = 1; ; pageNo++)
        {
            var result = await listLocations.HandleAsync(
                new ListLocationsRequest(pageNo, Paging.MaxPageSize), cancellationToken);
            var page = result.Result switch
            {
                Ok<PagedResponse<Features.Locations.LocationResponse>> ok => ok.Value!,
                ValidationProblem invalid => throw Invalid(invalid),
                _ => throw Unexpected(result.Result),
            };
            locations.AddRange(page.Items.Select(l => new LocationSummary(
                l.Id, l.Code, l.Name, l.Kind, IsPhysical: !l.IsSystem, l.AllowNegativeStock, l.IsActive)));
            if (page.Items.Count == 0 || locations.Count >= page.Total)
            {
                return locations;
            }
        }
    }

    public async Task<StockSnapshot> GetStockAsync(Guid? itemId, Guid? locationId, int max, CancellationToken cancellationToken)
    {
        CheckMax(max);
        var result = await listStock.HandleAsync(
            new ListStockRequest(itemId, locationId, Page: 1, PageSize: max), cancellationToken);
        var page = result.Result switch
        {
            Ok<Features.Stock.StockPageResponse> ok => ok.Value!,
            ValidationProblem invalid => throw Invalid(invalid),
            _ => throw Unexpected(result.Result),
        };
        return new StockSnapshot(
            page.Items.Select(r => new StockRowSnapshot(
                r.ItemId, r.Sku, r.ItemName, r.BaseUnit, r.LocationId, r.LocationCode, r.LocationName,
                r.OnHand, r.StockValue, r.AverageCost, r.LastUnitCost)).ToList(),
            page.Total,
            page.Totals.StockValue);
    }

    public async Task<MovementList?> GetItemMovementsAsync(Guid itemId, int max, CancellationToken cancellationToken)
    {
        CheckMax(max);
        var result = await listItemMovements.HandleAsync(
            itemId, new ListItemMovementsRequest(Page: 1, PageSize: max), cancellationToken);
        var page = result.Result switch
        {
            Ok<PagedResponse<Features.Items.ItemMovementResponse>> ok => ok.Value!,
            ValidationProblem invalid => throw Invalid(invalid),
            ProblemHttpResult { StatusCode: StatusCodes.Status404NotFound } => null,
            _ => throw Unexpected(result.Result),
        };
        if (page is null)
        {
            return null;
        }

        // Field by field: PostedBy stays behind (plan D10).
        return new MovementList(
            page.Items.Select(m => new MovementSnapshot(
                m.MovementId, m.DocumentId, m.DocumentType, m.PostedAt, m.Direction,
                m.LocationCode, m.LocationName, m.CounterpartLocationCode, m.CounterpartLocationName,
                m.QuantityChange, m.ValueChange, m.RunningOnHand, m.RunningValue)).ToList(),
            page.Total);
    }

    public async Task<DocumentList> ListDocumentsAsync(
        string? type, DateTimeOffset? postedFrom, DateTimeOffset? postedTo, int max, CancellationToken cancellationToken)
    {
        CheckMax(max);
        var result = await listDocuments.HandleAsync(
            new ListDocumentsRequest(type, postedFrom, postedTo, Page: 1, PageSize: max), cancellationToken);
        var page = result.Result switch
        {
            Ok<PagedResponse<Features.Documents.DocumentSummaryResponse>> ok => ok.Value!,
            ValidationProblem invalid => throw Invalid(invalid),
            _ => throw Unexpected(result.Result),
        };

        // Field by field: PostedBy stays behind (plan D10).
        return new DocumentList(
            page.Items.Select(d => new DocumentSummarySnapshot(
                d.Id, d.Type, d.ReasonCode, d.Reference, d.PostedAt, d.LineCount, d.ReversesDocumentId, d.ReversedByDocumentId)).ToList(),
            page.Total);
    }

    public async Task<DocumentSnapshot?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await getDocument.HandleAsync(documentId, cancellationToken);
        var document = result.Result switch
        {
            Ok<Features.Documents.DocumentDetailResponse> ok => ok.Value!,
            ProblemHttpResult { StatusCode: StatusCodes.Status404NotFound } => null,
            _ => throw Unexpected(result.Result),
        };
        if (document is null)
        {
            return null;
        }

        // Field by field: PostedBy stays behind (plan D10).
        return new DocumentSnapshot(
            document.Id,
            document.Type,
            document.ReasonCode,
            document.Reference,
            document.Notes,
            document.PostedAt,
            document.ReversesDocumentId,
            document.ReversedByDocumentId,
            document.Lines.Select(l => new DocumentLineSnapshot(
                l.LineNo, l.ItemId, l.Sku, l.ItemName, l.BaseUnit,
                l.FromLocationCode, l.FromLocationName, l.ToLocationCode, l.ToLocationName,
                l.Quantity, l.UnitCost, l.TotalCost)).ToList());
    }

    public async Task<LedgerHealthSnapshot> GetLedgerHealthAsync(CancellationToken cancellationToken)
    {
        var health = (await getLedgerHealth.HandleAsync(cancellationToken)).Value!;
        return new LedgerHealthSnapshot(
            health.CheckedAt,
            health.Healthy,
            health.Invariants.Select(i => new InvariantSnapshot(i.Id, i.Name, i.Passed, i.FailureCount)).ToList());
    }

    private static void CheckMax(int max)
    {
        if (max is < 1 or > Paging.MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, $"Max must be between 1 and {Paging.MaxPageSize}.");
        }
    }

    private static ArgumentException Invalid(ValidationProblem problem)
    {
        var (key, messages) = problem.ProblemDetails.Errors.First();
        return new ArgumentException(messages.FirstOrDefault() ?? "The request is invalid.", key);
    }

    private static InvalidOperationException Unexpected(IResult result) =>
        new($"Unexpected handler result {result.GetType().Name}.");
}