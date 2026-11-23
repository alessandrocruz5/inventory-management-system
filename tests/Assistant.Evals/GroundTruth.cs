using System.Globalization;
using System.Text.Json.Nodes;

namespace Assistant.Evals;

/// <summary>
/// Computes each expected number from the API's own read routes, as the demo Admin, after the scenario. Nothing is
/// hard-coded: change the seed or the scenario and the expectations follow. Manila dates use the tools' fixed +08:00.
/// </summary>
internal sealed class GroundTruth(HttpClient demoAdmin, ScenarioFacts facts)
{
    private const int PageSize = 100;

    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);

    private readonly Dictionary<string, Guid> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, JsonNode> _details = [];
    private readonly Dictionary<Guid, IReadOnlyList<JsonNode>> _movements = [];
    private Dictionary<string, Guid>? _locations;

    public async Task<decimal> ResolveAsync(NumberExpectation e, CancellationToken ct)
    {
        switch (e.Of)
        {
            case "stock.onHand":
                return Dec(await StockRowAsync(e, ct), "onHand");
            case "stock.stockValue":
                return Dec(await StockRowAsync(e, ct), "stockValue");
            case "stock.averageCost":
                return Dec(await StockRowAsync(e, ct), "averageCost");
            case "stock.lastUnitCost":
                return Dec(await StockRowAsync(e, ct), "lastUnitCost");
            case "stock.totalValue":
                {
                    var url = $"/api/stock?pageSize=1{(e.Location is null ? "" : $"&locationId={await LocationIdAsync(e.Location, ct)}")}";
                    return Dec((await GetAsync(url, ct))["totals"], "stockValue");
                }

            case "stock.onHandInUnit":
                {
                    var onHand = Dec(await StockRowAsync(e, ct), "onHand");
                    var detail = await DetailAsync(e.Sku!, ct);
                    if (string.Equals(detail["baseUnit"]!.GetValue<string>(), e.Unit, StringComparison.OrdinalIgnoreCase))
                    {
                        return onHand;
                    }

                    var unit = detail["units"]!.AsArray().FirstOrDefault(u => string.Equals(u!["code"]!.GetValue<string>(), e.Unit, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"{e.Sku} has no unit {e.Unit}.");
                    return decimal.Round(onHand / Dec(unit, "factor"), 4, MidpointRounding.AwayFromZero);
                }

            case "item.onHandTotal":
                return (await DetailAsync(e.Sku!, ct))["balances"]!.AsArray().Sum(b => Dec(b, "onHand"));
            case "document.lineCount":
                return (await DocumentAsync(e.Reference!, ct))["lines"]!.AsArray().Count;
            case "document.totalCost":
                return (await DocumentAsync(e.Reference!, ct))["lines"]!.AsArray().Sum(l => Dec(l, "totalCost"));
            case "history.count":
                return (await HistoryAsync(e, ct)).Count;
            case "history.sumQuantity":
                return (await HistoryAsync(e, ct)).Sum(m => Math.Abs(Dec(m, "quantityChange")));
            case "literal":
                return e.Value ?? throw new InvalidOperationException("literal needs a value.");
            default:
                throw new InvalidOperationException($"Unknown resolver {e.Of}.");
        }
    }

    /// <summary>Checks that a draft expectation names SKUs, units and location codes the scenario has.</summary>
    public async Task CheckDraftAsync(DraftExpectation draft, CancellationToken ct)
    {
        foreach (var code in new[] { draft.Location, draft.FromLocation, draft.ToLocation }.OfType<string>())
        {
            await LocationIdAsync(code, ct);
        }

        foreach (var line in draft.Lines)
        {
            var detail = await DetailAsync(line.Sku, ct);
            if (line.Unit is not null
                && !string.Equals(detail["baseUnit"]!.GetValue<string>(), line.Unit, StringComparison.OrdinalIgnoreCase)
                && !detail["units"]!.AsArray().Any(u => string.Equals(u!["code"]!.GetValue<string>(), line.Unit, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"{line.Sku} has no unit {line.Unit}.");
            }
        }
    }

    private async Task<JsonNode> StockRowAsync(NumberExpectation e, CancellationToken ct)
    {
        var url = $"/api/stock?itemId={await ItemIdAsync(e.Sku!, ct)}&locationId={await LocationIdAsync(e.Location!, ct)}&pageSize=1";
        return (await GetAsync(url, ct))["items"]!.AsArray().SingleOrDefault()
            ?? throw new InvalidOperationException($"No stock row for {e.Sku} at {e.Location}.");
    }

    /// <summary>Every movement of the item (each transfer has an In and an Out row), filtered by location, direction and Manila dates.</summary>
    private async Task<IReadOnlyList<JsonNode>> HistoryAsync(NumberExpectation e, CancellationToken ct)
    {
        var from = e.From is null ? DateOnly.MinValue : DateOnly.ParseExact(e.From, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = e.To is null ? DateOnly.MaxValue : DateOnly.ParseExact(e.To, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (e.Location is not null)
        {
            await LocationIdAsync(e.Location, ct);
        }

        return [.. (await MovementsAsync(e.Sku!, ct)).Where(m =>
        {
            var day = DateOnly.FromDateTime(m["postedAt"]!.GetValue<DateTimeOffset>().ToOffset(Manila).DateTime);
            return day >= from && day <= to
                && (e.Location is null || string.Equals(m["locationCode"]!.GetValue<string>(), e.Location, StringComparison.OrdinalIgnoreCase))
                && (e.Direction is null || string.Equals(m["direction"]!.GetValue<string>(), e.Direction, StringComparison.OrdinalIgnoreCase));
        })];
    }

    private async Task<IReadOnlyList<JsonNode>> MovementsAsync(string sku, CancellationToken ct)
    {
        var id = await ItemIdAsync(sku, ct);
        if (_movements.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var all = new List<JsonNode>();
        for (var page = 1; ; page++)
        {
            var body = await GetAsync($"/api/items/{id}/movements?page={page}&pageSize={PageSize}", ct);
            var items = body["items"]!.AsArray();
            all.AddRange(items.Select(i => i!));
            if (items.Count == 0 || all.Count >= body["total"]!.GetValue<int>())
            {
                break;
            }
        }

        _movements[id] = all;
        return all;
    }

    private async Task<JsonNode> DocumentAsync(string reference, CancellationToken ct) =>
        facts.DocumentsByReference.TryGetValue(reference, out var id)
            ? await GetAsync($"/api/documents/{id}", ct)
            : throw new InvalidOperationException($"No scenario document {reference}.");

    private async Task<JsonNode> DetailAsync(string sku, CancellationToken ct)
    {
        var id = await ItemIdAsync(sku, ct);
        if (!_details.TryGetValue(id, out var detail))
        {
            detail = await GetAsync($"/api/items/{id}", ct);
            _details[id] = detail;
        }

        return detail;
    }

    private async Task<Guid> ItemIdAsync(string sku, CancellationToken ct)
    {
        if (_items.TryGetValue(sku, out var id))
        {
            return id;
        }

        var page = await GetAsync($"/api/items?search={Uri.EscapeDataString(sku)}&pageSize={PageSize}", ct);
        var match = page["items"]!.AsArray().FirstOrDefault(i => string.Equals(i!["sku"]!.GetValue<string>(), sku, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No item {sku}.");
        id = match["id"]!.GetValue<Guid>();
        _items[sku] = id;
        return id;
    }

    private async Task<Guid> LocationIdAsync(string code, CancellationToken ct)
    {
        _locations ??= (await GetAsync($"/api/locations?pageSize={PageSize}", ct))["items"]!.AsArray()
            .ToDictionary(l => l!["code"]!.GetValue<string>(), l => l!["id"]!.GetValue<Guid>(), StringComparer.OrdinalIgnoreCase);
        return _locations.TryGetValue(code, out var id) ? id : throw new InvalidOperationException($"No location {code}.");
    }

    private async Task<JsonNode> GetAsync(string url, CancellationToken ct)
    {
        using var response = await demoAdmin.GetAsync(url, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GET {url}: {(int)response.StatusCode} {text}");
        }

        return JsonNode.Parse(text) ?? throw new InvalidOperationException($"GET {url}: no body.");
    }

    private static decimal Dec(JsonNode? node, string name) =>
        node?[name]?.GetValue<decimal>() ?? throw new InvalidOperationException($"{name} is null or missing.");
}