---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 19
unit: IMS-38
status: done
model: sonnet
effort: medium
depends_on: [14, 18]
parallel_group: S
files:
  create:
    - docs/evals/README.md
  modify:
    - README.md
    - "docs/Inventory System — Sprint 1 Spec.md"
  delete: []
---
# 19 · Results write-up

## Task
Write the evaluation results page, the portfolio artifact of this sprint, from the user's matrix run. Link it from the README with a short "AI assistant" section, and record the outcome in the spec.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop. IMS-36 and IMS-37 are merged.
- **Precondition.** The user has run the live matrix, so `docs/evals/results/<date>/` holds `matrix.md` and one `*.jsonl` per row, with at least two local rows and the Groq row. If it doesn't, stop and print exactly:
  `dotnet run --project tests/Assistant.Evals -- --rows local --hardware "<GPU, VRAM, cores>"` and `dotnet run --project tests/Assistant.Evals -- --rows gpt-oss-120b-groq --subset --out docs/evals/results/<same date>`.
  These result files are untracked inputs that go into this PR unchanged. They're demo data only, and contain no keys (check with `grep -rn 'gsk_' docs/evals/results` → nothing).
- Read:
  - The plan `docs/plans/2026-11-20-ai-assistant/2026-11-20-ai-assistant.md` (R1–R9, D1–D21, Assumptions, Known risks).
  - `tests/Assistant.Evals/README.md` and `golden.json`.
  - The test names in `tests/Inventory.IntegrationTests/{AssistantSecurityTests,AssistantDraftTests,AssistantToolTests,TenantIsolationTests}.cs` and `tests/Architecture.Tests/ModuleBoundaryTests.cs`.
- Audience: a hiring manager or an AI engineer skimming a portfolio. Lead with the result, keep it factual, and give a number for every claim. Don't round in the model's favour.

### `docs/evals/README.md` sections
1. **Summary** (≤ 5 lines): what the assistant does, the best accuracy and which model, the safety pass rate across rows, and "0 writes in N × M answers". It runs at $0.
2. **How it works**: a mermaid flowchart. Browser → `POST /api/assistant/chat` → `ChatHandler` → MEAI `FunctionInvokingChatClient` → `OpenTelemetryChatClient` → OpenAI-compatible endpoint (Ollama or Groq); the tools → `IInventoryReader` → Inventory's read handlers → EF tenant filter + SQL Server RLS; drafts → a card → the post-stock form → the existing posting route.
3. **Threat model**: a table of threat → control → evidence (a test or rule name), covering:
   - cross-tenant reads (T5 `Assistant_tools_read_only_the_callers_tenant`, RLS);
   - writes by the model (T12 `Assistant_has_no_write_or_sql_path`, `Unknown_and_write_tools_change_nothing`, harness no-writes);
   - privilege escalation (`Staff_is_never_offered_ledger_health`, `Only_an_admin_drafts_an_adjustment`);
   - prompt injection via item names and notes (red-team safety-3/4; containment by design);
   - PII leakage (`Tool_results_carry_no_user_identity`, `Prompt_text_never_reaches_logs`, safety-8);
   - system prompt leakage (safety-9);
   - resource abuse (limits, `Tool_rounds_are_capped`, `Replies_are_capped_and_round_trip`, `A_slow_model_is_cut_off`, `The_rate_limit_is_per_user`);
   - XSS from model output (plain-text rendering, `draft-card.spec`).
4. **Method**: the scenario, the 52 questions by category (20 in the subset), deterministic grading (D21), the calibration (oracle / near-miss / silent, in CI as T20), the settings (temperature 0, the limits), the hardware, and the date and commit.
5. **Results**: the matrix table copied from `matrix.md`, with the per-category table and the `†` subset note.
6. **Findings** (3–6 bullets, each backed by a number from the JSONL): for example, where small local models fail (tool choice, unit conversion, Taglish), how the injection cases fared, the latency/accuracy trade-off, and tokens per answer.
7. **Limitations**: a small n (52 and 20); one run per row; temperature 0 isn't bit-exact on GPUs; the free tier's limits and catalogue change; model tags and dates; no frontier baseline by design ($0); and the grader checks numbers and mentions, not tone.
8. **Reproduce**: the commands.
9. **Recommendation**: which local model should be the dev default, and why. Don't change `appsettings.Development.json`; the user decides.

### Other files
- `README.md`: add a short `## AI assistant` section after the existing feature sections. Cover:
  - what it is;
  - the $0 dev setup (install Ollama, `ollama pull qwen3:30b-a3b`, `aspire run`, open `/assistant`);
  - that the deployed stack shows "not set up" by design;
  - a Data Privacy Act note: pointing production at a hosted model sends tenant data to that provider, so do a privacy review first;
  - a link to `docs/evals/README.md`.
- Spec:
  - §1, on the "AI tools + evaluation matrix" row: mark it done in Sprint 4, linked to `docs/evals/README.md`.
  - §14, on the "Goal after the core" row: append "Sprint 4: built; results in docs/evals".
  - §15 Log: `- <date>: Sprint 4 evaluation results ([[2026-11-20-ai-assistant]]).`

## Do not
- Edit or re-run the result files, or invent numbers. Every figure must come from them.
- Touch files outside the `files` list (and the existing `docs/evals/results/**`). If you must, stop and report why.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false && cd ..
grep -rn 'gsk_' docs/evals || echo 'no keys'
```
- Every number in `docs/evals/README.md` traces to `matrix.md` or a JSONL line. Spot-check three and list them in your report.
