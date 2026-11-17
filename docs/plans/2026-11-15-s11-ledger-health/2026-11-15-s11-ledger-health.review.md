## Code review · IMS-15 · 2026-11-16 · Codex (GPT-6) · REQUEST CHANGES
Status: applied 2026-11-16: 3 applied, 0 disputed (the projection half of Should-fix 1 stays: its per-key list is the D2 design, so only the failure rows are bounded; the nit case is unreachable through CK_StockBalances_Physical, so it has no test)

**Verdict:** REQUEST CHANGES

**Blockers**
- None.

**Should-fix**
- `src/Modules/Inventory/Inventory/Features/Ledger/GetLedgerHealth/Handler.cs:167` — `Take(MaxFailures)` runs after each query has materialized all failures, and `LedgerProjection.ReadAsync` also materializes every projected key. A badly drifted large tenant can make the diagnostic endpoint allocate memory proportional to all failing rows despite the 100-row response cap. Keep the full failure count while limiting retained detail rows to 100 (including the projection path); add coverage proving the count remains full and returned failures are capped. Owner: prompt 02 (`Health endpoint`); prompt 01 (`Ledger lock and projection`) for any projection API changes.
- `tests/Inventory.IntegrationTests/LedgerRebuildTests.cs:58` — the rebuild test verifies correcting integral ledger values (5 / 50); its only 4-decimal value is the deliberately corrupted `before` value. It therefore does not catch SQL parameter scale/rounding regressions when a rebuild writes fractional `OnHand`, `StockValue`, or `LastUnitCost`. Add a rebuild case with a valid four-decimal ledger-derived value and assert the stored value remains exact. Owner: prompt 03 (`Rebuild endpoint`).

**Nits**
- `src/Modules/Inventory/Inventory/Features/Ledger/GetLedgerHealth/Handler.cs:118` — when `OnHand` is zero and `StockValue` is negative, the earlier `ValueWithStock` arm wins, so the returned check says `ValueWithStock` instead of `ValueWithoutStock`. Put the zero-stock check first if the diagnostic check names are intended to distinguish these cases.

Graph note: graph stale (its timestamp predates the latest commit); I used grep and direct source inspection for the review.

### Prompt for Claude
Run: `/apply-review 2026-11-15-s11-ledger-health IMS-15`

Or, without claude-config, paste:
~~~text
In `/home/apa/Code/portfolio/inventory-management-system`, apply the two Should-fix findings in `docs/plans/2026-11-15-s11-ledger-health/2026-11-15-s11-ledger-health.review.md` for IMS-15. Follow the owning plan prompts: prompt 02 (`Health endpoint`) owns `src/Modules/Inventory/Inventory/Features/Ledger/GetLedgerHealth/Handler.cs`; prompt 01 (`Ledger lock and projection`) owns `src/Modules/Inventory/Inventory/Persistence/LedgerProjection.cs`; prompt 03 (`Rebuild endpoint`) owns `tests/Inventory.IntegrationTests/LedgerRebuildTests.cs`. Keep the health endpoint's full failure counts while retaining/returning at most 100 detail rows, including the projection check. Add a regression test proving a rebuild writes a four-decimal ledger-derived value without rounding. Do not edit other files beyond those owned by these prompts unless required by their existing declared files. Never create or switch branches, commit or push unless this is build-unit's PR fix loop. Run the plan's declared verification commands for the affected prompts.
~~~
