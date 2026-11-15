## Audit · 2026-11-15-s9-reversals · 2026-11-15 · Codex (GPT-6) · PASS
Status: approved 2026-11-15

Graph built: none · Symbols traversed: 22 · Missed blast-radius items: 0

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|

**Independent blast radius:** `PostingEngine` is used by the four posting handlers and the opening-stock seeder; the plan preserves their command path and lists the regression suites. The reversal route adds the existing route guard probe, uses the tenant filtered EF context, and returns the established document response. The schema's unique idempotency and filtered reversal indexes match the plan's step-4 conflict handling. Document detail and item history already expose the reversal links and movement rows needed by the new response. The domain `PostingRules.Reverse` already enforces latest-only checks and recomputes last unit cost from history. Hidden coupling checked: ProblemDetails slugs, RequestHash canonical forms, tenant filtering/RLS, trigger and ledger constraints, and the S10/S11/S12 downstream notes. No unmitigated break found.

**Check results:** 1 Breakage — pass; 2 Usability — pass; 3 Bloat — pass; 4 Optimization — pass; 5 Notes & constraints — pass; 6 Requirements coverage — pass; 7 Prompt independence — pass; 8 Unit shape — pass; 9 Model fit — pass.

**Bloat estimate:** planned ~22 files / ~650 LOC; minimal viable ~22 files / ~600 LOC. Most added files are required test coverage across the API, concurrency, and UI contracts; no speculative abstraction or package is proposed.
**Model cost:** 01, 03, 05 → `opus` (ledger write path, route contract and concurrency); 02 → `opus` (shared Kernel and RequestHash contract); 04, 07 → `sonnet` (integration coverage and UI behavior); 06 → `haiku` (mechanical mappings and service method).

### Prompt for Claude
Nothing to apply.
