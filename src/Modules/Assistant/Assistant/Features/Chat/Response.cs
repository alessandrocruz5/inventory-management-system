namespace Assistant.Features.Chat;

/// <summary>One turn's answer. <see cref="Reply"/> is plain text of at most 8,000 chars (plan D12, D20).</summary>
internal sealed record AssistantChatResponse(
    string Reply,
    IReadOnlyList<ToolCallSummary> ToolCalls,
    AssistantUsage Usage,
    string Model);

/// <summary>A tool the model called this turn, in order. <see cref="Succeeded"/> is false for an unknown tool or a tool error.</summary>
internal sealed record ToolCallSummary(string Name, IDictionary<string, object?>? Arguments, bool Succeeded);

/// <summary>Token usage summed over every model call of the turn (plan R4); null when the provider reports none.</summary>
internal sealed record AssistantUsage(long? InputTokens, long? OutputTokens, int ModelCalls);