---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 08
unit: IMS-34
status: done
model: opus
effort: high
depends_on: [07]
parallel_group: H
files:
  create:
    - src/Modules/Assistant/Assistant/AssistantModel.cs
    - src/Modules/Assistant/Assistant/AssistantProblems.cs
    - src/Modules/Assistant/Assistant/Features/Chat/Handler.cs
    - src/Modules/Assistant/Assistant/Features/Chat/Request.cs
    - src/Modules/Assistant/Assistant/Features/Chat/Response.cs
  modify:
    - src/Modules/Assistant/Assistant/AssistantModuleExtensions.cs
    - src/ServiceDefaults/Extensions.cs
  delete: []
---
# 08 · Chat handler, model pipeline and limits

## Task
Build the trust boundary: the MEAI pipeline (tool loop, then OpenTelemetry), the turn handler (validation, limits, timeout budget, error mapping, reply and usage), the ProblemDetails helpers, and the per-user rate-limit policy. Prompt 09 maps the route and tests it end to end, so this prompt maps nothing.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Read first:
  - `src/Modules/Assistant/Assistant/{AssistantModuleExtensions,AssistantOptions}.cs`, `Tools/ToolCatalog.cs`, `Prompting/SystemPrompt.cs`.
  - `src/Shared/Kernel/Problems/ProblemTypes.cs`: `AssistantNotConfigured`, `AssistantUnavailable`, `RateLimited`, `Validation`.
  - `src/Shared/Kernel/Tenancy/ICurrentUser.cs`: `IsAuthenticated`, `UserId`, `Role`.
  - The ProblemDetails style in `src/Modules/Inventory/Inventory/Features/Problems.cs:13-20`.
  - `src/Api/Program.cs`: middleware order. `AddProblemDetails()` is registered, so `IProblemDetailsService` is available.
- Plan decisions:
  - **D12 limits** (put them in `AssistantLimits`): `MaxMessages = 20`, `MaxUserChars = 2_000`, `MaxAssistantChars = 8_000`, `MaxTotalChars = 24_000`, `MaxToolRounds = 5`, `MaxOutputTokens = 1_024`, `MaxReplyChars = MaxAssistantChars`, `MaxBodyBytes = 64 * 1024`. Temperature is 0.
  - **D12 reply cap**: `MaxOutputTokens` is only a request option. A provider can ignore it, and `ChatResponse.Text` concatenates every assistant message in the tool loop. So the handler bounds the reply itself (step 7). The cap equals `MaxAssistantChars` because the browser re-sends each reply as an `assistant` message, and `Validate` rejects one over 8,000 chars.
  - **D13**: 503 `assistant-not-configured`; 503 `assistant-unavailable` (with `Retry-After` when upstream gave one); 429 `rate-limited`.
  - **D15**: `EnableSensitiveData = false`, and no prompt or reply text in any log.
  - **D5**: stateless.
- MEAI 10.11.1 names (check them against the package and adapt; report any difference):
  - `new ChatClientBuilder(inner).UseFunctionInvocation(loggerFactory, f => { … }).UseOpenTelemetry(loggerFactory, sourceName, o => o.EnableSensitiveData = false).Build(services)`.
  - The first `Use` is outermost, so the tool loop wraps telemetry and every model call gets a span.
  - `FunctionInvokingChatClient`: `MaximumIterationsPerRequest`, `AllowConcurrentInvocation`, `IncludeDetailedErrors`, `MaximumConsecutiveErrorsPerRequest`.
  - `ChatResponse.Text`, `.Messages`, `.Usage` (`InputTokenCount`, `OutputTokenCount`), `.ModelId`.
  - `FunctionCallContent.CallId/Name/Arguments`, `FunctionResultContent.CallId/Result/Exception`.

### Files
1. `AssistantModel.cs`:
   - `internal static class AssistantTelemetry { public const string SourceName = "Assistant"; }`.
   - `internal static class AssistantLimits` (constants above), with XML docs that cite D12.
   - `internal sealed class AssistantModel`, a singleton built from `IServiceProvider` and `ILoggerFactory`:
     - `IsConfigured` is true when `GetKeyedService<IChatClient>(AssistantOptions.ModelServiceKey)` is non-null.
     - `Client` is the built pipeline, or null.
     - Function invocation: `MaximumIterationsPerRequest = AssistantLimits.MaxToolRounds`; `AllowConcurrentInvocation = false`, because the tools share one scoped DbContext, which isn't thread-safe; `IncludeDetailedErrors = false`, so no exception text goes to the model or provider; `MaximumConsecutiveErrorsPerRequest = 2`.
     - Telemetry: `sourceName: AssistantTelemetry.SourceName`, `EnableSensitiveData = false`.
2. `AssistantProblems.cs`:
   - `internal static class AssistantProblems` with `Validation(Dictionary<string, string[]>)`, `NotConfigured()` (503, title "Assistant not configured") and `Unavailable()` (503, title "Assistant unavailable"), each `ProblemHttpResult` with `type: ProblemTypes.Uri(...)`.
   - `WriteRateLimitedAsync(OnRejectedContext context, CancellationToken ct)`:
     - Sets `Retry-After` (whole seconds, rounded up) from `context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)`.
     - Writes a 429 problem through `IProblemDetailsService.WriteAsync` with `Type = ProblemTypes.Uri(ProblemTypes.RateLimited)`, `Title = "Too many requests"`.
