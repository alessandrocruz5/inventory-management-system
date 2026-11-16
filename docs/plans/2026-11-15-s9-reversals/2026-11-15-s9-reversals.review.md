## Code review · IMS-13 · 2026-11-15 · Codex (GPT-6) · APPROVE
Status: approved 2026-11-15

**Verdict:** APPROVE

**Blockers**
- None.

**Should-fix**
- None.

**Nits**
- None.

Risk: high, because the diff changes the ledger persistence path. The shared lock/read/write helpers preserve the existing posting behavior; reversal balance writes occur under the step-6 locks, latest-only checks use the locked `LastMovementId`, and movement/document writes remain append-only. The direct callers, route mapping, response contract, tenant probe, web button, and reversal-specific same-key/different-key races were traced. The updated spec slug list is within R5. No graph exists; direct grep and diff review found no hidden-coupling break.

### Prompt for Claude
Nothing to apply.
