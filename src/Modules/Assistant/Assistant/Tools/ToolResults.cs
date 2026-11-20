using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Assistant.Tools;

/// <summary>How tool results are serialized for the model: camelCase, nulls omitted (every token is re-sent each round).</summary>
internal static class ToolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Manila time is a fixed +08:00 (no DST). A fixed offset avoids <c>TimeZoneInfo</c>: chiseled images may lack tzdata.</summary>
internal static class ManilaTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    public static string Format(DateTimeOffset value) =>
        value.ToOffset(Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Midnight Manila time at the start of <paramref name="date"/>.</summary>
    public static DateTimeOffset StartOfDay(DateOnly date) =>
        new(date.Year, date.Month, date.Day, 0, 0, 0, Offset);
}

/// <summary><see cref="Error"/> is <c>not-found</c> or <c>invalid-argument</c>.</summary>
internal sealed record ToolError(string Error, string Message);

internal sealed record ItemsResult(IReadOnlyList<ItemRow> Items, int Total, bool Truncated);

internal sealed record ItemRow(string Sku, string Name, string BaseUnit, bool Active);

internal sealed record ItemResult(
    string Sku,
    string Name,
    string BaseUnit,
    bool Active,
    IReadOnlyList<UnitRow> Units,
    IReadOnlyList<BalanceRow> Balances);

internal sealed record UnitRow(string Code, decimal Factor, bool Active);

internal sealed record BalanceRow(string Location, decimal OnHand, decimal StockValue, decimal? AverageCost, decimal? LastUnitCost);

internal sealed record LocationsResult(IReadOnlyList<LocationRow> Locations);

internal sealed record LocationRow(string Code, string Name, bool Active, bool AllowNegativeStock);

internal sealed record StockResult(IReadOnlyList<StockRow> Rows, decimal TotalValue, int Total, bool Truncated);

internal sealed record StockRow(string Sku, string Name, string Location, decimal OnHand, string Unit, decimal StockValue, decimal? AverageCost);

internal sealed record HistoryResult(string Sku, string BaseUnit, IReadOnlyList<MovementRow> Movements, int Total, bool Truncated);

internal sealed record MovementRow(
    string PostedAt,
    string DocumentType,
    Guid DocumentId,
    string Direction,
    string Location,
    string Counterpart,
    decimal Quantity,
    decimal Value,
    decimal OnHandAfter,
    decimal ValueAfter);

internal sealed record DocumentsResult(IReadOnlyList<DocumentRow> Documents, int Total, bool Truncated);

internal sealed record DocumentRow(
    Guid DocumentId,
    string Type,
    string? ReasonCode,
    string? Reference,
    string PostedAt,
    int Lines,
    bool Reversed,
    bool IsReversal);

internal sealed record DocumentResult(
    Guid DocumentId,
    string Type,
    string? ReasonCode,
    string? Reference,
    string? Notes,
    string PostedAt,
    Guid? ReversesDocumentId,
    Guid? ReversedByDocumentId,
    IReadOnlyList<DocumentLineRow> Lines);

internal sealed record DocumentLineRow(string Sku, string Name, string From, string To, decimal Quantity, string Unit, decimal? UnitCost, decimal TotalCost);

internal sealed record HealthResult(bool Healthy, string CheckedAt, IReadOnlyList<InvariantRow> Invariants);

internal sealed record InvariantRow(string Id, string Name, bool Passed, int Failures);