using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Inventory.Features.Posting;

/// <summary>
/// The idempotency <c>RequestHash</c> (spec §6 step 2): SHA-256 over UTF-8 <c>"POST {route}\n{json}"</c>, where
/// <c>json</c> is the validated request in canonical form. There are four forms: a receipt
/// (<c>locationId</c>, <c>reference</c>, <c>notes</c>, <c>lines[itemId, quantity, unitCost]</c>), an issue (the same
/// without <c>unitCost</c>), a transfer (<c>fromLocationId</c>, <c>toLocationId</c>, <c>reference</c>, <c>notes</c>,
/// <c>lines[itemId, quantity]</c>) and an adjustment (<c>locationId</c>, <c>reasonCode</c>, <c>reference</c>,
/// <c>notes</c>, <c>lines[itemId, quantityDelta, unitCost]</c>). Hidden coupling: changing a form changes every hash
/// of it, so a retry of an earlier post would get a false <c>409 idempotency-key-reused</c>.
/// </summary>
internal static class RequestHash
{
    public const string ReceiptsRoute = "/api/documents/receipts";
    public const string IssuesRoute = "/api/documents/issues";
    public const string TransfersRoute = "/api/documents/transfers";
    public const string AdjustmentsRoute = "/api/documents/adjustments";

    // Scale 28: dividing by it strips trailing zeros without changing the value.
    private const decimal One = 1.0000000000000000000000000000m;

    /// <summary>The 32-byte hash of a receipt; its lines carry <c>unitCost</c>.</summary>
    public static byte[] Receipt(ValidatedPosting posting) => Hash(ReceiptsRoute, CanonicalJson(posting, withUnitCost: true));

    /// <summary>The 32-byte hash of an issue; its lines carry no <c>unitCost</c>.</summary>
    public static byte[] Issue(ValidatedPosting posting) => Hash(IssuesRoute, CanonicalJson(posting, withUnitCost: false));

    /// <summary>The 32-byte hash of a transfer; it names both locations.</summary>
    public static byte[] Transfer(ValidatedPosting posting) => Hash(TransfersRoute, TransferCanonicalJson(posting));

    /// <summary>The 32-byte hash of an adjustment; its lines carry a signed <c>quantityDelta</c> and <c>unitCost</c>.</summary>
    public static byte[] Adjustment(ValidatedPosting posting) => Hash(AdjustmentsRoute, AdjustmentCanonicalJson(posting));

    /// <summary>
    /// No whitespace, every property always written in a fixed order, null for absent. Guids are lowercase
    /// <c>"D"</c>, strings are the normalized values, decimals are raw numbers without trailing zeros, and lines
    /// are in <c>LineNo</c> order. The writer's default encoder escapes every non-ASCII character, so the output is
    /// ASCII.
    /// </summary>
    internal static string CanonicalJson(ValidatedPosting posting, bool withUnitCost)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            json.WriteStartObject();
            json.WriteString("locationId", posting.LocationId.ToString("D"));
            WriteText(json, "reference", posting.Reference);
            WriteText(json, "notes", posting.Notes);
            json.WriteStartArray("lines");
            foreach (var line in posting.Lines.OrderBy(l => l.LineNo))
            {
                json.WriteStartObject();
                json.WriteString("itemId", line.ItemId.ToString("D"));
                WriteDecimal(json, "quantity", line.Quantity);
                if (withUnitCost)
                {
                    WriteDecimal(json, "unitCost", line.UnitCost);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The transfer form, under the same rules as <see cref="CanonicalJson"/>. <c>LocationId</c> is the source.</summary>
    internal static string TransferCanonicalJson(ValidatedPosting posting)
    {
        var to = posting.ToLocationId
            ?? throw new InvalidOperationException("A transfer posting must carry a ToLocationId.");

        return Write(json =>
        {
            json.WriteStartObject();
            json.WriteString("fromLocationId", posting.LocationId.ToString("D"));
            json.WriteString("toLocationId", to.ToString("D"));
            WriteText(json, "reference", posting.Reference);
            WriteText(json, "notes", posting.Notes);
            json.WriteStartArray("lines");
            foreach (var line in posting.Lines.OrderBy(l => l.LineNo))
            {
                json.WriteStartObject();
                json.WriteString("itemId", line.ItemId.ToString("D"));
                WriteDecimal(json, "quantity", line.Quantity);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        });
    }

    /// <summary>
    /// The adjustment form, under the same rules as <see cref="CanonicalJson"/>. <c>reasonCode</c> is the enum name
    /// and <c>quantityDelta</c> the signed <c>Quantity</c>.
    /// </summary>
    internal static string AdjustmentCanonicalJson(ValidatedPosting posting)
    {
        var reason = posting.ReasonCode
            ?? throw new InvalidOperationException("An adjustment posting must carry a ReasonCode.");

        return Write(json =>
        {
            json.WriteStartObject();
            json.WriteString("locationId", posting.LocationId.ToString("D"));
            json.WriteString("reasonCode", reason.ToString());
            WriteText(json, "reference", posting.Reference);
            WriteText(json, "notes", posting.Notes);
            json.WriteStartArray("lines");
            foreach (var line in posting.Lines.OrderBy(l => l.LineNo))
            {
                json.WriteStartObject();
                json.WriteString("itemId", line.ItemId.ToString("D"));
                WriteDecimal(json, "quantityDelta", line.Quantity);
                WriteDecimal(json, "unitCost", line.UnitCost);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        });
    }

    /// <summary>Invariant culture, trailing zeros stripped, never an exponent: <c>10.0000m</c> → <c>10</c>.</summary>
    internal static string FormatDecimal(decimal value) => (value / One).ToString(CultureInfo.InvariantCulture);

    private static byte[] Hash(string route, string json) => SHA256.HashData(Encoding.UTF8.GetBytes($"POST {route}\n{json}"));

    private static string Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            write(json);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteText(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, value);
        }
    }

    private static void WriteDecimal(Utf8JsonWriter json, string name, decimal? value)
    {
        json.WritePropertyName(name);
        if (value is { } number)
        {
            json.WriteRawValue(FormatDecimal(number));
        }
        else
        {
            json.WriteNullValue();
        }
    }
}