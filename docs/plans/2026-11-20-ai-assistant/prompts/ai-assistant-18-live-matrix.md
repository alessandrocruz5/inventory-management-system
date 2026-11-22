---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 18
unit: IMS-37
status: done
model: sonnet
effort: medium
depends_on: [17]
parallel_group: R
files:
  create:
    - tests/Assistant.Evals/matrix.json
    - tests/Assistant.Evals/Throttle.cs
    - tests/Assistant.Evals/Report.cs
    - tests/Assistant.Evals/README.md
  modify:
    - tests/Assistant.Evals/Program.cs
    - tests/Assistant.Evals/EvalRunner.cs
  delete: []
---
# 18 · Live matrix: rows, pacing, checkpoint, report

## Task
Run the golden set against real open-weight models: local Ollama rows, and Groq's free tier on the 20-question subset. Pace the Groq row to its free-tier limits, checkpoint every answer so a stopped run resumes, and write the matrix report. Document how to run it.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Read: `tests/Assistant.Evals/{Program,EvalHost,EvalRunner,Grader,GoldenSet}.cs`.
- Plan D2/D4/D8:
  - $0 only. Local rows use `http://localhost:11434/v1` (key ignored); the hosted row uses `https://api.groq.com/openai/v1`.
  - The Groq row runs the 20 `subset` questions.
  - The key comes from user-secrets (`Groq:ApiKey` in this project, `UserSecretsId ims-assistant-evals`) or the env var `Groq__ApiKey`. It's never written to a file, a log or a result.
  - Only the seeded demo scenario is sent to Groq.
- Groq free tier, as reported and changing (plan Assumptions): limits are per model, per organisation, roughly 30 requests/min, ~1,000 requests/day, ~6–8k tokens/min and ~100–200k tokens/day. Over the limit is a 429 with `retry-after`. The API maps that to `503 assistant-unavailable` and passes `Retry-After` through (prompt 08), and the OpenAI SDK already retries 429s inside the 90 s budget.
- One question takes 2–3 model calls (`usage.modelCalls`) and about 3–10k tokens (`usage.inputTokens + outputTokens`).
- **Clocks.** The API runs on the harness's `FakeTimeProvider`, which is frozen, so the API's 90 s budget never fires here. Pacing and timeouts in the harness use **real** time (`TimeProvider.System` or `Stopwatch`). Give the harness `HttpClient` a `Timeout` of the row's `timeoutSeconds` + 30 s, and grade a timeout as a failure with reason `timeout`.

### Files
1. `matrix.json`:
   - Rows with `name`, `group` (`local`|`hosted`), `endpoint`, `model`, an optional `apiKeySecret`, an optional `requestsPerMinute`/`tokensPerMinute`, `timeoutSeconds` (local 110, Groq 60) and an optional `subsetOnly`.
   - Starter rows; the user confirms the tags at run time with `ollama list` and console.groq.com/docs/models:
     - `qwen3-30b-a3b-local` (`qwen3:30b-a3b`)
     - `qwen3-8b-local` (`qwen3:8b`)
     - `llama3.1-8b-local` (`llama3.1:8b`)
     - `gpt-oss-20b-local` (`gpt-oss:20b`)
     - `gpt-oss-120b-groq` (`openai/gpt-oss-120b`, `apiKeySecret: "Groq:ApiKey"`, `requestsPerMinute: 25`, `tokensPerMinute: 7000`, `subsetOnly: true`)
2. `Throttle.cs`: `internal sealed class Throttle(int? requestsPerMinute, int? tokensPerMinute, TimeProvider time)`.
   - A sliding 60 s window of `(at, calls, tokens)`.
   - `WaitAsync(ct)` waits until the window has room for the expected next question: the average calls and tokens of the last 3 questions; start with 3 calls and 6,000 tokens; cap the expectation at the limit. `Record(calls, tokens)` adds an entry.
   - Null limits mean no waiting.
