---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 09
unit: IMS-34
status: done
model: sonnet
effort: high
depends_on: [08]
parallel_group: I
files:
  create:
    - src/Modules/Assistant/Assistant/Features/Chat/Endpoint.cs
    - tests/Inventory.IntegrationTests/AssistantSecurityTests.cs
  modify:
    - src/Modules/Assistant/Assistant/AssistantModuleExtensions.cs
    - src/Api/Program.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
    - tests/Inventory.IntegrationTests/AssistantTests.cs
  delete: []
---
# 09 · Chat route, rate limit, T5 probe and hardening tests (T19)

## Task
Map `POST /api/assistant/chat` with the rate-limit policy and a body-size limit, turn on the rate limiter, add the route's T5 probe, and prove each R3 control with integration tests that use the scripted model.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Read first:
  - `src/Modules/Assistant/Assistant/Features/Chat/{Handler,Request,Response}.cs`, `AssistantModel.cs` (`AssistantLimits`), `AssistantProblems.cs`, `AssistantModuleExtensions.cs` (`AssistantRateLimit.PolicyName`), `Tools/ToolCatalog.cs`.
  - The test plumbing: `tests/Inventory.IntegrationTests/ScriptedChatClient.cs`, `AssistantTests.cs` (`AssistantTests.CreateApp(db, model, settings)`), `ApiFactory.cs` (`ChatModel`, `Clock`), `DatabaseFixture.cs:62-84`, `PostingTests.cs` helpers.
- T5 rules (TASKS.md Downstream IMS-4): the probe is a `[Fact]` with `[ProbesRoute("POST /api/assistant/chat")]` in `TenantIsolationTests`. The chat body carries no ids, so foreign ids can only reach the tools through the model; the probe scripts exactly that.
- Every test makes its own tenants. Never contact a real model: every app that chats gets `ChatModel`, or `Assistant:Endpoint` blank.