3. `Features/Chat/Request.cs`:
   - `internal sealed record AssistantChatRequest(IReadOnlyList<AssistantChatMessage>? Messages)` and `internal sealed record AssistantChatMessage(string? Role, string? Content)`. Don't name them `ChatMessage`/`ChatResponse`: MEAI owns those names.
   - `internal static Dictionary<string, string[]> Validate(AssistantChatRequest request)`. Keys, as the spec §9 paragraph (prompt 03) states:
     - `messages`: null, empty, more than 20, or total content over 24,000 chars.
     - `messages[i].role`: not exactly `user`/`assistant` (ordinal), or the last one isn't `user`.
     - `messages[i].content`: null or blank, a user message over 2,000 chars, or an assistant message over 8,000.
4. `Features/Chat/Response.cs`:
   - `internal sealed record AssistantChatResponse(string Reply, IReadOnlyList<ToolCallSummary> ToolCalls, AssistantUsage Usage, string Model)`.
   - `ToolCallSummary(string Name, IDictionary<string, object?>? Arguments, bool Succeeded)`.
   - `AssistantUsage(long? InputTokens, long? OutputTokens, int ModelCalls)`.
5. `Features/Chat/Handler.cs`: `internal sealed class ChatHandler(AssistantModel model, ToolCatalog catalog, ICurrentUser user, TimeProvider clock, IOptions<AssistantOptions> options, ILogger<ChatHandler> logger)` with `Task<Results<Ok<AssistantChatResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(AssistantChatRequest request, HttpContext http, CancellationToken ct)`:
   1. If `!model.IsConfigured`, return `NotConfigured()`, before validating.
   2. Run `Validate`; if it has errors, return `Validation(errors)`.
   3. Messages: `new ChatMessage(ChatRole.System, SystemPrompt.Build(user.Role, clock.GetUtcNow()))`, then each request message as `ChatRole.User`/`ChatRole.Assistant` text. Roles come from the validated strings, never from anything else.
   4. `new ChatOptions { Tools = [.. catalog.For(user.Role)], Temperature = 0f, MaxOutputTokens = AssistantLimits.MaxOutputTokens, ToolMode = ChatToolMode.Auto }`.
   5. Budget: `using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.TimeoutSeconds), clock);` linked with `ct`.
   6. `await model.Client!.GetResponseAsync(messages, chatOptions, linked.Token)`:
      - `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` → log a Warning "Assistant turn exceeded {TimeoutSeconds}s" → `Unavailable()`.
      - A client abort (`ct` cancelled) propagates.
      - `catch (Exception ex) when (ex is not OperationCanceledException)` → log a Warning with `ex.GetType().Name` and, for `System.ClientModel.ClientResultException`, its `Status`. Never log `ex.Message`, which can echo request content. For a `ClientResultException` with status 429 and a `retry-after` header on `GetRawResponse()`, set `http.Response.Headers.RetryAfter`. Return `Unavailable()`.
   7. Reply: `response.Text`, minus every `<think>…</think>` block (and an unclosed leading `<think>…` up to the end), trimmed. If it's empty:
      - when the last message still holds a `FunctionCallContent` (the round cap was hit): `"I couldn't finish looking that up. Try a narrower question."`
      - otherwise: `"I don't have an answer for that."`
      - Cap (D12): declare `internal const string ReplyCutNotice = "\n\n[Reply cut off: too long.]";` on `AssistantLimits`. If the reply is longer than `AssistantLimits.MaxReplyChars`, log a Warning "Assistant reply cut from {Length} chars" (the length only, never the text). Keep the first `MaxReplyChars - ReplyCutNotice.Length` chars, dropping one more if the last kept char is a high surrogate so no pair is split. Trim the end and append `ReplyCutNotice`. The result is always ≤ `MaxReplyChars`.
   8. Tool calls: pair each `FunctionCallContent` with its `FunctionResultContent` by `CallId`, in order. `Succeeded` = a result exists, `Exception` is null, and the result (serialize it with `ToolJson.Options` if it isn't already a `JsonElement`) has no top-level `error` property.
   9. Usage: `response.Usage`. Check that the tool loop sums usage across rounds; if it doesn't, sum the per-message usage yourself. `ModelCalls` = the number of `ChatRole.Assistant` messages in `response.Messages`.
   10. Model: `options.Value.Model ?? response.ModelId ?? "unknown"`. Return `Ok`.
6. `AssistantModuleExtensions.AddAssistantModule`:
   - `AddSingleton<AssistantModel>()` and `AddScoped<ChatHandler>()`.
   - `AddRateLimiter(o => { o.RejectionStatusCode = 429; o.OnRejected = AssistantProblems.WriteRateLimitedAsync; o.AddPolicy(AssistantRateLimit.PolicyName, http => … ); })`.
   - Partition key: `ICurrentUser.UserId.ToString()` when authenticated, else `"anonymous"`.
   - `FixedWindowRateLimiterOptions { PermitLimit = options.PermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }`.
   - Declare `internal static class AssistantRateLimit { public const string PolicyName = "assistant"; }` in this file.
   - No global limiter: only endpoints that opt in are limited.
7. `src/ServiceDefaults/Extensions.cs`: `.AddMeter("Assistant")` in `WithMetrics` and `.AddSource("Assistant")` in `WithTracing`, next to `builder.Environment.ApplicationName`, with a comment "Assistant module's GenAI telemetry (plan D15)".

## Do not
- Map the route or call `UseRateLimiter` (prompt 09).
- Log or trace message content, tool arguments or tool results.
- Allow concurrent tool calls. Add streaming, retries of your own or caching. (The OpenAI SDK already retries 429/5xx inside the budget.)
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Architecture.Tests
dotnet test
```
- Report each MEAI name that differed from this prompt, and whether `Usage` was already summed across rounds.
