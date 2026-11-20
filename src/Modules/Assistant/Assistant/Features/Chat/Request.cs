namespace Assistant.Features.Chat;

/// <summary>One stateless turn: the capped history, ending with the user's message (plan D5, D12).</summary>
internal sealed record AssistantChatRequest(IReadOnlyList<AssistantChatMessage?>? Messages);

/// <summary><see cref="Role"/> is <c>user</c> or <c>assistant</c>, exactly.</summary>
internal sealed record AssistantChatMessage(string? Role, string? Content);

internal static class ChatRequestValidation
{
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";

    /// <summary>
    /// The D12 history checks, keyed as spec §9 "Assistant bodies" states: <c>messages</c>,
    /// <c>messages[i].role</c> and <c>messages[i].content</c>. An empty result means the request is valid.
    /// </summary>
    public static Dictionary<string, string[]> Validate(AssistantChatRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var messages = request.Messages;

        if (messages is null || messages.Count == 0)
        {
            errors["messages"] = ["Send at least one message."];
            return errors;
        }

        if (messages.Count > AssistantLimits.MaxMessages)
        {
            errors["messages"] = [$"Send at most {AssistantLimits.MaxMessages} messages."];
            return errors;
        }

        long total = 0;
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            var path = $"messages[{i}]";

            if (message is null)
            {
                errors[$"{path}.content"] = ["Message is required."];
                continue;
            }

            var isUser = string.Equals(message.Role, UserRole, StringComparison.Ordinal);
            var isAssistant = string.Equals(message.Role, AssistantRole, StringComparison.Ordinal);

            if (!isUser && !isAssistant)
            {
                errors[$"{path}.role"] = ["Role must be \"user\" or \"assistant\"."];
            }
            else if (i == messages.Count - 1 && !isUser)
            {
                errors[$"{path}.role"] = ["The last message must be from the user."];
            }

            var content = message.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                errors[$"{path}.content"] = ["Content is required."];
                continue;
            }

            total += content.Length;
            if (isUser && content.Length > AssistantLimits.MaxUserChars)
            {
                errors[$"{path}.content"] = [$"A message can be at most {AssistantLimits.MaxUserChars} characters."];
            }
            else if (isAssistant && content.Length > AssistantLimits.MaxAssistantChars)
            {
                errors[$"{path}.content"] = [$"A reply can be at most {AssistantLimits.MaxAssistantChars} characters."];
            }
        }

        if (total > AssistantLimits.MaxTotalChars && !errors.ContainsKey("messages"))
        {
            errors["messages"] = [$"The conversation can be at most {AssistantLimits.MaxTotalChars} characters."];
        }

        return errors;
    }
}