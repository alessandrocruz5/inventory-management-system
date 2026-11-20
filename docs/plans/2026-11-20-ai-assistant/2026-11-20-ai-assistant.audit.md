
## Audit · ai-assistant · 2026-11-20 · Codex (GPT-6) · PASS-WITH-CHANGES
Status: applied 2026-11-20: 1 applied, 0 disputed

Graph built: 2026-11-20 13:24:08 +08 · Symbols traversed: 5 · Missed blast-radius items: 0

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|
| FIX | Breakage / hardening | The 1,024-token output limit is only passed to the model as a request option. The handler returns `response.Text` without a server-side bound, so a provider that ignores or misapplies `MaxOutputTokens` can return an oversized reply despite R3's output cap. | Plan D12 `2026-11-20-ai-assistant.md:113`; prompt 08 sends `MaxOutputTokens` at `ai-assistant-08-chat-handler-and-pipeline.md:76` and then returns the model text at `:82`. | Define and enforce a response-side maximum before constructing the API reply (with a documented truncation or controlled failure behavior), and add an integration test using a scripted oversized response. Keep the provider token setting as an additional limit. |

**Bloat estimate:** planned ~50 unique files / ~3,000 LOC; minimal viable ~44 files / ~2,700 LOC. The additional reader contract, browser chat, and isolated console evaluation harness map directly to R1–R9; no unrelated abstraction or package was found.
**Model cost:** 03 → haiku (mechanical slug/docs update); 01, 08, 16 → opus (shared contract, trust boundary, published grader); remaining prompts → sonnet. The selected models match the stated complexity.

Blast radius checked independently: `IInventoryModule`, `InventoryModuleExtensions`, `ApiFactory`, `PostStock`, and `ProblemTypes`, plus repository-wide greps for routes, config, telemetry, package constraints, test references, and non-graph coupling. The graph predates no tracked source changes (`graphify-out/graph.json` is newer than the latest commit; working-tree changes are limited to plan bookkeeping). No additional missed callers or consumers were found. The staged unit order preserves dependencies: U1 contract → U2 API → U3 drafts, with U4 UI and U5 harness parallel after U3, then U6 write-up. The new endpoints are included in the T5 plan, and the planned T12 rules cover Assistant's read-only module boundary. No migration, tenant schema, package, or deployment change was overlooked.

### Prompt for Claude
Run: `/apply-review ai-assistant`

Apply the FIX finding at `docs/plans/2026-11-20-ai-assistant/prompts/ai-assistant-08-chat-handler-and-pipeline.md:76,82`: enforce a documented server-side maximum on returned assistant text (truncate or return a controlled ProblemDetails response), while retaining `MaxOutputTokens`; add a scripted integration test for an oversized provider response. Update the corresponding limit and verification description in `docs/plans/2026-11-20-ai-assistant/2026-11-20-ai-assistant.md:109-118` if needed. Touch only the plan and prompt files required to resolve this finding. Never create or switch branches, commit, or push. Done when the response-side bound and its test are specified and the audit finding is marked applied or disputed.
