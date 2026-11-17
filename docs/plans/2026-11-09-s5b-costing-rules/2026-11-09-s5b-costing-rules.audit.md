## Audit · 2026-11-09-s5b-costing-rules · 2026-11-09 · Codex (GPT-5) · PASS-WITH-CHANGES
Status: applied 2026-11-09: 2 applied, 0 disputed

Graph built: none (no `graphify-out/graph.json`) · Symbols traversed: 12 · Missed blast-radius items: 1

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|
| FIX | Breakage | The harness cannot meet its own all-or-nothing guarantee: it adds missing balances to its state before calling `PostingRules`, so a rejected first-touch operation leaves a new balance behind even though the ledger and result are uncommitted. | `prompts/s5b-costing-rules-03-vector-tests.md:44` creates state before the call; `:54-58` requires no commit on exception. | In prompt 03, stage missing balances in a per-operation working dictionary (or clone the balance map) and replace the harness state only after `PostingRules` returns. Add a rejected first-touch assertion proving balance state and movement count are unchanged. |
| FIX | Notes & constraints | The harness's claimed DB-CHECK assertion rejects a schema-valid zero-quantity revaluation. That will make the S10 extension fail despite matching the persisted constraint. | `prompts/s5b-costing-rules-03-vector-tests.md:69` requires `Quantity > 0`; `src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs:113` permits `Quantity = 0 AND TotalCost > 0`; the plan explicitly reserves those revaluations for S10 at `2026-11-09-s5b-costing-rules.md:128`. | Make the harness assertion exactly match the schema: `Quantity > 0 || (Quantity == 0 && TotalCost > 0)`, alongside `TotalCost >= 0` and distinct locations. |

**Bloat estimate:** planned ~12 files / ~1,000 LOC; minimal viable ~12 files / ~1,000 LOC. The pure domain model, focused test project, and three sequential prompts are proportionate to R1–R7; no speculative dependency or abstraction found.
**Model cost:** 01–02 → opus (high-stakes `Domain/**` ledger/money rules); 03 → sonnet (test-only harness and vectors).

### Prompt for Claude
Run: `/apply-review 2026-11-09-s5b-costing-rules IMS-7`

Or, without claude-config, paste:
~~~text
In /home/apa/Code/portfolio/inventory-management-system, update only the plan and prompt files for docs/plans/2026-11-09-s5b-costing-rules; do not implement production code, create or switch branches, commit, or push.

1. docs/plans/2026-11-09-s5b-costing-rules/prompts/s5b-costing-rules-03-vector-tests.md:44,54-58 — The LedgerHarness creates missing balances in persistent state before PostingRules runs, contradicting its requirement that an exception commits nothing. Specify that every operation stages missing balances in a local working copy and replaces harness state only after PostingRules succeeds. Add a rejected first-touch test/assertion that proves balance state and movement count do not change.
2. docs/plans/2026-11-09-s5b-costing-rules/prompts/s5b-costing-rules-03-vector-tests.md:69 — Its DB CHECK assertion incorrectly requires Quantity > 0. Match src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs:113: Quantity > 0 || (Quantity == 0 && TotalCost > 0), while retaining TotalCost >= 0 and From != To. This preserves S10's zero-quantity revaluation path.

Done when: the prompt remains self-contained, declares no new file touches or dependencies, and accurately represents the existing database constraint and atomic-harness contract.
~~~