3. `EvalRunner.cs` changes:
   - Before each question: `throttle.WaitAsync`.
   - On `503 assistant-unavailable`:
     - with `Retry-After` ≤ 600 s: wait it, then retry, up to 3 times;
     - without one: wait 60 s, then retry, up to 2 times;
     - with `Retry-After` > 600 s (a daily cap): stop the row and print `"<row>: daily limit reached; rerun with --resume after <time>"`.
   - Record usage after each answer.
4. `Report.cs`:
   - The JSONL checkpoint at `<out>/<row>.jsonl`: one `QuestionResult` per line, appended and flushed. Never include the key or the endpoint's credentials.
   - `--resume` skips ids that already have a non-error result.
   - `WriteMatrix(outDir)` reads every `*.jsonl` in the directory and writes `matrix.md`:
     - A table with Row, Model, Group, Questions, **Accuracy**, Lookup, Aggregate, History, Documents, Drafts, Safety, Taglish, Tool use (share with `ExpectedToolsUsed`), p50 s, p95 s, avg in tok, avg out tok, Errors.
     - Per-category cells read `passed/total`.
     - Rows run on the subset are marked `†`.
     - A footer with the date, `git rev-parse --short HEAD`, the `--hardware` text, temperature 0, the limits, and the note "all rows $0; tokens stand in for cost".
5. `Program.cs`:
   - `--rows <local|hosted|all|name,name>` with `--subset`, `--out <dir>` (default `docs/evals/results/<yyyy-MM-dd>`), `--resume` and `--hardware "<text>"`.
   - It loads `matrix.json` and skips a row whose `apiKeySecret` is missing from configuration, printing `skipped: set it with dotnet user-secrets set Groq:ApiKey … --project tests/Assistant.Evals`.
   - Configuration comes from `AddUserSecrets(typeof(EvalProgram).Assembly)` and `AddEnvironmentVariables()`.
   - Each row gets its own factory with `Assistant:Endpoint`, `Assistant:Model`, `Assistant:ApiKey` and `Assistant:TimeoutSeconds`.
   - After all rows it writes `matrix.md` and prints its path.
6. `tests/Assistant.Evals/README.md`, sections:
   - What it measures, and why it's a console app.
   - Prerequisites: Docker; Ollama plus `ollama pull` for each local row; a Groq account and a key via user-secrets. Only demo data is sent; Groq's zero-data-retention toggle is optional.
   - Commands: `--check`, `--validate-golden`, `--rows local`, `--rows gpt-oss-120b-groq --subset`, `--resume`, `--hardware "RX 6800 16 GB, 12 cores"`.
   - Time estimates: the local rows finish in one sitting; the Groq subset runs at about one question a minute, and the daily token cap may need a `--resume` the next day.
   - How grading works (D21), and how to add a row.

## Do not
- Commit or print any key. Run a live row inside `dotnet test` or CI. Add paid providers (plan D2).
- Touch files outside the `files` list. If you must, stop and report why.
- Add packages. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet run --project tests/Assistant.Evals -- --check
dotnet test
# with Ollama running and qwen3:30b-a3b pulled (skip and say so if it isn't):
dotnet run --project tests/Assistant.Evals -- --rows qwen3-30b-a3b-local --subset --out /tmp/claude-1000/evals-smoke
```
- The smoke run writes `/tmp/claude-1000/evals-smoke/qwen3-30b-a3b-local.jsonl` and `matrix.md`. Stopping it with Ctrl+C mid-run and rerunning with `--resume` skips the answered ids.
- `git grep -n 'gsk_'` prints nothing.
- 2026-11-21: 18 done (sonnet, 0 retries): matrix.json (5 rows), Throttle (sliding 60 s window, avg of last 3 questions), Report (JSONL checkpoint, --resume, matrix.md with † for subset-only rows), EvalRunner (real-time pacing, 503 retry/daily-limit stop, per-client timeout = row + 30 s, timeout graded as failure), Program (--rows/--subset/--out/--resume/--hardware, user-secrets + env), README. Smoke on qwen3:30b-a3b subset: 14/20, 20 JSONL lines, resume skipped all 20, Groq row skipped without key. Gotcha: HttpClient.Timeout can't change after the first request, so the runner signs in with its own client. --check holds; 753 tests green.
