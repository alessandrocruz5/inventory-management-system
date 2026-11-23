# Assistant evaluation harness

Scores open-weight models on the assistant's golden question set (52 questions, 20 of them the *subset*), through the real
`POST /api/assistant/chat` and the real tools, over a fixed ledger scenario. Answers are graded deterministically against
ledger ground truth: no LLM judge.

## What it measures

Per model row: accuracy overall and per category (lookup, aggregate, history, documents, drafts, safety, taglish), the share of
questions where the expected tools were used, latency (p50, p95), average input and output tokens, and errors. Every row is $0,
so tokens stand in for cost.

It is a console app, not a test project, on purpose: a live row takes minutes to hours and needs a model, so `dotnet test` and
CI never run one. CI runs only `--check`, which calibrates the grader with scripted models (oracle, near-miss, silent).

## Prerequisites

- Docker (the harness starts a SQL Server 2025 container with Testcontainers).
- Local rows: [Ollama](https://ollama.com) running, and `ollama pull <tag>` for each row you run (tags are in `matrix.json`).
  Confirm them with `ollama list`.
- Hosted row: a Groq account and a key, stored in user-secrets. The key is never written to a file, a log or a result:

  ```bash
  dotnet user-secrets set Groq:ApiKey <your key> --project tests/Assistant.Evals
  ```

  The environment variable `Groq__ApiKey` works too. Without a key the Groq row is skipped. Only the seeded demo scenario is
  sent to Groq. Its zero-data-retention toggle is optional. Check current model ids at console.groq.com/docs/models.

## Commands

Run from the repository root.

```bash
dotnet run --project tests/Assistant.Evals -- --check              # calibrate the grader (CI runs this)
dotnet run --project tests/Assistant.Evals -- --validate-golden    # validate golden.json and resolve every expected number
dotnet run --project tests/Assistant.Evals -- --rows local         # every local row, full set
dotnet run --project tests/Assistant.Evals -- --rows gpt-oss-120b-groq --subset
dotnet run --project tests/Assistant.Evals -- --rows local --resume --out docs/evals/results/2026-11-21
dotnet run --project tests/Assistant.Evals -- --rows local --hardware "RX 6800 16 GB, 12 cores"
```

`--rows` takes `local`, `hosted`, `all` or row names separated by commas. `--out` defaults to `docs/evals/results/<yyyy-MM-dd>`.
Each answer is appended to `<out>/<row>.jsonl` as it arrives, and `matrix.md` is rewritten from every `*.jsonl` in the folder when
the run ends. A row whose `.jsonl` already exists needs `--resume` (it skips questions that already have a non-error result and
reruns errors and timeouts) or another `--out`. Keep a `--subset` run and a full run in different `--out` folders.
Ctrl+C stops cleanly; rerun with `--resume`.

## Time

- Local rows: the full set finishes in one sitting. A row takes roughly 15 to 60 minutes depending on the model and the GPU.
- Groq free tier: the subset runs at about one question a minute, because the harness paces itself to the row's
  `requestsPerMinute` and `tokensPerMinute`. The daily token cap may stop a run: it prints
  `<row>: daily limit reached; rerun with --resume after <time>`. Wait, then resume.

The Groq limits change, so treat the numbers in `matrix.json` as a starting point.

## How grading works

A question passes when all of these hold (the shape is documented at the top of `GoldenSet.cs`):

- every expected number appears in the reply (money within half a centavo at 2 dp, quantities exactly; `1`, `1.00` and `1.0000` are equal);
- every `mentions` group has at least one of its words in the reply, and no `notMentions` word does;
- an expected draft matches the one the assistant returned (type, locations, reason, lines, quantities, units, costs);
- no forbidden tool was called, no draft was made when `noDraft` is set, and the ledger did not change during the question;
- the API answered 200 within the row's timeout (a timeout is a failure with reason `timeout`).

Which tools were used is reported (`Tool use`) but is not part of pass or fail. Rows run on the subset only are marked `†` in
`matrix.md`, and are not comparable with full-set rows.

## Adding a row

Add an object to `matrix.json`: `name`, `group` (`local` or `hosted`), `endpoint` (an OpenAI-compatible base URL), `model`,
`timeoutSeconds` (at most 110), and optionally `apiKeySecret` (a configuration key such as `Groq:ApiKey`, never the value),
`requestsPerMinute`, `tokensPerMinute` and `subsetOnly`. Then run it by name with `--rows <name>`.
