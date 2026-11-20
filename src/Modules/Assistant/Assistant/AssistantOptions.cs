namespace Assistant;

/// <summary>
/// The <c>Assistant</c> configuration section. The deployed stack (AppHost, compose, Bicep) sets none of these,
/// so the assistant is unconfigured there and answers <c>assistant-not-configured</c> (plan D14).
/// </summary>
internal sealed class AssistantOptions
{
    public const string Section = "Assistant";

    /// <summary>Service key of the model <c>IChatClient</c>; absent when no model is configured.</summary>
    internal const string ModelServiceKey = "assistant-model";

    /// <summary>Base URL of an OpenAI-compatible endpoint, such as local Ollama.</summary>
    public string? Endpoint { get; set; }

    public string? Model { get; set; }

    /// <summary>Never logged or returned.</summary>
    public string? ApiKey { get; set; }

    public int PermitsPerMinute { get; set; } = 10;

    /// <summary>Per-turn budget. The default sits below the nginx 120 s read timeout (<c>web/nginx.conf.template</c>).</summary>
    public int TimeoutSeconds { get; set; } = 90;
}