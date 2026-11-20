---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 17
unit: IMS-37
status: todo
model: sonnet
effort: high
depends_on: [16]
parallel_group: Q
files:
  create:
    - tests/Assistant.Evals/EvalRunner.cs
    - tests/Assistant.Evals/ScriptedModels.cs
  modify:
    - tests/Assistant.Evals/Program.cs
    - .github/workflows/ci.yml
    - "docs/Inventory System — Sprint 1 Spec.md"
  delete: []
---
# 17 · Runner, calibration rows and CI check (T20)

## Task
Build the runner that asks each golden question through `POST /api/assistant/chat` and grades it, plus three scripted calibration rows (oracle, near-miss, silent) that run through the **real pipeline and real tools**. Add `--check` and run it in CI.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Read: `tests/Assistant.Evals/{Program,EvalHost,EvalScenario,GoldenSet,GroundTruth,Grader}.cs` and `golden.json`. Read `tests/Inventory.IntegrationTests/ScriptedChatClient.cs` for how a scripted `IChatClient` emits `FunctionCallContent` and sees `FunctionResultContent`.
- A scripted model is the keyed inner client (`EvalHost.CreateApp(model, …)`), so the app's own `FunctionInvokingChatClient` runs the real tools against the scenario database. That's the point: the calibration proves the tools, the reply contract, the resolvers and the grader together.
- Plan D17:
  - oracle → 100%;
  - near-miss → fails every answerable question;
  - silent → fails every answerable question;
  - no writes in any row.

### Files
1. `EvalRunner.cs`: `internal sealed class EvalRunner(EvalHost host, ScenarioFacts facts, GroundTruth truth)` with `Task<IReadOnlyList<QuestionResult>> RunAsync(EvalApiFactory app, IEnumerable<Question> questions, IProgress<QuestionResult>? progress, CancellationToken ct)`.
   - Signs in once per role (`staff@demo.test`, `admin@demo.test`) on that factory. The clock is fixed at the scenario's `Now`, so cookies hold.
   - Per question:
     1. Read the document totals of both tenants (demo admin and isolation admin clients).
     2. POST `{ messages: [...history, { role: "user", content: ask }] }`, timed with a `Stopwatch`.
     3. Read the totals again, then `Grader.Grade`.
   - Expected values come from `GroundTruth` and are resolved once per question.
2. `ScriptedModels.cs`: `internal sealed class GoldenScriptedClient(GoldenSet set, IReadOnlyDictionary<string, IReadOnlyList<decimal>> expected, ScriptedMode mode) : IChatClient`, with `ScriptedMode { Oracle, NearMiss, Silent }`.
   - Identify the question by the last user message (exact `ask` after substitution).
   - First call (no tool results yet):
     - Oracle and NearMiss emit `oracle.calls` as `FunctionCallContent`s. NearMiss adds 1 to each draft quantity in a `draft_document` call.
     - Silent replies at once.
   - Next call: the final text.
     - **Oracle:** the expected numbers, formatted in a style that rotates with the question index (`1234.5`, `1,234.50`, `₱1,234.50`, `PHP 1234.5000`), which exercises the grader's parser. Then the first alternative of each `mentions` group. For questions with no numbers, mentions or draft: `"I can't help with that."`.
     - **NearMiss:** every number + 1 (money + 1.00), and each mention replaced with `ZZZ`.
     - **Silent:** `"I can't help with that."`.
   - Set `Usage` (100/10) and `ModelId = "scripted-<mode>"`.
3. `Program.cs`: `--check` starts the host, runs the scenario, then loads, validates and resolves the golden set. It runs the three rows, each on its own factory, and prints a table (row, passed / total, answerable failed). Exit 1 unless:
   - oracle passes 52/52;
   - near-miss and silent pass none of the answerable questions;
   - no question in any row wrote.

   On failure, print each violating question id and its reasons.
4. `.github/workflows/ci.yml`: in the `test` job, after `- run: dotnet test`, add:
   ```yaml
         - name: Eval harness calibration (T20)
           run: dotnet run --project tests/Assistant.Evals -- --check
   ```
   Change nothing else. The job ids (`build`, `test`, `e2e`) are the required checks.
5. Spec §11 test table: add `| T20 | Eval harness calibration: oracle 100%, near-miss and silent fail every answerable question, no writes in any row | Harness (CI) | Plan [[2026-11-20-ai-assistant]] R7 |`.

## Do not
- Run a live model, or read any API key, in `--check`.
- Loosen the grader to make the oracle pass. If the oracle fails, the bug is in the oracle calls, a resolver or a tool: fix the golden set or stop and report.
- Touch files outside the `files` list. If you must, stop and report why.
- Add packages. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet run --project tests/Assistant.Evals -- --check
dotnet test
```
- `--check` prints oracle 52/52, near-miss 0 answerable passed, silent 0 answerable passed, and exits 0. Then break one resolver on purpose (for example add 1 to `stock.onHand`); `--check` exits 1 naming the affected questions. Revert it.
