using System.ComponentModel;
using System.Globalization;

using Inventory.Contracts;

namespace Assistant.Tools;

/// <summary>
/// The assistant's read-only tools. They read only through <see cref="IInventoryReader"/>, which is tenant-scoped, so
/// a foreign SKU, location code or document id reads as not found. Results carry no user identity (plan D10).
/// The instance is request-scoped: each tool captures the reader here instead of resolving services at call time.
/// </summary>
internal sealed class InventoryTools(IInventoryReader reader)
{
    private const int ListCap = 25;
    private const int StockCap = 50;
    private const int HistoryRead = 100;

    private static readonly string[] DocumentTypes = ["Receipt", "Issue", "Transfer", "Adjustment", "Reversal"];

    private static ToolError NotFound(string message) => new("not-found", message);

    private static ToolError Invalid(string message) => new("invalid-argument", message);

    public async Task<object> FindItemsAsync(
        [Description("Part of a SKU or item name. Omit to list items.")] string? query = null,
        [Description("Include deactivated items.")] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            var list = await reader.SearchItemsAsync(query, includeInactive, ListCap, ct);
            return new ItemsResult(
                [.. list.Items.Select(i => new ItemRow(i.Sku, i.Name, i.BaseUnit, i.IsActive))],
                list.Total,
                list.Total > list.Items.Count);
        }
        catch (ArgumentException ex)
        {
            return Invalid(ex.Message);
        }
    }

    public async Task<object> GetItemAsync(
        [Description("The item's SKU, such as RICE-25.")] string sku,
        CancellationToken ct = default)
    {
        try
        {
            var item = await reader.GetItemBySkuAsync(sku, ct);
            return item is null
                ? NotFound($"No item with SKU {sku}. Use find_items.")
                : new ItemResult(
                    item.Sku,
                    item.Name,
                    item.BaseUnit,
                    item.IsActive,
                    [.. item.Units.Select(u => new UnitRow(u.Code, u.Factor, u.IsActive))],
                    [.. item.Balances.Select(b => new BalanceRow(b.LocationCode, b.OnHand, b.StockValue, b.AverageCost, b.LastUnitCost))]);
        }
        catch (ArgumentException ex)
        {
            return Invalid(ex.Message);
        }
    }

    public async Task<object> ListLocationsAsync(CancellationToken ct = default)
    {
        try
        {
            var locations = await reader.ListLocationsAsync(ct);
            return new LocationsResult(
                [.. locations.Where(l => l.IsPhysical).Select(l => new LocationRow(l.Code, l.Name, l.IsActive, l.AllowNegativeStock))]);
        }
        catch (ArgumentException ex)
        {
            return Invalid(ex.Message);
        }
    }

    public async Task<object> GetStockAsync(
        [Description("An item's SKU. Omit for all items.")] string? sku = null,
        [Description("A location code, such as WH-1. Omit for all locations.")] string? location = null,
        CancellationToken ct = default)
    {
        try
        {
            Guid? itemId = null;
            if (!string.IsNullOrWhiteSpace(sku))
            {
                var item = await reader.GetItemBySkuAsync(sku, ct);
                if (item is null)
                {
                    return NotFound($"No item with SKU {sku}. Use find_items.");
                }

                itemId = item.Id;
            }

            Guid? locationId = null;
            if (!string.IsNullOrWhiteSpace(location))
            {
                var found = await FindPhysicalLocationAsync(location, ct);
                if (found is null)
                {
                    return NotFound($"No location with code {location}. Use list_locations.");
                }

                locationId = found.Id;
            }

            var stock = await reader.GetStockAsync(itemId, locationId, StockCap, ct);
            return new StockResult(
                [.. stock.Rows.Select(r => new StockRow(r.Sku, r.ItemName, r.LocationCode, r.OnHand, r.BaseUnit, r.StockValue, r.AverageCost))],
                stock.TotalStockValue,
                stock.Total,
                stock.Total > stock.Rows.Count);
        }
        catch (ArgumentException ex)
        {
            return Invalid(ex.Message);
        }
    }

    public async Task<object> GetItemHistoryAsync(
        [Description("The item's SKU.")] string sku,
        [Description("A location code to show only that location's movements.")] string? location = null,
        [Description("How many movements to return, 1 to 25. Default 10.")] int limit = 10,
        CancellationToken ct = default)
    {
        try
        {
            var item = await reader.GetItemBySkuAsync(sku, ct);
            if (item is null)
            {
                return NotFound($"No item with SKU {sku}. Use find_items.");
            }

            limit = Math.Clamp(limit, 1, ListCap);
            var list = await reader.GetItemMovementsAsync(item.Id, HistoryRead, ct);
            if (list is null)
            {
                return NotFound($"No item with SKU {sku}. Use find_items.");
            }

            IEnumerable<MovementSnapshot> matched = list.Movements;
            var filtered = !string.IsNullOrWhiteSpace(location);
            if (filtered)
            {
                matched = matched.Where(m => string.Equals(m.LocationCode, location, StringComparison.OrdinalIgnoreCase));
            }

            var all = matched.ToList();
            return new HistoryResult(
                item.Sku,
                item.BaseUnit,
                [.. all.Take(limit).Select(m => new MovementRow(
                    ManilaTime.Format(m.PostedAt),
                    m.DocumentType,
                    m.DocumentId,
                    m.Direction,
                    m.LocationCode,
                    m.CounterpartLocationCode,
                    m.QuantityChange,
                    m.ValueChange,
                    m.RunningOnHand,
                    m.RunningValue))],
                filtered ? all.Count : list.Total,
                all.Count > limit || list.Total > HistoryRead);
        }
        catch (ArgumentException ex)
        {
            return Invalid(ex.Message);
        }
    }

    public async Task<object> ListDocumentsAsync(
        [Description("Receipt, Issue, Transfer, Adjustment or Reversal. Omit for all types.")] string? type = null,
        [Description("First day, Manila date yyyy-MM-dd.")] string? from = null,
        [Description("Last day, Manila date yyyy-MM-dd, inclusive.")] string? to = null,
        CancellationToken ct = default)
    {
        try
        {
            string? exactType = null;
            if (!string.IsNullOrWhiteSpace(type))
            {
                exactType = DocumentTypes.FirstOrDefault(t => string.Equals(t, type.Trim(), StringComparison.OrdinalIgnoreCase));
                if (exactType is null)
                {
                    return Invalid($"Unknown document type {type}. Use one of: {string.Join(", ", DocumentTypes)}.");
                }
            }

            DateTimeOffset? postedFrom = null;
            if (!string.IsNullOrWhiteSpace(from))
            {
                if (!TryParseDate(from, out var day))
                {
                    return Invalid($"from must be a date like 2026-11-20, not {from}.");
                }

                postedFrom = ManilaTime.StartOfDay(day);
            }

            DateTimeOffset? postedTo = null;
            if (!string.IsNullOrWhiteSpace(to))
            {
                if (!TryParseDate(to, out var day))
                {
                    return Invalid($"to must be a date like 2026-11-20, not {to}.");
                }

                postedTo = ManilaTime.StartOfDay(day.AddDays(1));
            }

            var list = await reader.ListDocumentsAsync(exactType, postedFrom, postedTo, ListCap, ct);
            return new DocumentsResult(
                [.. list.Documents.Select(d => new DocumentRow(
                    d.Id,
                    d.Type,
                    d.ReasonCode,
                    d.Reference,
                    ManilaTime.Format(d.PostedAt),
                    d.LineCount,
                    d.ReversedByDocumentId is not null,
                    d.ReversesDocumentId is not null))],
                list.Total,
                list.Total > list.Documents.Count);
        }
        catch (ArgumentException ex)
        {
            return Invalid(ex.Message);
        }
    }

    public async Task<object> GetDocumentAsync(
        [Description("The document id, a GUID from list_documents or get_item_history.")] string documentId,
        CancellationToken ct = default)
    {
        try
        {
            if (!Guid.TryParse(documentId, out var id))
            {
                return Invalid($"documentId must be a GUID, not {documentId}.");
            }

            var document = await reader.GetDocumentAsync(id, ct);
            return document is null
                ? NotFound($"No document with id {documentId}. Use list_documents.")
                : new DocumentResult(
                    document.Id,
                    document.Type,
                    document.ReasonCode,
                    document.Reference,
                    document.Notes,
                    ManilaTime.Format(document.PostedAt),
                    document.ReversesDocumentId,
                    document.ReversedByDocumentId,
                    [.. document.Lines.Select(l => new DocumentLineRow(
                        l.Sku, l.ItemName, l.FromLocationCode, l.ToLocationCode, l.Quantity, l.BaseUnit, l.UnitCost, l.TotalCost))]);
        }
        catch (ArgumentException ex)
        {
            return Invalid(ex.Message);
        }
    }

    public async Task<object> GetLedgerHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var health = await reader.GetLedgerHealthAsync(ct);
            return new HealthResult(
                health.Healthy,
                ManilaTime.Format(health.CheckedAt),
                [.. health.Invariants.Select(i => new InvariantRow(i.Id, i.Name, i.Passed, i.FailureCount))]);
        }
        catch (ArgumentException ex)
        {
            return Invalid(ex.Message);
        }
    }

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private async Task<LocationSummary?> FindPhysicalLocationAsync(string code, CancellationToken ct) =>
        (await reader.ListLocationsAsync(ct))
            .FirstOrDefault(l => l.IsPhysical && string.Equals(l.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
}