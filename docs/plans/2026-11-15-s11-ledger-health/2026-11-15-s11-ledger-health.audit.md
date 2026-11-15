## Audit · 2026-11-15-s11-ledger-health · 2026-11-15 · Codex (GPT-6) · PASS-WITH-CHANGES
Status: applied 2026-11-15: 2 applied, 0 disputed

Graph built: 2026-11-15 21:38 +0800 (matches latest commit; working tree has only plan bookkeeping/new plan files) · Symbols traversed: 0 · Missed blast-radius items: 0

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|
| FIX | Usability | Prompt 04 demands identical outcome counts across two runs even though its concurrent burst submits balance-dependent operations with nondeterministic transaction ordering. A fixed `Random` seed does not fix server scheduling, so this can create a flaky acceptance criterion. | `prompts/s11-ledger-health-04-t9-random-run.md:40-49,74-77` | Make the concurrent burst operations independent of order (or exclude their status counts from deterministic count assertions); keep the seed and invariant assertions. State that deterministic counts apply only to the sequential portion. |
| FIX | Usability | The project requires every `## Commands` line. Its command is plain `dotnet test`, but the plan runs only `dotnet test --max-parallel-test-modules 1`. The latter avoids the known Testcontainers startup crash, but does not literally run the declared command, leaving R7's coverage ambiguous. | `CLAUDE.md` `## Commands`; `TASKS.md:102`; plan `:175-192` | Reconcile the verification requirement with the known flake: explicitly record the serialized command as the approved equivalent for this slice, or add a safe way to execute the declared command and explain the retry/flake handling. Do not silently claim both commands were run. |

**Bloat estimate:** planned ~25 files / ~900 LOC; minimal viable ~20 files / ~700 LOC. Most planned files are justified by separate API, persistence, test and UI layers; the additional dedicated projection/rebuild/invariant tests are proportionate for this ledger-health slice.
**Model cost:** 01–04 opus is justified by high-stakes persistence, SQL invariants, concurrent rebuild behavior and T9; 05–06 sonnet is appropriate for conventional Angular service/guard/screen work.

### Prompt for Claude
Run: `/apply-review 2026-11-15-s11-ledger-health`

Or, without claude-config, paste:
~~~text
Repo: /home/apa/Code/portfolio/inventory-management-system
Plan: docs/plans/2026-11-15-s11-ledger-health/2026-11-15-s11-ledger-health.md

Apply these findings:
- prompts/s11-ledger-health-04-t9-random-run.md:74-77: concurrent request scheduling is nondeterministic, so a fixed random seed cannot guarantee identical outcome counts. Make concurrent operations independent of order or exclude burst outcomes from deterministic count assertions; limit deterministic counts to the sequential run.
- CLAUDE.md ## Commands; TASKS.md:102; plan:175-192: the declared `dotnet test` command is not run; only its serialized variant is. Align the plan's R7 verification with the repository's known Testcontainers flake and state the serialized command as the accepted equivalent, or define safe handling for running the exact command. Do not claim the plain command ran unless it did.

Edit the plan/prompt files needed for these findings and the plan Log. Do not change code, TASKS.md, CHANGELOG.md or _index.md for this audit application. Never create or switch branches, commit or push unless this is build-unit's PR fix loop. Done when both findings are resolved in the plan/prompt and the planned verification commands remain runnable.
~~~
