
## Code review · IMS-30 · 2026-11-19 · Codex (GPT-6) · REQUEST CHANGES
Status: applied 2026-11-19: 1 applied, 0 disputed

Risk: high. The graph is stale (graphify-out/graph.json predates the latest commit); reviewed by source and call-site inspection.

**Verdict:** REQUEST CHANGES

**Blockers** (must fix before merge)
- `src/Modules/Inventory/Inventory/Features/Stocktakes/PostStocktake/Handler.cs:88` — When `PostValidatedAsync` reports `idempotency-key-reused`, the existing document was created from a different request hash. This can happen when one parallel post fails and unfreezes the stocktake, a count is changed, and another request that froze the old lines completes afterward. The handler then records the old document ID and marks the now-changed sheet Posted, so the final stocktake lines and `PostedDocumentId` describe different counts. Resolve the race so a document can only mark the exact frozen sheet that produced its hash as Posted; do not treat a mismatched hash as an ordinary successful replay.

**Should-fix**
- None.

**Nits**
- None.

### Prompt for Claude
Run: `/apply-review 2026-11-18-units-stocktakes IMS-30`

Apply the blocker through the prompt that owns the file:
- **Prompt 12** (`units-stocktakes-12-stocktake-post.md`) owns `src/Modules/Inventory/Inventory/Features/Stocktakes/PostStocktake/Handler.cs`. Fix the concurrent unfreeze/recount/post race so `PostedDocumentId` cannot be associated with a different frozen sheet hash. Add a regression test in `tests/Inventory.IntegrationTests/StocktakeTests.cs` that forces the interleaving and proves the stocktake cannot end Posted with a sheet that differs from the adjustment held by its server-derived key.