### Steps
1. `Features/Chat/Endpoint.cs`: `internal static class PostChatEndpoint { MapPostChat(this RouteGroupBuilder assistant) }` → `MapPost("/chat", (AssistantChatRequest request, ChatHandler handler, HttpContext http, CancellationToken ct) => handler.HandleAsync(request, http, ct))`, `.RequireRateLimiting(AssistantRateLimit.PolicyName)` and `.WithMetadata(new RequestSizeLimitAttribute(AssistantLimits.MaxBodyBytes))`.
2. `AssistantModuleExtensions.MapAssistantEndpoints`: add `assistant.MapPostChat();`.
3. `src/Api/Program.cs`: `app.UseRateLimiter();` right after `app.UseAuthorization();`. Comment: after authorization, so the partition is the signed-in user (plan D12).
4. `TenantIsolationTests.cs`: `[Fact][ProbesRoute("POST /api/assistant/chat")] Assistant_tools_read_only_the_callers_tenant`.
   - Tenants A and B, both Admin. A has `SAME-1` with 5 on hand. B has `SAME-1` with 9 on hand, plus `B-ONLY-<8 hex>` named `B secret item` and a receipt with reference `B-SECRET-REF`.
   - A's app, with a script that calls in one round: `get_stock {sku: B-ONLY…}`, `get_item {sku: B-ONLY…}`, `get_document {documentId: B's doc id}`, `get_item_history {sku: B-ONLY…}`, `get_stock {sku: "SAME-1"}`; then `Reply("done")`.
   - Assert: 200; the first four tool results are `not-found`; the last shows `onHand` 5 only; `ToolResultsJson()` contains neither `B secret item`, `B-SECRET-REF`, nor `9`-quantity rows.
5. `AssistantTests.cs`, add:
   - `Chat_answers_through_a_tool`: script `CallTools(get_stock{sku})`, then `Reply("You have 5.")`. Assert 200; `reply == "You have 5."`; `toolCalls == [{ name: get_stock, succeeded: true }]`; `usage` 200/20 with `modelCalls` 2; `model == "scripted"`. The second recorded turn holds the tool result with `onHand` 5.
   - `Chat_is_unavailable_when_not_configured`: `Assistant:Endpoint` blank → `503`, `type /problems/assistant-not-configured`.
   - `[Theory] Chat_validates_the_history`, one row each, asserting `400 validation` and the error key: empty; 21 messages; role `system`; role `tool`; last is `assistant`; a user message of 2,001 chars; an assistant message of 8,001; total > 24,000; blank content.
   - `Chat_strips_think_blocks`: `Reply("<think>secret plan</think>Rice: 120 kg")` → `reply == "Rice: 120 kg"`.
   - `Chat_requires_sign_in` → 401.
6. `AssistantSecurityTests.cs` (class `AssistantSecurityTests(DatabaseFixture db)`; XML summary "T19: the assistant's hardening controls (plan R3)"):
   1. `Staff_is_never_offered_ledger_health`: Staff's first turn's `ToolNames` lacks `get_ledger_health`; Admin's has it. A scripted Staff call to `get_ledger_health` ends with no `invariants` in any tool result.
   2. `Unknown_and_write_tools_change_nothing`: a script calls `post_receipt`, `reverse_document` and `delete_item` with plausible args, then replies. `GET /api/documents` `total` and `GET /api/stock` are unchanged.
   3. `Tool_results_carry_no_user_identity`: after the user's own receipt and issue, the script calls `get_document`, `get_item_history` and `list_documents`. `ToolResultsJson()` contains no `postedBy`, not the user's email, no `@`, and not the user's id (`GET /api/auth/me`).
   4. `System_prompt_has_role_and_date_not_identity`: `Clock = FakeTimeProvider(2026-11-19T17:00Z)`. The system message contains `2026-11-20` and `an Admin`, not the email.
   5. `Tool_rounds_are_capped`: `new ScriptedChatClient(repeatLast: true, new CallTools(get_stock …))` → 200 with the fallback reply. `Turns.Count <= AssistantLimits.MaxToolRounds + 1`; record the exact number observed in a comment.
   6. `A_slow_model_is_cut_off`: `("Assistant:TimeoutSeconds", "1")` with `Wait(10 s, Reply)` → 503 `assistant-unavailable` in under 5 s (Stopwatch).
   7. `A_failing_model_leaks_nothing`: `Fail(new HttpRequestException("connection refused to 10.0.0.7"))` → 503 `assistant-unavailable`; the body doesn't contain `10.0.0.7`.
   8. `The_rate_limit_is_per_user`: `("Assistant:PermitsPerMinute", "2")`. User A gets 200, 200, then 429 `type /problems/rate-limited` with a `Retry-After` header. User B (same tenant, same app) still gets 200. A's `GET /api/stock` still gets 200.
   9. `Oversized_bodies_are_rejected`: a 70 KB body → 413. If `TestServer` doesn't enforce the limit, assert instead that the endpoint's metadata carries `IRequestSizeLimitMetadata` with `MaxRequestBodySize == 65536`, and say so in a comment.
   10. `Prompt_text_never_reaches_logs`: a capturing `ILoggerProvider` declared in this file, via `app.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(capture).SetMinimumLevel(LogLevel.Trace)))` and a client from `CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true })`. Send `ZEBRA-CANARY-7` as the question, with a reply containing `OKAPI-CANARY-9`. No captured message, state or exception text contains either canary.
   11. `Replies_are_capped_and_round_trip` (plan D12 reply cap): script `Reply(oversized)`, where `oversized` is 10,000 copies of `"😀"` (20,000 chars), with a one-char `"x"` prefix if needed so that a naive cut at `MaxReplyChars - ReplyCutNotice.Length` would split a pair (show the arithmetic in a comment). Assert 200; `reply.Length <= AssistantLimits.MaxReplyChars`; `reply` ends with `AssistantLimits.ReplyCutNotice`; it contains no `'�'` and no lone surrogate (`char.IsSurrogate` only in valid pairs). Then send `[user, assistant: reply, user]` on the same app: 200, not 400.

## Do not
- Weaken a limit to make a test pass. If a control doesn't hold, stop and report it.
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*AssistantTests' --filter-class '*AssistantSecurityTests' --filter-class '*TenantIsolationTests'
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false && cd ..
```
