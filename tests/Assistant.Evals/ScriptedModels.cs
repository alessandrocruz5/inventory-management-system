using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.AI;

namespace Assistant.Evals;

/// <summary>How a calibration row answers: right (oracle), slightly wrong (near-miss), or not at all (silent). Plan D17.</summary>
internal enum ScriptedMode
{
    Oracle,
    NearMiss,
    Silent,
}

/// <summary>
/// A scripted model for the calibration rows. It is the keyed inner client, so the app's own <c>FunctionInvokingChatClient</c>
/// runs the real tools against the scenario database. It finds the question by the last user message, makes the oracle's
/// tool calls in one round, then answers from the expected values.
/// </summary>
internal sealed class GoldenScriptedClient(GoldenSet set, IReadOnlyDictionary<string, IReadOnlyList<decimal>> expected, ScriptedMode mode) : IChatClient
{
    private const string Refusal = "I can't help with that.";

    private readonly Dictionary<string, (Question Question, int Index)> _byAsk =
        set.Questions.Select((q, i) => (q, i)).ToDictionary(x => x.q.Ask, x => (x.q, x.i), StringComparer.Ordinal);

    private int _callCounter;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var all = messages.ToList();
        var ask = all.LastOrDefault(m => m.Role == ChatRole.User)?.Text
            ?? throw new InvalidOperationException("The scripted model was sent no user message.");
        if (!_byAsk.TryGetValue(ask, out var entry))
        {
            throw new InvalidOperationException($"No golden question asks: {ask}");
        }

        var (question, index) = entry;
        var haveResults = all.Any(m => m.Contents.OfType<FunctionResultContent>().Any());
        if (!haveResults && mode != ScriptedMode.Silent && question.Oracle.Calls.Count > 0)
        {
            var message = new ChatMessage(ChatRole.Assistant, []);
            foreach (var call in question.Oracle.Calls)
            {
                message.Contents.Add(new FunctionCallContent($"call-{Interlocked.Increment(ref _callCounter)}", call.Tool, ToArguments(call)));
            }

            return Task.FromResult(Respond(message));
        }

        return Task.FromResult(Respond(new ChatMessage(ChatRole.Assistant, FinalText(question, index))));
    }

    private ChatResponse Respond(ChatMessage message) => new(message)
    {
        ModelId = mode switch
        {
            ScriptedMode.Oracle => "scripted-oracle",
            ScriptedMode.NearMiss => "scripted-near-miss",
            _ => "scripted-silent",
        },
        Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 },
    };

    private string FinalText(Question q, int index)
    {
        if (mode == ScriptedMode.Silent)
        {
            return Refusal;
        }

        var nearMiss = mode == ScriptedMode.NearMiss;
        var numbers = expected[q.Id]
            .Select((value, i) => Format(nearMiss ? value + (q.Expect.Numbers[i].IsMoney ? 1.00m : 1m) : value, (index + i) % 4));
        var mentions = q.Expect.Mentions.Select(group => nearMiss ? "ZZZ" : group[0]);

        var parts = new List<string>();
        if (q.Expect.Numbers.Count > 0)
        {
            parts.Add(string.Join("; ", numbers));
        }

        parts.AddRange(mentions);
        if (parts.Count > 0)
        {
            return string.Join(". ", parts) + ".";
        }

        return q.Expect.Draft is not null ? "I've prepared that draft for you to review." : Refusal;
    }

    /// <summary>Four styles, rotated by question index, so the grader's parser sees each: 1234.5, 1,234.50, ₱1,234.50, PHP 1234.5000.</summary>
    internal static string Format(decimal value, int style)
    {
        var sign = value < 0 ? "-" : "";
        var abs = Math.Abs(value);
        return style switch
        {
            0 => sign + abs.ToString("0.####", CultureInfo.InvariantCulture),
            1 => sign + abs.ToString("#,##0.00##", CultureInfo.InvariantCulture),
            2 => sign + "₱" + abs.ToString("#,##0.00##", CultureInfo.InvariantCulture),
            _ => sign + "PHP " + abs.ToString("0.0000", CultureInfo.InvariantCulture),
        };
    }

    private Dictionary<string, object?> ToArguments(OracleCall call)
    {
        var args = new Dictionary<string, object?>();
        foreach (var (name, value) in call.Args ?? new Dictionary<string, JsonElement>())
        {
            args[name] = mode == ScriptedMode.NearMiss && call.Tool == "draft_document" && name == "lines"
                ? AddOneToQuantities(value)
                : value.Clone();
        }

        return args;
    }

    private static JsonElement AddOneToQuantities(JsonElement lines)
    {
        var array = JsonNode.Parse(lines.GetRawText())!.AsArray();
        foreach (var line in array.OfType<JsonObject>())
        {
            if (line["quantity"] is { } quantity)
            {
                line["quantity"] = JsonValue.Create(quantity.GetValue<decimal>() + 1m);
            }
        }

        return JsonSerializer.SerializeToElement(array);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}