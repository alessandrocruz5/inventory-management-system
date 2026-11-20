using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Assistant;

internal static class AssistantTelemetry
{
    /// <summary>The <c>ActivitySource</c> and <c>Meter</c> name of the assistant's GenAI telemetry (plan D15).</summary>
    public const string SourceName = "Assistant";
}

/// <summary>The per-turn limits (plan D12). Spec §9 "Assistant bodies" states the same numbers.</summary>
internal static class AssistantLimits
{
    /// <summary>D12: at most 20 messages of history per turn.</summary>
    public const int MaxMessages = 20;

    /// <summary>D12: a user message is 1–2,000 chars.</summary>
    public const int MaxUserChars = 2_000;

    /// <summary>D12: an assistant message (a reply sent back as history) is at most 8,000 chars.</summary>
    public const int MaxAssistantChars = 8_000;

    /// <summary>D12: at most 24,000 chars of content across the history.</summary>
    public const int MaxTotalChars = 24_000;

    /// <summary>
    /// D12: at most 5 rounds of tool calls per turn (MEAI's <c>MaximumIterationsPerRequest</c>). The model is called at
    /// most once more after the last round, so a turn makes at most 6 model calls.
    /// </summary>
    public const int MaxToolRounds = 5;

    /// <summary>D12: the provider-side output limit. It doesn't bound the reply on its own; see <see cref="MaxReplyChars"/>.</summary>
    public const int MaxOutputTokens = 1_024;

    /// <summary>
    /// D12: the handler cuts a longer reply. It equals <see cref="MaxAssistantChars"/>, so every reply fits back into
    /// the next turn's history.
    /// </summary>
    public const int MaxReplyChars = MaxAssistantChars;

    /// <summary>D12: the request body limit.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>D12: ends a reply that was cut at <see cref="MaxReplyChars"/>.</summary>
    internal const string ReplyCutNotice = "\n\n[Reply cut off: too long.]";
}

/// <summary>
/// The model pipeline: the tool loop outermost, then OpenTelemetry, so every model call gets its own span (plan D15).
/// <see cref="Client"/> is null when no model is configured (plan D14).
/// </summary>
internal sealed class AssistantModel
{
    public AssistantModel(IServiceProvider services, ILoggerFactory loggerFactory)
    {
        var inner = services.GetKeyedService<IChatClient>(AssistantOptions.ModelServiceKey);
        if (inner is null)
        {
            return;
        }

        Client = new ChatClientBuilder(inner)
            .UseFunctionInvocation(loggerFactory, f =>
            {
                f.MaximumIterationsPerRequest = AssistantLimits.MaxToolRounds;
                // The tools share one scoped DbContext, which isn't thread-safe.
                f.AllowConcurrentInvocation = false;
                // No exception text goes to the model or the provider.
                f.IncludeDetailedErrors = false;
                f.MaximumConsecutiveErrorsPerRequest = 2;
            })
            .UseOpenTelemetry(loggerFactory, AssistantTelemetry.SourceName, o => o.EnableSensitiveData = false)
            .Build(services);
    }

    public bool IsConfigured => Client is not null;

    public IChatClient? Client { get; }
}