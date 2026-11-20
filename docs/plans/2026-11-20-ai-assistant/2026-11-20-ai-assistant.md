---
type: plan
kind: feature
status: in-progress
repo: inventory-management-system
delivery: pr
base: develop
sprint: 4
epic: IMS-32
created: 2026-11-20
risk: medium
prompts: 19
graph_built: 2026-11-20T13:24:08+08:00
tags: [plan]
---
# Sprint 4 — Evaluated, hardened AI assistant over the ledger

## Request
> sprint 4 - evaluated, hardened AI asistant over the ledger

| # | Requirement |
|---|---|
| R1 | A signed-in user asks questions in plain language (English or Taglish) about their tenant's stock, items, item history and documents, and an Admin also about ledger health. The assistant answers only from read-only tools over the ledger. |
| R2 | The assistant drafts receipts, issues, transfers and (Admin only) adjustments. A person opens each draft in the existing post-stock form, checks it and posts it. The assistant itself never writes. |
| R3 | Hardened: tenant isolation, server-side role gating, no user identity in tool results, prompt-injection containment, caps on input, tool rounds, output, time and request rate, and ProblemDetails on every failure. Integration and architecture tests prove each control. |
| R4 | Observable: OpenTelemetry GenAI spans and metrics appear in the Aspire dashboard with no prompt or reply text, and each reply reports its token usage. |
| R5 | A chat page in the Angular app, with a "not set up" state, tool-use chips and draft cards. |
| R6 | Evaluated: a harness scores local and hosted open-weight models on a golden question set with a red-team subset. Answers are graded deterministically against ledger ground truth. The harness reports accuracy per category, tool use, latency and tokens per model. |
| R7 | CI calibrates the grader with scripted models (oracle, near-miss, silent) through the real API and tools. CI never calls a live model. |
| R8 | $0 to run: local Ollama for dev and the local rows, Groq's free tier for the hosted row (harness only, 20-question subset), and nothing configured in the deployed stack. |
| R9 | A results write-up (method, threat model, matrix, findings, limits), linked from the README. |

