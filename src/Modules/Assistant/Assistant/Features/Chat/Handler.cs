using System.ClientModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Assistant.Drafts;
using Assistant.Prompting;
using Assistant.Tools;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Features.Chat;

/// <summary>
/// One assistant turn: the trust boundary between untrusted model output and the caller's tenant. The model sees only
/// the role-gated read tools (plan D11), works inside a time budget (D12), and its reply is bounded here (D12). Logs
/// carry types, statuses and lengths, never message, argument or result text (D15).
/// </summary>
internal sealed class ChatHandler(
    AssistantModel model,
    ToolCatalog catalog,
    DraftCollector drafts,
    ICurrentUser user,
    TimeProvider clock,
    IOptions<AssistantOptions> options,
    ILogger<ChatHandler> logger)
{
    internal const string RoundCapReply = "I couldn't finish looking that up. Try a narrower question.";
    internal const string EmptyReply = "I don't have an answer for that.";

    public async Task<Results<Ok<AssistantChatResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        AssistantChatRequest request, HttpContext http, CancellationToken ct)
    {
        if (!model.IsConfigured)
        {
            return AssistantProblems.NotConfigured();
        }

        var errors = ChatRequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return AssistantProblems.Validation(errors);
        }

        var messages = new List<ChatMessage>(request.Messages!.Count + 1)
        {
            new(ChatRole.System, SystemPrompt.Build(user.Role, clock.GetUtcNow())),
        };
        foreach (var message in request.Messages!)
        {
            var role = string.Equals(message!.Role, ChatRequestValidation.UserRole, StringComparison.Ordinal)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new ChatMessage(role, message.Content));
        }

        var tools = catalog.For(user.Role);
        var chatOptions = new ChatOptions
        {
            Tools = [.. tools],
            Temperature = 0f,
            MaxOutputTokens = AssistantLimits.MaxOutputTokens,
            ToolMode = ChatToolMode.Auto,
        };

        var timeoutSeconds = options.Value.TimeoutSeconds;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        ChatResponse response;
        try
        {
            response = await model.Client!.GetResponseAsync(messages, chatOptions, linked.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The client is still there, so this is the budget (or a transport timeout inside it), not an abort.
            if (budget.IsCancellationRequested)
            {
                logger.LogWarning("Assistant turn exceeded {TimeoutSeconds}s", timeoutSeconds);
            }
            else
            {
                logger.LogWarning("Assistant model call failed: {ExceptionType}", ex.GetType().Name);
            }

            return AssistantProblems.Unavailable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never log ex.Message: provider errors can echo request content.
            if (ex is ClientResultException upstream)
            {
                logger.LogWarning(
                    "Assistant model call failed: {ExceptionType} {Status}", ex.GetType().Name, upstream.Status);
                if (upstream.Status == StatusCodes.Status429TooManyRequests
                    && upstream.GetRawResponse()?.Headers.TryGetValue("retry-after", out var retryAfter) == true
                    && RetryConditionHeaderValue.TryParse(retryAfter, out var parsed))
                {
                    http.Response.Headers.RetryAfter = parsed.ToString();
                }
            }
            else
            {
                logger.LogWarning("Assistant model call failed: {ExceptionType}", ex.GetType().Name);
            }

            return AssistantProblems.Unavailable();
        }

        var reply = BuildReply(response);
        var offered = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        // FunctionInvokingChatClient already sums Usage over its rounds.
        var usage = new AssistantUsage(
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount,
            response.Messages.Count(m => m.Role == ChatRole.Assistant));

        return TypedResults.Ok(new AssistantChatResponse(
            reply,
            SummarizeToolCalls(response, offered),
            [.. drafts.Drafts],
            usage,
            options.Value.Model ?? response.ModelId ?? "unknown"));
    }

    private string BuildReply(ChatResponse response)
    {
        var reply = StripThinking(response.Text).Trim();

        if (reply.Length == 0)
        {
            // A trailing tool call means the round cap stopped the loop before the model answered.
            var stoppedAtCap = response.Messages.LastOrDefault()?.Contents.OfType<FunctionCallContent>().Any() == true;
            return stoppedAtCap ? RoundCapReply : EmptyReply;
        }

        if (reply.Length > AssistantLimits.MaxReplyChars)
        {
            logger.LogWarning("Assistant reply cut from {Length} chars", reply.Length);
            var keep = AssistantLimits.MaxReplyChars - AssistantLimits.ReplyCutNotice.Length;
            if (char.IsHighSurrogate(reply[keep - 1]))
            {
                keep--;
            }

            reply = reply[..keep].TrimEnd() + AssistantLimits.ReplyCutNotice;
        }

        return reply;
    }

    /// <summary>Removes every <c>&lt;think&gt;…&lt;/think&gt;</c> block, and an unclosed one up to the end. Linear time.</summary>
    internal static string StripThinking(string text)
    {
        const string open = "<think>";
        const string close = "</think>";

        var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        var position = 0;
        while (start >= 0)
        {
            result.Append(text, position, start - position);
            var end = text.IndexOf(close, start + open.Length, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return result.ToString();
            }

            position = end + close.Length;
            start = text.IndexOf(open, position, StringComparison.OrdinalIgnoreCase);
        }

        result.Append(text, position, text.Length - position);
        return result.ToString();
    }

    private static List<ToolCallSummary> SummarizeToolCalls(ChatResponse response, HashSet<string> offered)
    {
        var contents = response.Messages.SelectMany(m => m.Contents).ToList();

        var results = new Dictionary<string, FunctionResultContent>(StringComparer.Ordinal);
        foreach (var result in contents.OfType<FunctionResultContent>())
        {
            results.TryAdd(result.CallId, result);
        }

        return
        [
            .. contents.OfType<FunctionCallContent>().Select(call => new ToolCallSummary(
                call.Name,
                call.Arguments,
                offered.Contains(call.Name)
                    && results.TryGetValue(call.CallId, out var result)
                    && Succeeded(result))),
        ];
    }

    /// <summary>No exception, no top-level <c>error</c> property (a <see cref="ToolError"/>), and no <c>ok: false</c> (a refused draft).</summary>
    private static bool Succeeded(FunctionResultContent result)
    {
        if (result.Exception is not null)
        {
            return false;
        }

        var element = result.Result is JsonElement json
            ? json
            : JsonSerializer.SerializeToElement(result.Result, ToolJson.Options);

        if (element.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        return !element.TryGetProperty("error", out _)
            && !(element.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False);
    }
}