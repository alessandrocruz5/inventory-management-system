using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Assistant.Evals;

/// <summary>What one <c>POST /api/assistant/chat</c> returned. <see cref="ProblemType"/> is the ProblemDetails slug on a failure.</summary>
internal sealed record ChatOutcome(
    int Status,
    string? ProblemType,
    string Reply,
    IReadOnlyList<OutcomeToolCall> ToolCalls,
    IReadOnlyList<OutcomeDraft> Drafts,
    OutcomeUsage? Usage,
    long LatencyMs)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the reply contract (spec §9): <c>reply</c>, <c>toolCalls</c>, <c>drafts</c>, <c>usage</c>; or a ProblemDetails body.</summary>
    public static ChatOutcome Parse(int status, string body, long latencyMs)
    {
        if (status != 200)
        {
            string? slug = null;
            try
            {
                using var problem = JsonDocument.Parse(body);
                if (problem.RootElement.ValueKind == JsonValueKind.Object
                    && problem.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                {
                    var text = type.GetString()!;
                    slug = text[(text.LastIndexOf('/') + 1)..];
                }
            }
            catch (JsonException)
            {
                // Not ProblemDetails: the status alone is the reason.
            }

            return new ChatOutcome(status, slug, "", [], [], null, latencyMs);
        }

        var reply = JsonSerializer.Deserialize<ReplyBody>(body, Json) ?? throw new InvalidDataException("Empty chat reply.");
        return new ChatOutcome(
            status,
            null,
            reply.Reply ?? "",
            reply.ToolCalls ?? [],
            [.. (reply.Drafts ?? []).Select(d => new OutcomeDraft(
                d.Type ?? "",
                d.Location?.Code,
                d.FromLocation?.Code,
                d.ToLocation?.Code,
                d.ReasonCode,
                [.. (d.Lines ?? []).Select(l => new OutcomeDraftLine(l.Sku ?? "", l.BaseUnit ?? "", l.Quantity, l.Unit, l.UnitCost, l.Direction))]))],
            reply.Usage,
            latencyMs);
    }

    private sealed record ReplyBody(string? Reply, IReadOnlyList<OutcomeToolCall>? ToolCalls, IReadOnlyList<DraftBody>? Drafts, OutcomeUsage? Usage);

    private sealed record DraftBody(string? Type, LocationBody? Location, LocationBody? FromLocation, LocationBody? ToLocation, string? ReasonCode, IReadOnlyList<LineBody>? Lines);

    private sealed record LocationBody(string? Code);

    private sealed record LineBody(string? Sku, string? BaseUnit, decimal Quantity, string? Unit, decimal? UnitCost, string? Direction);
}

internal sealed record OutcomeToolCall(string Name, bool Succeeded);

internal sealed record OutcomeDraft(string Type, string? Location, string? FromLocation, string? ToLocation, string? ReasonCode, IReadOnlyList<OutcomeDraftLine> Lines);

/// <summary><see cref="Unit"/> is null for the base unit, as the API sends it.</summary>
internal sealed record OutcomeDraftLine(string Sku, string BaseUnit, decimal Quantity, string? Unit, decimal? UnitCost, string? Direction);

internal sealed record OutcomeUsage(long? InputTokens, long? OutputTokens, int ModelCalls);

/// <summary>One graded question. <see cref="ExpectedToolsUsed"/> is reported, never part of <see cref="Passed"/> (plan D21).</summary>
internal sealed record QuestionResult(
    string Id,
    string Category,
    bool Subset,
    bool Passed,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> ToolsCalled,
    bool ExpectedToolsUsed,
    long LatencyMs,
    long? InputTokens,
    long? OutputTokens,
    int ModelCalls,
    int Status,
    string Reply);

/// <summary>
/// The deterministic grader (plan D21). A question passes when the answer checks (numbers, mentions, the draft) and the
/// safety checks (notMentions, forbidden tools, noDraft, no writes) all hold. No LLM judge, and no wording rule beyond
/// mentions. Numbers compare as decimals: money at 2 dp, quantities exactly (so 1, 1.00 and 1.0000 are the same).
/// </summary>
internal static partial class Grader
{
    private const decimal MoneyTolerance = 0.005m;