## Context used
- Note: spec §1: "AI tools + evaluation matrix | Sprint 4+ | Needs real ledger data and a golden question set". §14 Decisions: "Goal after the core: Hiring: build the AI evaluation matrix — Sprint 4+ measures accuracy, latency and cost across local and hosted open-weight models." That matches D2 exactly.
- Note: spec §4: "Later modules (packs, AI) plug in beside Inventory and call it only through `Inventory.Contracts`." This gives D6 and D7.
- Note: spec §8: a cross-tenant id gets 404, never 403. §9: the route and slug tables (new rows go here). §11: the T5 rules ("grows with every slice that adds a route") and T12. "Prefer the real database over mocks."
- Note: TASKS.md Downstream:
  - IMS-2: one public type per module implementation (`<M>ModuleExtensions` with `Add<M>Module`/`Map<M>Endpoints`); only Api and tests reference a module implementation; test projects use xunit.v3 only.
  - IMS-3: a new slug lands in §9, Kernel `ProblemTypes` and Angular `PROBLEM_MESSAGES` in the same PR. The integration-test fixture pattern.
  - IMS-4: every new `/api` route gets a T5 probe or an exemption; RawText trailing-slash rule from IMS-5.
  - IMS-6: `--max-parallel-test-modules 1`, because two assemblies starting containers at once crash.
  - IMS-7: restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1` here.
  - IMS-8 (S7/T13 note): a 201 and a replay format the same decimal differently (`1` vs `1.0000`), so compare numbers, not text. The grader does.
  - IMS-9: PostedBy `Guid.Empty` is "System"; CANDLE-WHT has no stock.
  - IMS-16: renaming port 4200, the seeded emails or a UI label used by specs breaks e2e; the shell nav overflowed at 375 px once.
  - IMS-17/18: any AppHost change re-runs `aspire publish` and updates the Bicep in the same PR. This plan changes neither (D14).
- Note: CLAUDE.md "Ask before you add any NuGet or npm package". The user approved `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.OpenAI` in this session.
- Graph: `graphify update .` (stale: web/stocktakes changed after the last build) → 4,805 nodes. This install's CLI has no `query`, `affected` or `god-nodes`. `GRAPH_REPORT.md` lists `problemMessage()` (44 edges) as a god node, and this plan only adds map entries beside it. `graphify explain` on `IInventoryModule` (3 edges), `ApiFactory` (5), `PostStock` (24) and `InventoryModuleExtensions` (3). The blast radius below comes from grep and reading code.
- Code:
  - `src/Modules/Inventory/Inventory.Contracts/IInventoryModule.cs`: the only contract today is `ProvisionTenantAsync`, a write. D8 bans it from Assistant.
  - `Inventory/InventoryModuleExtensions.cs:52-98`: handler registrations.
  - Read handlers return typed HTTP results:
    - `ListStock/Handler.cs:12`: `Results<Ok<StockPageResponse>, ValidationProblem>`
    - `GetItem/Handler.cs:13`, `GetDocument/Handler.cs:13`: `Results<Ok<T>, ProblemHttpResult>`
    - `ListItemMovements/Handler.cs:14`
    - `ListDocuments/Handler.cs:19`
    - `GetLedgerHealth/Handler.cs:21`: `Ok<LedgerHealthResponse>`, Admin route at `GetLedgerHealth/Endpoint.cs:9`
  - `Features/Paging.cs:8-9`: default 25, max 100.
  - Response records carry `PostedBy` (`ItemMovementResponse.cs`, `DocumentDetailResponse.cs`, `DocumentSummaryResponse.cs`), which D10 drops.
  - `Features/Posting/PostingValidation.cs:42-55`: `MaxAmount`, `MaxLines = 100`, reference 100 and notes 500. The draft validator mirrors these (D12).
  - `Persistence/StockDocumentType.cs`, `AdjustmentReason.cs`: the enum names.
  - `src/Api/Program.cs`: module wiring and middleware order (`UseAuthentication` → `TenantMiddleware` → `UseAuthorization`), and the `/api` group with `RequireAuthorization()`.
  - `src/ServiceDefaults/Extensions.cs:58-75`: tracing sources and meters.
  - `tests/Architecture.Tests/{ModuleBoundaryTests,ProjectReferenceTests,MigrationCallTests}.cs`: `ModuleNames = ["Inventory","Platform"]`. `Each_module_has_a_migrator` requires `Persistence/<M>Migrator.cs`, so Assistant (no tables) stays out of that list.
  - `tests/Inventory.IntegrationTests/TenantIsolationTests.cs:26-30` (Exempt) and `:961-984` (route guard). `ApiFactory.cs`: `Clock`/`Retries` init hooks. `DatabaseFixture.cs:64,78`: `CreateTenantWithUserAsync`, `SignInAsync`.
  - `src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs:29-102` and `Platform/Seeding/PlatformSeeder.cs:30-33`: the demo data and the seeded users (`admin@demo.test`, `staff@demo.test`, `admin@isolation.test`).
  - `Platform/Identity/IdentityRegistration.cs:40-41`: the cookie lasts 8 h, sliding, so the harness signs in again after it moves its fake clock.
  - `.github/workflows/ci.yml`: jobs `build` → `test` → `e2e`; `dotnet test` on the slnx runs only test projects (Api is in the slnx and isn't run).
  - `.github/scripts/check-banned-packages.sh`: bans MediatR, AutoMapper, MassTransit and FluentAssertions 8+ only, so MEAI and OpenAI pass.
  - Web:
    - `web/src/app/core/problems/problem-messages.ts:2`: the slug map.
    - `documents/post-stock.ts:164-182,352-381`: the constructor, `reset(type)` and `makeLine()`. An item change clears the line's unit (`:412`), so a prefill sets the unit after the item.
    - `documents/post-stock.guard.ts`: Staff can't open adjustment.
    - `app.html:21-115`: nav. `app.routes.ts`: lazy routes behind `authGuard`.
    - `core/auth/auth.service.ts:18-20`: the `user` and `isAdmin` signals.
- NuGet (queried 2026-11-20): `Microsoft.Extensions.AI` and `.OpenAI` `10.11.1` (net10.0 needs `Microsoft.Extensions.*` ≥ 10.0.13; the repo is on 10.0.14; transitive `OpenAI` 2.14.0). `11.0.0` (2026-11-10) would pull 11.x abstractions into a net10 app (D3).

## Options
The shape was settled in conversation, before this plan. The real choices and their outcomes:

| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| A. Evaluated, hardened assistant (read tools + drafts + eval harness + red-team) | $0 | ~1 sprint, 19 prompts | Medium: a new untrusted-input boundary | One new module and one harness | Demo data only leaves the machine, and only to Groq |
| B. Q&A only, no evals | $0 | ~½ sprint | Low | Low | Same |
| C. Sprint 4 as "live hardening", no AI | $0 | ~1 sprint | Low | — | — |
| D. A plus a Claude baseline row | ~$5 once | +1 prompt | Low | API key upkeep | Demo data to Anthropic |

**Recommendation:** A. The eval matrix and the red-team evidence are the AI-engineering signal. B is a demo, not evidence, and C leaves the gap open. **Chosen:** A, $0 (no D). The Groq row runs a 20-question subset, and chat is stateless.

## Decisions
- **D1** Scope: read-only Q&A plus draft documents, a chat page, a manual harness, and calibration with a CI fake. Chosen by the user.
- **D2** $0 and open-weight only:
  - Ollama serves dev and the local matrix rows.
  - Groq's free tier is the hosted row, and its key lives only in the harness's user-secrets.
  - Scripted `IChatClient`s run in tests and CI.
  - There's no frontier row. Chosen by the user; it matches spec §14.
- **D3** Packages: `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.OpenAI`, pinned at `10.11.1`. Approved by the user. The pin follows the repo's 10.0.x Microsoft.Extensions line. The transitive `OpenAI` 2.14.0 (MIT) falls under that approval. CLAUDE.md's allowed list gains both (prompt 04).
- **D4** The Groq row runs the 20 questions flagged `subset: true`. The local rows run all of them. Chosen by the user.
- **D5** Stateless chat. The browser keeps the conversation in memory only and clears it when the signed-in user changes. Each turn sends a capped history. The server stores nothing: no migration, no tenant table, no retention rule. Chosen by the user.
- **D6** A new module, `src/Modules/Assistant/Assistant`, per spec §4. It has no `Assistant.Contracts`, because nothing consumes it.
- **D7** Inventory gains `IInventoryReader` in `Inventory.Contracts`, implemented by reusing the existing read handlers. The assistant then sees exactly what the screens show, and the harness's ground truth (the same HTTP read routes) agrees with it by construction. Direct EF is used only for exact-key lookups that no handler offers (SKU → id).
- **D8** No write path. Assistant must not depend on `IInventoryModule`, EF Core or SqlClient, enforced by new T12 rules. Drafts are proposals. The person posts them through the existing form and `POST /api/documents/*`, so the Idempotency-Key, validation and RequestHash stay unchanged.
- **D9** Tools take SKUs and location codes, not GUIDs, except document ids. That means fewer tokens and no hallucinated ids, and lookups stay tenant-scoped.
- **D10** No user identity reaches the model. Contract snapshots have no `PostedBy`, `CreatedBy` or email; prompt 02 has a reflection test. The system prompt carries the role and today's date, not the email or name. This is minimisation under the Data Privacy Act (RA 10173).
- **D11** Role gating is server-side:
  - Staff isn't offered `get_ledger_health`.
  - An adjustment draft needs Admin.
  - The model never decides a permission.
- **D12** Limits:
  - At most 20 messages per turn.
  - A user message is 1–2,000 chars and an assistant message ≤ 8,000, with ≤ 24,000 chars in total. Roles are `user`/`assistant` only, and the last message is `user`.
  - A request body is at most 64 KB.
  - At most 5 tool rounds per turn and 1,024 output tokens, at temperature 0.
  - A reply is at most 8,000 chars (`MaxReplyChars = MaxAssistantChars`), enforced by the handler after it strips `<think>` blocks. A longer reply is cut without splitting a surrogate pair, ends with a fixed notice, and logs a Warning with its length only. The cap equals the history's assistant-message limit, so every reply fits back into the next turn. `MaxOutputTokens` stays as the provider-side limit. It doesn't bound the reply on its own: a provider can ignore it, and `ChatResponse.Text` concatenates every assistant message in the tool loop (MEAI 10.5.2 docs). Cutting beats a 503, because a 503 discards a valid answer and the tool work behind it. (Codex audit 2026-11-20; prompts 08 and 09.)
  - A 90 s budget per turn, under the nginx 120 s read timeout.
  - 10 turns/min per user.
  - Lists in tool results are capped (25, or 50 for stock) with `total` and `truncated`.
  - At most 3 drafts per reply.
  - The draft limits mirror `PostingValidation`, and a test pins them (prompt 10).
- **D13** New slugs:
  - `assistant-not-configured` (503)
  - `assistant-unavailable` (503, with `Retry-After` when known): timeouts, model errors and upstream 429s
  - `rate-limited` (429, `Retry-After`)
- **D14** Config keys `Assistant:Endpoint`, `Assistant:Model`, `Assistant:ApiKey`, `Assistant:PermitsPerMinute` (default 10) and `Assistant:TimeoutSeconds` (default 90). Dev defaults sit in `src/Api/appsettings.Development.json` and point at local Ollama `http://localhost:11434/v1` with `qwen3:30b-a3b`. The AppHost, compose and Bicep don't set them, so the deployed stack answers `assistant-not-configured`. No AppHost or `deploy/` change, and no re-publish.
- **D15** Telemetry. The MEAI pipeline is `UseFunctionInvocation()` → `UseOpenTelemetry(sourceName: "Assistant")` with `EnableSensitiveData = false`, and ServiceDefaults adds the `Assistant` source and meter. Logs never contain prompt or reply text.
- **D16** The harness, `tests/Assistant.Evals`, is a console app, not an xunit project, so `dotnet test` never runs a live model. It hosts the real API in-process on a Testcontainers SQL Server 2025 with a `FakeTimeProvider`. It migrates, seeds the demo, and posts a fixed scenario (including a prompt-injection item name, injection notes and an isolation-tenant canary). It then grades with resolvers over the API's read routes.
- **D17** Calibration. `--check` runs three scripted rows through the real pipeline and tools:
  - The oracle must score 100%.
  - The near-miss must fail every answerable question.
  - The silent row must fail every answerable question.

  `dotnet run --project tests/Assistant.Evals -- --check` becomes a step in CI's `test` job (T20).
- **D18** Assistant integration tests go in `tests/Inventory.IntegrationTests`. They share the fixture and the T5 guard, and avoid a third SQL container in CI (IMS-6 note). They are named T19 in spec §11.
- **D19** No streaming in v1: one JSON reply per turn, with a progress state in the UI.
- **D20** The UI renders replies as plain text (`white-space: pre-wrap`, interpolation only, no `innerHTML` and no markdown), so model output can't inject HTML.
- **D21** The grader:
  - Pass/fail uses the answer checks (numbers, mentions, drafts) and the safety checks (forbidden tools, canaries, no writes).
  - Tool use is a separate column.
  - Numbers compare as numbers: money at 2 dp, and quantities exactly at up to 4 dp, accepting `1`, `1.00` and `1.0000`.
  - After every question, the document totals of both tenants must be unchanged.

**Assumptions:** (for `/audit-plan` to challenge)
- MEAI 10.11.1 exposes these names, as in 9.x/10.x: `AsIChatClient`, `ChatClientBuilder.UseFunctionInvocation(loggerFactory, configure)` with `MaximumIterationsPerRequest`/`AllowConcurrentInvocation`/`IncludeDetailedErrors`, `UseOpenTelemetry(loggerFactory, sourceName, configure)`, `AIFunctionFactory.Create`, `FunctionCallContent`/`FunctionResultContent` and `ChatResponse.Usage`. Each prompt tells the executor to check against the restored package.
- `FunctionInvokingChatClient` sums `Usage` across rounds. Prompt 08 checks this and sums the usage itself if not.
- Ollama's `/v1` tool calling works through the OpenAI .NET SDK for `qwen3:30b-a3b`. Qwen3 may emit `<think>…</think>`, so the handler strips it.
- Groq's free tier allows about 30 RPM, ~1,000 RPD, ~6–8k TPM and ~100–200k TPD per model, per organisation, and the catalogue changes. Model ids are picked at run time from console.groq.com.
- Manila dates use a fixed `+08:00` offset. The Philippines has had no DST since 1978, so this is exact, and it avoids depending on tzdata in chiseled images.
- `dotnet test` (MTP) skips non-test `Exe` projects in the slnx, as it already does for `Api`. Prompt 15 checks this.
- `TestServer` enforces the 64 KB body limit. If it doesn't, prompt 09 asserts the endpoint metadata instead.
- The user runs the live matrix on their own machine (GPU, Groq key) between IMS-37 and IMS-38.

**Non-goals:**
- RAG, embeddings or vector search.
- NL2SQL; the assistant has no SQL path at all (D8).
- An MCP server.
- Streaming.
- Server-side chat history.
- A Claude or other frontier row.
- An Ollama resource in the AppHost.
- Any `deploy/`, compose or Bicep change.
- Prompt caching or fine-tuning.
- Rendering markdown in replies.
- A Playwright spec for the chat. The UI is covered by Vitest and the API by integration tests.
- AI quotas or billing per tenant.
- A dollar-cost column; every row is $0, so tokens stand in for cost.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory.Contracts/IInventoryReader.cs` | C | Read contract for other modules (D7) | R1 | 01 |
| `src/Modules/Inventory/Inventory.Contracts/InventoryReadModels.cs` | C | Snapshot records, with no user identity (D10) | R1, R3 | 01 |
| `src/Modules/Inventory/Inventory/InventoryReader.cs` | C | Implements the contract over the existing handlers | R1 | 01 |
| `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs` | M | Registers `IInventoryReader` | R1 | 01 |
| `tests/Inventory.IntegrationTests/InventoryReaderTests.cs` | C | Reader matches the routes, stays tenant-scoped, carries no identity | R1, R3 | 02 |
| `src/Shared/Kernel/Problems/ProblemTypes.cs` | M | 3 slugs (D13) | R3 | 03 |
| `web/src/app/core/problems/problem-messages.ts` (+ `.spec.ts`) | M | Slug ↔ map coupling | R3, R5 | 03 |
| `docs/Inventory System — Sprint 1 Spec.md` | M | §4 module, §9 routes and slugs, §11 T19, §15 log (03). §9 drafts (11). §11 T20 (17). §1/§14 results (19) | R3, R6 | 03, 11, 17, 19 |
| `src/Modules/Assistant/Assistant/Assistant.csproj` | C | New module (D6); MEAI packages (D3) | R1 | 04 |
| `src/Modules/Assistant/Assistant/AssistantModuleExtensions.cs` | C, then M | Registration, model client, catalog, pipeline, rate limiter, routes | R1, R3, R4 | 04, 05, 07, 08, 09, 10 |
| `src/Modules/Assistant/Assistant/AssistantOptions.cs` | C | Config keys (D14) | R8 | 04 |
| `InventorySystem.slnx` | M | Add Assistant and Assistant.Evals | R1, R6 | 04, 15 |
| `src/Api/Api.csproj` | M | Reference Assistant; give `Assistant.Evals` internals access | R1, R6 | 04, 15 |
| `src/Api/Program.cs` | M | `AddAssistantModule`, `MapAssistantEndpoints` (04); `UseRateLimiter` (09) | R1, R3 | 04, 09 |
| `src/Api/appsettings.Development.json` | M | Dev defaults to local Ollama (D14). 19 only recommends a new default; the user decides | R8 | 04 |
| `CLAUDE.md` | M | Allowed packages (04); hidden coupling for draft limits (10) and the golden set (16) | R3, R6 | 04, 10, 16 |
| `src/Modules/Assistant/Assistant/Features/Status/Endpoint.cs` | C | `GET /api/assistant/status` | R5, R8 | 05 |
| `tests/Inventory.IntegrationTests/ScriptedChatClient.cs` | C | Scripted model for tests (D18) | R3 | 05 |
| `tests/Inventory.IntegrationTests/ApiFactory.cs` | M | `ChatModel` init hook | R3 | 05 |
| `tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj` | M | Reference Assistant | R3 | 05 |
| `tests/Inventory.IntegrationTests/AssistantTests.cs` | C, then M | Status, chat contract, drafts in replies | R1–R5 | 05, 09, 11 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | M | T5 exemption (status), T5 probe (chat) | R3 | 05, 09 |
| `tests/Architecture.Tests/ModuleBoundaryTests.cs`, `ProjectReferenceTests.cs`, `Architecture.Tests.csproj` | M | T12 covers Assistant; no write or SQL path (D8) | R3 | 06 |
| `src/Modules/Assistant/Assistant/Tools/{InventoryTools,ToolResults,ToolCatalog}.cs` | C | 7 read tools, role-gated (D9–D11) | R1, R3 | 07 |
| `src/Modules/Assistant/Assistant/Prompting/SystemPrompt.cs` | C, then M | Rules, role and date, injection framing | R1, R3 | 07, 10 |
| `tests/Inventory.IntegrationTests/AssistantToolTests.cs` | C | Tools in a tenant scope | R1, R3 | 07 |
| `src/Modules/Assistant/Assistant/Features/Chat/{Handler,Request,Response}.cs` | C, then M | Turn handling, validation, limits, error mapping | R1, R3, R4 | 08, 11 |
| `src/Modules/Assistant/Assistant/{AssistantModel,AssistantProblems}.cs` | C | MEAI pipeline holder; ProblemDetails helpers | R3, R4 | 08 |
| `src/ServiceDefaults/Extensions.cs` | M | `Assistant` source and meter (D15) | R4 | 08 |
| `src/Modules/Assistant/Assistant/Features/Chat/Endpoint.cs` | C | `POST /api/assistant/chat` | R1 | 09 |
| `tests/Inventory.IntegrationTests/AssistantSecurityTests.cs` | C | The hardening controls of R3 (T19) | R3 | 09 |
| `src/Modules/Assistant/Assistant/Drafts/{DocumentDraft,DraftValidator}.cs`, `Tools/DraftTool.cs` | C | Drafts (D8, D11, D12) | R2, R3 | 10 |
| `tests/Inventory.IntegrationTests/AssistantDraftTests.cs` | C | Draft rules, mirrored limits, no writes | R2, R3 | 10 |
| `web/src/app/assistant/assistant.service.ts` (+ spec) | C | Status, send, in-memory conversation (D5) | R5 | 12 |
| `web/src/app/assistant/assistant.{ts,html,scss}` (+ spec) | C, then M | Chat page (D19, D20) | R5 | 12, 13 |
| `web/src/app/app.routes.ts`, `web/src/app/app.html` | M | `/assistant` route and nav link | R5 | 12 |
| `web/src/app/assistant/draft-card.ts` (+ spec) | C | Draft card with "Review and post" | R2, R5 | 13 |
| `web/src/app/documents/posting-form.ts` (+ spec) | M | Pure `draftToForm` mapping | R2 | 14 |
| `web/src/app/documents/post-stock.{ts,html}` (+ spec) | M | Prefill from navigation state, with a notice | R2 | 14 |
| `tests/Assistant.Evals/Assistant.Evals.csproj`, `Program.cs`, `EvalHost.cs`, `EvalScenario.cs` | C | Harness host and fixed scenario (D16) | R6, R8 | 15 |
| `src/Modules/Assistant/Assistant/Assistant.csproj` | M | Give `Assistant.Evals` internals access (model key) | R6 | 15 |
| `tests/Assistant.Evals/{golden.json,GoldenSet,GroundTruth,Grader}.cs` | C | Golden set, resolvers, grader (D21) | R6 | 16 |
| `tests/Assistant.Evals/{EvalRunner,ScriptedModels}.cs` | C | Runner and calibration rows (D17) | R6, R7 | 17 |
| `.github/workflows/ci.yml` | M | `--check` step in `test` | R7 | 17 |
| `tests/Assistant.Evals/{matrix.json,Throttle,Report}.cs`, `README.md` | C | Live rows, rate pacing, checkpoint, report | R6, R8 | 18 |
| `docs/evals/README.md` | C | Results write-up | R9 | 19 |
| `README.md` | M | "AI assistant" section | R8, R9 | 19 |

### Review constraints check
- decimal / decimal(19,4), AwayFromZero: the contract snapshots and draft quantities are `decimal`. Drafts check ≤ 4 dp; they never round, they reject. No `double` anywhere, including the grader's comparisons.
- No UPDATE/DELETE on ledger tables, no hard deletes: Assistant has no EF and no write contract (T12, D8). The harness posts its scenario through the public API only.
- TenantId never from a request; no bare `IgnoreQueryFilters()`: tools take codes and ids that resolve inside the caller's scope. The reader uses the handlers or tenant-filtered EF, and T5 probes it through the chat (09).
- PostedAt from TimeProvider; no write request carries a date: drafts carry no date. `list_documents` dates are read filters, like `GET /documents`, and the T10 allowlist holds because the chat route writes nothing.
- The API connects as `inventory_app`: unchanged. The harness migrates as `sa` and serves as `inventory_app`, as the fixture does.
- No banned package; nothing outside the list without asking: MEAI and MEAI.OpenAI are approved (D3); `OpenAI` is transitive. No npm package is added.
- Section 6 and ledger columns: untouched.

## Blast radius
**Risk:** medium. The plan adds to a `Shared code:` contract (`Inventory.Contracts`) and opens a new untrusted-input boundary, where model output drives tool calls under the caller's tenant. It touches no High-stakes path, adds no migration and writes nothing to the ledger. RLS stays the backstop under every read.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `Inventory.Contracts` | Direct (shared code) | New `IInventoryReader` and records | No. Additive; T12 `Contracts_reference_only_Kernel_and_other_contracts` still holds | Opus prompt; 02 tests parity with the routes |
| Read handlers (ListStock, ListItems, GetItem, ListItemMovements, ListDocuments, GetDocument, GetLedgerHealth, ListLocations) | Direct | Called by `InventoryReader` | No; unchanged. A later change to a result union fails the reader at compile time | Reader unwraps `Ok<T>` only and maps the rest |
| `GetLedgerHealthHandler` (in `Features/Ledger/**`, a high-stakes glob) | Direct | Called, not modified | No | Admin gating lives in `ToolCatalog` (D11) and is tested (07, 09) |
| `AddInventoryModule` | Direct | One registration | No | — |
| `src/Api/Program.cs` | Direct | Module wiring; `UseRateLimiter` after `UseAuthorization` | No. Only endpoints with `RequireRateLimiting` are limited; there's no global limiter | 09 asserts other routes aren't limited |
| T5 route guard | Non-graph (test) | 2 new routes | Would fail | Exemption with the status route (05); probe with the chat route (09) |
| T12 rules | Non-graph (test) | New module | Would miss it | 06 adds Assistant to the boundary and reference rules. `MigrationCallTests` stays as it is (no persistence, D5) |
| `ApiFactory` | Direct (test) | New `ChatModel` init hook | No. Additive; existing callers are unchanged | — |
| `appsettings.Development.json` | Non-graph (config) | Integration tests run in Development | No. A real client registers but is never called; tests that chat inject `ChatModel` or blank `Assistant:Endpoint` | 05 |
| ServiceDefaults tracing/metrics | Direct | `AddSource`/`AddMeter("Assistant")` | No | — |
| AppHost, `deploy/`, Bicep | Non-graph (hidden coupling) | No change; the new keys are optional with defaults | No. The deployed stack shows "not configured" | D14; 19's README states it |
| nginx `proxy_read_timeout 120s` | Non-graph | Long turns | No. The 90 s budget is below it | D12 |
| Chat history round trip (chat page, prompt 12) | Non-graph (contract) | Each reply is re-sent as an `assistant` message, which the server rejects above 8,000 chars | Would 400 every later turn after one long reply, since the page trims only the oldest messages | D12 reply cap in 08 equals `MaxAssistantChars`; 09's `Replies_are_capped_and_round_trip` re-sends a cut reply |
| ProblemDetails slugs ↔ `PROBLEM_MESSAGES` ↔ §9 | Hidden coupling | 3 new slugs | Would drift | All three in prompt 03, same PR (IMS-34) |
| `problemMessage()` (god node) | Transitive | Map grows | No | Entries only |
| `PostStock` (24 edges) | Direct | Prefill path | No. It runs only when navigation state carries a valid draft whose type matches the route | 14 extends `post-stock.spec.ts`; the normal path is tested unchanged |
| Post-stock guard (Staff can't open adjustment) | Direct | Draft cards | No. The server never offers Staff an adjustment draft (D11), and the card hides the button for non-Admin | 10, 13 |
| `app.html` nav at 375 px | Known risk (IMS-16) | One more link | Could crowd | The drawer handles narrow widths; e2e at 375 px runs in CI; 12 checks it at 375 px |
| e2e specs (link names, seeded emails, port 4200) | Non-graph | New link "Assistant" | No. No label collides ("Ledger health" etc.) | — |
| CI `test` job | Non-graph | New `--check` step after `dotnet test` | No. Job ids are unchanged; adds ~2 min | 17 |
| `check-banned-packages.sh` | Non-graph | New projects and packages | No. MEAI/OpenAI aren't banned | 04 runs it |
| `dotnet test` on the slnx | Non-graph | New `Exe` project `Assistant.Evals` | No, as long as MTP skips non-test projects | 15 verifies by listing the test modules run |
| Testcontainers start-up crash (IMS-6) | Known risk | The harness starts its own container | No in CI: it runs after `dotnet test` in sequence | — |
| Database schema, RLS, T6 | — | No change | No | — |

**Known risks in the touched area:**
- Prompt injection through tenant-entered text (item names, document notes and references). Containment is structural: the model has no write tool, role gating is server-side, tenant scope comes from the request, drafts need a human, and replies render as text. The red-team set measures how well each model resists.
- Cross-border data under the Data Privacy Act if someone later points production at a hosted model. The deployed stack is unconfigured (D14), and the README warns that a privacy review comes first (19).
- Model quality varies with hardware and version, and the free tier can change. The write-up states the dates, model tags, hardware and n (19).

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-33 | Inventory read contract | Added | 01, 02 | — | no (shared code) | R1: `IInventoryReader` returns the same numbers as `GET /stock`, `/items/{id}`, `/items/{id}/movements`, `/documents`, `/documents/{id}` and `/ledger/health` for the caller's tenant. R3: a foreign id or SKU returns null or empty; no contract type has a `PostedBy`/`CreatedBy`/`Email` member. |
| IMS-34 | Assistant API: read-only Q&A, hardened and observable | Added | 03, 04, 05, 06, 07, 08, 09 | IMS-33 | no | R1: `POST /api/assistant/chat` answers through the 7 read tools. R3: T5 probe; Staff is never offered ledger health; unknown tools write nothing; no identity in tool results; 400 on bad history; caps on tool rounds, output (1,024 tokens, and replies ≤ 8,000 chars that round-trip as history), time and rate; 503 `assistant-not-configured`/`assistant-unavailable`; 429 `rate-limited`; T12 forbids writes and SQL from Assistant. R4: `Assistant` spans and metrics without content; `usage` in every reply. R8: `GET /api/assistant/status` reports the configuration; the deployed stack is unconfigured. |
| IMS-35 | Draft documents (API) | Added | 10, 11 | IMS-34 | no | R2: `draft_document` validates against master data and the mirrored posting limits, returns `drafts` in the reply, and writes nothing. Staff can't draft adjustments; foreign SKUs and locations are rejected; at most 3 drafts per reply. |
| IMS-36 | Assistant chat page and form prefill | Added | 12, 13, 14 | IMS-35 | no | R5: `/assistant` works at 375 px, with a not-configured state, tool chips, plain-text replies, errors through `problemMessage`, and a conversation cleared on user change. R2: "Review and post" opens the form prefilled with a notice; the normal form path is unchanged. |
| IMS-37 | Evaluation harness | Added | 15, 16, 17, 18 | IMS-35 | no | R6: `--validate-golden` resolves every expectation against the scenario; the live matrix writes JSONL and `matrix.md`. R7: CI's `--check` passes (oracle 100%, near-miss and silent fail every answerable question). R8: Groq row paced and resumable; no key in the repo. |
| IMS-38 | Evaluation results write-up | Added | 19 | IMS-36, IMS-37 | no | R9: `docs/evals/README.md` with method, threat model (control → test), the matrix from the user's run, findings and limits; README "AI assistant" section; spec §1/§14 updated. |

No unit touches a `## High-stakes paths` glob. IMS-33 changes `Shared code:`, and IMS-34 is a security boundary, so request a code-guardian review on both.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Inventory read contract and reader | IMS-33 | opus | high | — | A | 4 |
| 02 | Reader integration tests | IMS-33 | sonnet | medium | 01 | B | 1 |
| 03 | Assistant problem slugs and spec | IMS-34 | haiku | low | — | C | 4 |
| 04 | Assistant module skeleton and wiring | IMS-34 | sonnet | medium | 03 | D | 8 |
| 05 | Status route and test hooks | IMS-34 | sonnet | medium | 04 | E | 7 |
| 06 | Architecture rules for Assistant | IMS-34 | sonnet | medium | 05 | F | 3 |
| 07 | Read tools and system prompt | IMS-34 | sonnet | high | 06 | G | 6 |
| 08 | Chat handler, model pipeline and limits | IMS-34 | opus | high | 07 | H | 7 |
| 09 | Chat route, rate limit, T5 probe and hardening tests | IMS-34 | sonnet | high | 08 | I | 6 |
| 10 | Draft validation and the draft tool | IMS-35 | sonnet | high | 09 | J | 8 |
| 11 | Drafts in the chat reply | IMS-35 | sonnet | medium | 10 | K | 4 |
| 12 | Chat page | IMS-36 | sonnet | medium | 11 | L | 8 |
| 13 | Draft cards | IMS-36 | sonnet | medium | 12 | M | 4 |
| 14 | Post-stock prefill from a draft | IMS-36 | sonnet | high | 13 | N | 5 |
| 15 | Eval host and scenario | IMS-37 | sonnet | high | 11 | O | 7 |
| 16 | Golden set, ground truth and grader | IMS-37 | opus | high | 15 | P | 6 |
| 17 | Runner, calibration rows and CI check | IMS-37 | sonnet | high | 16 | Q | 5 |
| 18 | Live matrix: rows, pacing, checkpoint, report | IMS-37 | sonnet | medium | 17 | R | 6 |
| 19 | Results write-up | IMS-38 | sonnet | medium | 14, 18 | S | 3 |

IMS-36 (12–14) and IMS-37 (15–18) both depend only on IMS-35, so they can go in parallel as separate PRs.

- **01 opus:** `Shared code:` contract that every later module reads through. A wrong shape or a tenant-scope slip there leaks into every consumer.
- **08 opus:** the trust boundary. Untrusted model output drives tool calls under the caller's tenant, and the turn handler owns the limits, the timeout semantics (client abort vs. budget), the error mapping and the MEAI pipeline order.
- **16 opus:** the grader defines every published number. A lenient check inflates every row; a brittle one silently deflates it.

## End-to-end verification
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false && cd ..

# R1, R3 (IMS-33, IMS-34): reader parity, tools, chat contract, hardening, T5
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*InventoryReaderTests' --filter-class '*AssistantToolTests' --filter-class '*AssistantTests' --filter-class '*AssistantSecurityTests' --filter-class '*TenantIsolationTests'
# R3 (D8): T12, no write or SQL path from Assistant
dotnet test --project tests/Architecture.Tests
# R2: drafts validate, never write, limits mirror PostingValidation
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*AssistantDraftTests'
# R6, R7: the golden set resolves; calibration (also in CI)
dotnet run --project tests/Assistant.Evals -- --validate-golden
dotnet run --project tests/Assistant.Evals -- --check
# R8: no banned packages; no secrets committed
bash .github/scripts/check-banned-packages.sh
git grep -nE 'gsk_[A-Za-z0-9]{20,}' -- . ':!**/node_modules/**' && echo 'KEY LEAK' || echo 'no Groq keys in the repo'
# R4, R5, R8 (manual, needs Ollama running with qwen3:30b-a3b):
#   aspire run → sign in as admin@demo.test → /assistant → "How much white rice is at QC?" → answer + "Checked stock" chip;
#   Aspire dashboard → Traces → the api span has "chat qwen3:30b-a3b" and "execute_tool get_stock" children, with no message text.
#   "Receive 2 sacks of brown sugar at MKT at 3,650 per sack" → draft card → Review and post → prefilled form → Post → document.
#   Stop Ollama → send → "The assistant's model didn't respond" message.
# R6 (manual, IMS-37 → IMS-38): the live matrix, see tests/Assistant.Evals/README.md
dotnet run --project tests/Assistant.Evals -- --rows local
dotnet run --project tests/Assistant.Evals -- --rows groq --subset
```

## Rollback
- Each unit is one PR with no migration, so `git revert` of the merge commit rolls it back.
- Kill switch with no deploy: blank `Assistant:Endpoint`. The API returns `503 assistant-not-configured` and the page shows "not set up".
- Reverting IMS-33 needs IMS-34–IMS-38 reverted first: Assistant is IMS-33's only consumer, and the compiler enforces the order.
- The CI `--check` step reverts with IMS-37.

## Log
- 2026-11-20: planned
- 2026-11-20: applied Codex audit (1 applied, 0 disputed)
- 2026-11-20: audited, 1 fix applied (server-side reply cap, D12)
- 2026-11-20: IMS-33 started; rollback point b9e55f038222d471b670998295e4582a144e9b76
- 2026-11-20: 01 done (opus, 0 retries): IInventoryReader + 16 snapshot records in Inventory.Contracts; InventoryReader maps the 8 read handlers field by field (no PostedBy/Version), 404 → null, ValidationProblem → ArgumentException; registered scoped. 682 tests green.
- 2026-11-20: 02 done (sonnet, 0 retries): InventoryReaderTests, 7 tests green (stable over 2 runs); full suite 689 green.
