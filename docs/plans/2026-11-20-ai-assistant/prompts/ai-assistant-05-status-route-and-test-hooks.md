---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 05
unit: IMS-34
status: done
model: sonnet
effort: medium
depends_on: [04]
parallel_group: E
files:
  create:
    - src/Modules/Assistant/Assistant/Features/Status/Endpoint.cs
    - tests/Inventory.IntegrationTests/ScriptedChatClient.cs
    - tests/Inventory.IntegrationTests/AssistantTests.cs
  modify:
    - src/Modules/Assistant/Assistant/AssistantModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
    - tests/Inventory.IntegrationTests/ApiFactory.cs
    - tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj
  delete: []
---
# 05 · Status route and test hooks

## Task
Map `GET /api/assistant/status`, exempt it in T5, and add the test plumbing the rest of the sprint uses: a scripted `IChatClient` and an `ApiFactory` hook that injects it.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Module: `src/Modules/Assistant/Assistant` (prompt 04). `AssistantOptions.ModelServiceKey = "assistant-model"`; the keyed `IChatClient` is registered only when a model is configured.
- Endpoint pattern: `src/Modules/Inventory/Inventory/Features/Stock/StockEndpoints.cs` and `Features/Stock/ListStock/Endpoint.cs`. The `/api` group already requires a signed-in user (`src/Api/Program.cs`).
- T5 (TASKS.md Downstream IMS-4): every `/api` route gets a probe or an exemption with a reason, or `Every_api_route_is_probed_or_exempt` (`TenantIsolationTests.cs:961`) fails. The route's RawText is `/api/assistant/status`.
- Microsoft.Extensions.AI (10.11.1) types used below: `IChatClient`, `ChatMessage`, `ChatRole`, `ChatOptions`, `ChatResponse`, `UsageDetails`, `FunctionCallContent`, `FunctionResultContent`, `AITool`/`AIFunction`. Check the exact members against the restored package.

### Steps
1. `Features/Status/Endpoint.cs`:
   - `internal static class GetStatusEndpoint` with `MapGetStatus(this RouteGroupBuilder assistant)` → `MapGet("/status", ...)`. It returns `TypedResults.Ok(new AssistantStatusResponse(configured, configured ? options.Value.Model ?? "unknown" : null))`, where `configured = services.GetKeyedService<IChatClient>(AssistantOptions.ModelServiceKey) is not null`.
   - Declare `internal sealed record AssistantStatusResponse(bool Configured, string? Model);` in the same file. Never return the endpoint or the key.
2. `AssistantModuleExtensions.MapAssistantEndpoints`: `var assistant = endpoints.MapGroup("/assistant"); assistant.MapGetStatus(); return endpoints;`.
3. `TenantIsolationTests.cs` `Exempt`: add `["GET /api/assistant/status"] = "reads configuration only; no tenant data"`.
4. `Inventory.IntegrationTests.csproj`: add `<ProjectReference Include="..\..\src\Modules\Assistant\Assistant\Assistant.csproj" />`.
5. `ApiFactory.cs`: add `internal IChatClient? ChatModel { get; init; }` with an XML doc in the style of `Clock`. In `ConfigureWebHost`, when it's set: `builder.ConfigureTestServices(s => { s.RemoveAllKeyed<IChatClient>(AssistantOptions.ModelServiceKey); s.AddKeyedSingleton(AssistantOptions.ModelServiceKey, model); });`.
6. `ScriptedChatClient.cs`: `internal sealed class ScriptedChatClient : IChatClient`, a model stand-in that replays steps in order.
   - Steps are nested records: `Step` (abstract); `CallTools(params ToolCall[] Calls)` with `ToolCall(string Name, object? Arguments)` (an anonymous object or dictionary, serialized to `IDictionary<string, object?>` of `JsonElement`s); `Reply(string Text)`; `Fail(Exception Error)`; `Wait(TimeSpan Delay, Step Then)`, which awaits `Task.Delay(Delay, ct)` first and so honours cancellation.
   - Constructor `ScriptedChatClient(bool repeatLast, params Step[] steps)`, plus a convenience `ScriptedChatClient(params Step[] steps)` with `repeatLast: false`. When the steps run out: with `repeatLast` the last step repeats; otherwise it throws `InvalidOperationException("script exhausted")`.
   - `CallTools` returns a `ChatResponse` whose assistant message holds one `FunctionCallContent(callId: "call-<n>", name, arguments)` per call. `Reply` returns an assistant text message. Both set `Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 }` and `ModelId = "scripted"`.
   - Recording: `public List<Turn> Turns { get; } = []` with `record Turn(IReadOnlyList<ChatMessage> Messages, IReadOnlyList<string> ToolNames)`, where the tool names come from `options?.Tools`. Copy the message list each call.
   - A helper `IEnumerable<string> ToolResultsJson()` that serializes every `FunctionResultContent.Result` the client was sent, for assertions.
   - `GetStreamingResponseAsync` throws `NotSupportedException`. `GetService` returns null. `Dispose` does nothing.
7. `AssistantTests.cs`: class `AssistantTests(DatabaseFixture db)`, with an `internal static ApiFactory CreateApp(DatabaseFixture db, IChatClient model, params (string Key, string Value)[] settings)` helper that builds `new ApiFactory(db.AppConnectionString, [("Assistant:Model", "scripted"), .. settings]) { ChatModel = model }` for later prompts. Tests:
   - `Status_is_not_configured_without_an_endpoint`: `db.CreateApp(("Assistant:Endpoint", ""))`, signed in (`DatabaseFixture.CreateTenantWithUserAsync` + `SignInAsync`) → `200 { configured: false, model: null }`.
   - `Status_names_the_model_when_configured`: `CreateApp(db, new ScriptedChatClient())` → `200 { configured: true, model: "scripted" }`.
   - `Status_requires_sign_in`: anonymous → `401`.
   - `Development_settings_point_at_local_ollama`: `db.CreateApp()` (Development env) → `configured: true`, `model: "qwen3:30b-a3b"`. It makes no network call: status never contacts the model.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Contact a real model in any test.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*AssistantTests' --filter-class '*TenantIsolationTests'
dotnet test
```
