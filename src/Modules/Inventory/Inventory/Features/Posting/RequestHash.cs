using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Inventory.Features.Posting;

/// <summary>
/// The idempotency <c>RequestHash</c> (spec §6 step 2): SHA-256 over UTF-8 <c>"POST {route}\n{json}"</c>, where
/// <c>json</c> is the validated request in canonical form. Hidden coupling: changing this form changes every hash,
/// so a retry of an earlier post would get a false <c>409 idempotency-key-reused</c>.
/// </summary>
internal static class RequestHash
{
    public const string ReceiptsRoute = "/api/documents/receipts";
    public const string IssuesRoute = "/api/documents/issues";

    // Scale 28: dividing by it strips trailing zeros without changing the value.
    private const decimal One = 1.0000000000000000000000000000m;

    /// <summary>The 32-byte hash of a receipt; its lines carry <c>unitCost</c>.</summary>
    public static byte[] Receipt(ValidatedPosting posting) => Hash(ReceiptsRoute, CanonicalJson(posting, withUnitCost: true));

    /// <summary>The 32-byte hash of an issue; its lines carry no <c>unitCost</c>.</summary>
    public static byte[] Issue(ValidatedPosting posting) => Hash(IssuesRoute, CanonicalJson(posting, withUnitCost: false));

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

    /// <summary>Invariant culture, trailing zeros stripped, never an exponent: <c>10.0000m</c> → <c>10</c>.</summary>
    internal static string FormatDecimal(decimal value) => (value / One).ToString(CultureInfo.InvariantCulture);

    private static byte[] Hash(string route, string json) => SHA256.HashData(Encoding.UTF8.GetBytes($"POST {route}\n{json}"));

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