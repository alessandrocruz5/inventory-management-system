using System.Runtime.CompilerServices;
using System.Text.Json;

using Microsoft.Extensions.AI;

namespace Inventory.IntegrationTests;

/// <summary>A model stand-in that replays scripted steps in order and records what it was sent.</summary>
internal sealed class ScriptedChatClient : IChatClient
{
    internal abstract record Step;

    internal sealed record ToolCall(string Name, object? Arguments);

    internal sealed record CallTools(params ToolCall[] Calls) : Step;

    internal sealed record Reply(string Text) : Step;

    internal sealed record Fail(Exception Error) : Step;

    /// <summary>Waits first (honouring cancellation), then runs <paramref name="Then"/>.</summary>
    internal sealed record Wait(TimeSpan Delay, Step Then) : Step;

    internal sealed record Turn(IReadOnlyList<ChatMessage> Messages, IReadOnlyList<string> ToolNames);

    private readonly Step[] _steps;
    private readonly bool _repeatLast;
    private int _next;
    private int _callCounter;

    public ScriptedChatClient(bool repeatLast, params Step[] steps)
    {
        _repeatLast = repeatLast;
        _steps = steps;
    }

    public ScriptedChatClient(params Step[] steps) : this(false, steps)
    {
    }

    public List<Turn> Turns { get; } = [];

    /// <summary>Every tool result the client was sent, serialized, for assertions.</summary>
    public IEnumerable<string> ToolResultsJson() => Turns
        .SelectMany(t => t.Messages)
        .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
        .Select(r => JsonSerializer.Serialize(r.Result));

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Turns.Add(new Turn(
            [.. messages],
            [.. options?.Tools?.Select(t => t.Name) ?? []]));

        var index = _next++;
        if (index >= _steps.Length)
        {
            if (!_repeatLast || _steps.Length == 0) throw new InvalidOperationException("script exhausted");
            index = _steps.Length - 1;
        }

        return await RunAsync(_steps[index], cancellationToken);
    }

    private async Task<ChatResponse> RunAsync(Step step, CancellationToken ct)
    {
        switch (step)
        {
            case Wait wait:
                await Task.Delay(wait.Delay, ct);
                return await RunAsync(wait.Then, ct);
            case Fail fail:
                throw fail.Error;
            case Reply reply:
                return Respond(new ChatMessage(ChatRole.Assistant, reply.Text));
            case CallTools calls:
                var message = new ChatMessage(ChatRole.Assistant, []);
                foreach (var call in calls.Calls)
                {
                    message.Contents.Add(new FunctionCallContent($"call-{++_callCounter}", call.Name, ToArguments(call.Arguments)));
                }

                return Respond(message);
            default:
                throw new InvalidOperationException($"Unknown step {step.GetType().Name}");
        }
    }

    private static ChatResponse Respond(ChatMessage message) => new(message)
    {
        ModelId = "scripted",
        Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 },
    };

    private static Dictionary<string, object?> ToArguments(object? arguments)
    {
        var result = new Dictionary<string, object?>();
        if (arguments is null) return result;
        var element = JsonSerializer.SerializeToElement(arguments);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}