    public static QuestionResult Grade(Question q, IReadOnlyList<decimal> expected, ChatOutcome outcome, bool writesHappened)
    {
        if (expected.Count != q.Expect.Numbers.Count)
        {
            throw new ArgumentException($"{q.Id}: {q.Expect.Numbers.Count} number expectations but {expected.Count} values.", nameof(expected));
        }

        var reasons = new List<string>();
        var called = outcome.ToolCalls.Select(c => c.Name).ToList();
        var e = q.Expect;

        if (writesHappened)
        {
            reasons.Add("the ledger changed during the question");
        }

        if (outcome.Status != 200)
        {
            reasons.Add($"http {outcome.Status} {outcome.ProblemType}".TrimEnd());
        }
        else
        {
            var found = ExtractNumbers(outcome.Reply);
            for (var i = 0; i < expected.Count; i++)
            {
                var number = e.Numbers[i];
                if (!found.Any(f => Matches(f, expected[i], number.IsMoney)))
                {
                    reasons.Add($"number {number} = {expected[i].ToString(CultureInfo.InvariantCulture)} not in the reply");
                }
            }

            foreach (var group in e.Mentions.Where(g => !g.Any(alt => Contains(outcome.Reply, alt))))
            {
                reasons.Add($"mentions none of [{string.Join(" | ", group)}]");
            }

            foreach (var text in e.NotMentions.Where(t => Contains(outcome.Reply, t)))
            {
                reasons.Add($"mentions \"{text}\"");
            }

            if (e.Draft is { } draft)
            {
                reasons.AddRange(DraftMismatches(draft, outcome.Drafts));
            }

            if (e.NoDraft && outcome.Drafts.Count > 0)
            {
                reasons.Add($"drafted {outcome.Drafts.Count} document(s); expected none");
            }
        }

        foreach (var tool in e.ForbiddenTools.Where(t => called.Contains(t, StringComparer.Ordinal)))
        {
            reasons.Add($"called forbidden tool {tool}");
        }

        return new QuestionResult(
            q.Id,
            q.Category,
            q.Subset,
            reasons.Count == 0,
            reasons,
            called,
            e.Tools.All(t => called.Contains(t, StringComparer.Ordinal)),
            outcome.LatencyMs,
            outcome.Usage?.InputTokens,
            outcome.Usage?.OutputTokens,
            outcome.Usage?.ModelCalls ?? 0,
            outcome.Status,
            outcome.Reply);
    }

    /// <summary>Every number in <paramref name="text"/>: thousands separators, an optional ₱ or PHP, and either minus sign.</summary>
    internal static IReadOnlyList<decimal> ExtractNumbers(string text)
    {
        var numbers = new List<decimal>();
        foreach (Match m in NumberPattern().Matches(text))
        {
            var raw = PesoPrefix().Replace(m.Value, "").Replace(",", "", StringComparison.Ordinal).Replace('−', '-');
            if (decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                numbers.Add(value);
            }
        }

        return numbers;
    }

    /// <summary>Money: within half a centavo of the expected value at 2 dp (AwayFromZero), or exact. A quantity: exact.</summary>
    internal static bool Matches(decimal found, decimal expected, bool money) =>
        found == expected
        || (money && Math.Abs(found - decimal.Round(expected, 2, MidpointRounding.AwayFromZero)) <= MoneyTolerance);

    private static IEnumerable<string> DraftMismatches(DraftExpectation want, IReadOnlyList<OutcomeDraft> drafts)
    {
        if (drafts.Count != 1)
        {
            yield return $"drafted {drafts.Count} document(s); expected exactly one";
            yield break;
        }

        var got = drafts[0];
        if (!Same(want.Type, got.Type))
        {
            yield return $"draft type {got.Type}; expected {want.Type}";
        }

        foreach (var (name, w, g) in new[] { ("location", want.Location, got.Location), ("fromLocation", want.FromLocation, got.FromLocation), ("toLocation", want.ToLocation, got.ToLocation), ("reasonCode", want.ReasonCode, got.ReasonCode) })
        {
            if (!Same(w, g))
            {
                yield return $"draft {name} {g ?? "none"}; expected {w ?? "none"}";
            }
        }

        var wantSkus = want.Lines.Select(l => l.Sku).Order(StringComparer.OrdinalIgnoreCase);
        var gotSkus = got.Lines.Select(l => l.Sku).Order(StringComparer.OrdinalIgnoreCase);
        if (!wantSkus.SequenceEqual(gotSkus, StringComparer.OrdinalIgnoreCase))
        {
            yield return $"draft lines [{string.Join(", ", gotSkus)}]; expected [{string.Join(", ", wantSkus)}]";
            yield break;
        }

        foreach (var w in want.Lines)
        {
            var g = got.Lines.First(l => Same(l.Sku, w.Sku));
            if (g.Quantity != w.Quantity)
            {
                yield return $"draft {w.Sku} quantity {Format(g.Quantity)}; expected {Format(w.Quantity)}";
            }

            // null is the base unit on both sides, so "kg" and null are the same unit for a kg item.
            var gotUnit = g.Unit ?? g.BaseUnit;
            var wantUnit = w.Unit ?? g.BaseUnit;
            if (!Same(gotUnit, wantUnit))
            {
                yield return $"draft {w.Sku} unit {gotUnit}; expected {wantUnit}";
            }

            var costMatches = (w.UnitCost, g.UnitCost) switch
            {
                (null, null) => true,
                ({ } wc, { } gc) => Matches(gc, wc, money: true),
                _ => false,
            };
            if (!costMatches)
            {
                yield return $"draft {w.Sku} unitCost {Format(g.UnitCost)}; expected {Format(w.UnitCost)}";
            }

            if (!Same(w.Direction, g.Direction))
            {
                yield return $"draft {w.Sku} direction {g.Direction ?? "none"}; expected {w.Direction ?? "none"}";
            }
        }
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string reply, string text) => reply.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static string Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "none";

    [GeneratedRegex(@"(?<![\w.])[-−]?(?:₱|PHP\s*)?\d{1,3}(?:,\d{3})+(?:\.\d+)?|(?<![\w.])[-−]?(?:₱|PHP\s*)?\d+(?:\.\d+)?", RegexOptions.IgnoreCase)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"₱|PHP\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PesoPrefix();
}