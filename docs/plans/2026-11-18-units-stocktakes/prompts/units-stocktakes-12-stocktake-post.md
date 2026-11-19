---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 12
unit: IMS-30
status: done
model: opus
effort: xhigh
depends_on: [11]
parallel_group: L
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Stocktakes/PostStocktake/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/PostStocktake/Handler.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentPoster.cs
    - src/Modules/Inventory/Inventory/Persistence/PostingCommand.cs   # D17 guard (Codex review fix, user-approved)
    - src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs    # D17 guard (Codex review fix, user-approved)
    - src/Modules/Inventory/Inventory/Features/Stocktakes/StocktakeEndpoints.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/StocktakeTests.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
    - docs/Inventory System — Sprint 1 Spec.md
  delete: []
---
# 12 · Post a stocktake exactly once (spec §6 change D10, approved)

## Task
Add `POST /api/stocktakes/{id}/post` (Admin). It posts the stocktake's snapshot deltas as **one** `CountCorrection` adjustment through `DocumentPoster` and `PostingEngine`, under the server-derived key `stocktake:<id>`, using the Draft → Posting → Posted state machine. Retries, refreshes and parallel clicks then produce exactly one document. Refactor `DocumentPoster` minimally to accept a key and an already-validated posting. Write the T17 tests and update the spec.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. `Features/Posting/**` is a High-stakes path, and a code-guardian review is required.
- Already in place:
  - Prompts 09–11: tables, start/list/get/count/cancel, `StocktakeReader` (line order by `Sku` with `StringComparer.Ordinal`, D14), `Problems.StocktakeNotOpen`, `StocktakeTests.StartAsync`.
  - Prompt 06: `PostingValidation.StocktakeKeyPrefix = "stocktake:"`. Client keys with that prefix are rejected.
- Plan decisions (**D10 approved by the user on 2026-11-18**):
  - **D9:** an `Adjustment` with `ReasonCode = AdjustmentReason.CountCorrection`.
    - Lines: every stocktake line with `delta = CountedQuantity − ExpectedQuantity ≠ 0`, in D14 order, numbered 1..n.
    - `ValidatedLine(n, ItemId, Quantity: delta, UnitCost: delta > 0 ? line.UnitCost : null, Unit: null)`.
    - `Reference = $"Stocktake {id.ToString("N")[..8].ToUpperInvariant()}"`; `Notes = stocktake.Notes`.
    - `PostedBy` = the posting Admin. `DocumentPoster` already uses `ICurrentUser`.
  - **D10:**
    - Key = `PostingValidation.StocktakeKeyPrefix + id.ToString("D")` (46 characters).
    - Hash = `RequestHash.Adjustment(validated)`. The lines are frozen once the status is Posting, so the hash is stable across retries.
  - **D13:** every line must be counted. Otherwise 400 with one error per uncounted line, `lines[j].quantity` "Count this item, or enter 0." (j = D14 index). Zero lines → 400 `lines` "Add at least one count."
    - More than `PostingValidation.MaxLines` (100) non-zero deltas → 400 `lines` "More than 100 counts differ; a stocktake can post at most 100 corrections."
    - `|delta| > MaxAmount` → 400 `lines[j].quantity`.
    - No differences → Posted with `PostedDocumentId = null` and no document.
  - **D14:** remap poster validation keys from document line index i (0-based, the `n − 1` of the posted line) to stocktake line index j: `lines[i].unitCost` → `lines[j].unitCost`, `lines[i].quantityDelta` → `lines[j].quantity`. Other keys pass through.
- Read first:
  - `Features/Posting/DocumentPoster.cs` in full: `PostAsync` at `:28-133`, `Shape` at `:136-152`, `ToResult` at `:154-191`.
  - `Persistence/PostingEngine.cs:29-114` (`FindExistingAsync`, `RunAsync`; a parallel duplicate becomes a replay) and `:116-145` (step 4 claims the key).
  - `Features/Posting/PostAdjustment/Handler.cs`.
  - `Features/Stocktakes/CancelStocktake/Handler.cs` (prompt 11) for the U-lock transaction pattern.

### 1. `DocumentPoster` (minimal refactor; behaviour unchanged)
- Split `PostAsync` at the point after `var requestHash = hash(validated);`. Move the rest of its body, from the step 2 lookup on, into:
  ```csharp
  /// <summary>Steps 2–10 for a posting already through step 1's pure checks, under <paramref name="key"/>. Stocktakes post
  /// here with a server-derived key (plan D10); PostAsync calls it after reading and checking the client's header.</summary>
  public Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> PostValidatedAsync(
      HttpContext http, PostingKind kind, string key, ValidatedPosting validated, byte[] requestHash, CancellationToken ct)
  ```
- `PostAsync` keeps its signature and calls `PostValidatedAsync(http, kind, key, validated, requestHash, ct)`. Every existing posting test must pass unchanged.

### 2. `PostStocktakeHandler`
Route `"/{id:guid}/post"`, `.RequireAuthorization(InventoryPolicies.Admin)`, empty body. Flow:
1. **Freeze.** In a strategy transaction (`ChangeTracker.Clear()` per attempt), lock the stocktake `WITH (UPDLOCK, ROWLOCK)`.
   - Missing → 404.
   - `Posted` → commit nothing and return 200 with the detail (idempotent).
   - `Cancelled` → `StocktakeNotOpen`.
   - `Draft`: run the D13 pre-checks on its lines (uncounted, empty, over 100, magnitude). On failure, return the 400 with no state change. Otherwise set `Status = Posting` and `UpdatedAt`/`UpdatedBy`, save and commit.
   - `Posting`: a crashed or parallel earlier attempt. Continue without changes.
2. **Build.** Read the frozen lines (no lock is needed: counts are refused while the status isn't Draft, prompt 11). Compute the deltas in D14 order. If there are no differences, go to step 4 with `documentId = null`.
3. **Post.** Call `poster.PostValidatedAsync(http, PostingKind.Adjustment, key, validated, RequestHash.Adjustment(validated), ct)`. Inspect `result.Result`:
   - `Created<DocumentResponse>` or `Ok<DocumentResponse>` (a replay): `documentId = value.Id`. Go to step 4.
   - `ProblemHttpResult` whose type is `idempotency-key-reused`: this is a bug, because the lines are frozen and the prefix is reserved. `throw new InvalidOperationException(...)`.
   - Any other `ValidationProblem` or `ProblemHttpResult` (`insufficient-stock`, `inactive-item`, `inactive-location`, `concurrency-conflict`, a remapped 400): nothing was written. **Unfreeze**: in a strategy transaction with the same U lock, `Posting` → `Draft` only if the status is still `Posting`, then save and commit. Return the problem, with a `ValidationProblem`'s error keys remapped per D14: build a new `Problems.Validation(remapped)`.
4. **Finish.** In a strategy transaction with the U lock: if the status is `Posting`, set `Posted`, `PostedDocumentId = documentId`, `UpdatedAt`/`UpdatedBy`, and commit. If it's already `Posted`, a parallel attempt finished first: do nothing. Return 200 with `StocktakeReader.ReadAsync(id)`.
- Comment the class with the D10 reasoning in three lines: the frozen lines give a stable hash; the key makes posting once-only; the status records the outcome, and a crash between steps 3 and 4 is healed by posting again.
- Register it, and map it in `StocktakeEndpoints`.

### 3. Tests (`StocktakeTests.cs`, appended; T17)
Each test uses its own tenant, an Admin client and a Staff client. Use `ScalarAsync` and `LedgerRows.OpenAsync` for raw reads.
- `Post_creates_one_count_correction_with_the_deltas`: X received 10 @ 5, Y received 4 @ 2. Count X 8 and Y 4, then post → 200, `status` Posted. `postedDocumentId` → `GET /api/documents/{id}`: type Adjustment, reasonCode CountCorrection, exactly one line (X, −2, cost 10.0000), reference starts with "Stocktake ". Balance X is 8 / 40.0000 and Y is unchanged.
- `Delta_is_measured_against_the_count_snapshot`: count X 8 (expected 10), **then** issue 3 through the API, then post → X ends at 5 (8 − 3), not 8.
- `Positive_delta_on_an_empty_balance_needs_a_unit_cost`: issue Y to 0. Count Y 2 with no `unitCost`; X is counted unchanged and sorts first. Post → 400 with the key `lines[<Y's index>].unitCost` (remapped, not 0). The status is back to `Draft`, and no document holds the key. Re-count Y with `unitCost` 3 and post → 200, and Y is 2 / 6.0000.
- `Uncounted_line_blocks_post`: a prefilled line left uncounted → 400 `lines[j].quantity`. The status stays Draft.
- `No_differences_posts_no_document`: everything counted equal → Posted, `postedDocumentId` null, and `SELECT COUNT(*) FROM inventory.StockDocuments WHERE IdempotencyKey = 'stocktake:<id>'` is 0.
- `Insufficient_stock_reverts_to_draft`: count X 0 (expected 10), then issue all 10 through the API, then post → 422 `insufficient-stock`, status Draft.
- `Posting_twice_is_idempotent`: post, post again → both 200 with the same `postedDocumentId`, and exactly one document holds the key.
- `Parallel_posts_make_one_document`: 5 parallel posts → all 200, the same `postedDocumentId`, one document.
- `Crash_between_post_and_finish_heals_on_retry`: post. Then, as the **owner** (sa, the fixture's admin connection; the stocktake tables have no triggers), set `Status = 'Posting', PostedDocumentId = NULL` to simulate the crash. Post again → 200, the same document id (a replay), `Posted`.
- `Count_while_posting_is_409`: set the status to `Posting` as owner, then a count → 409 `stocktake-not-open` with `stocktakeStatus` "Posting" (prompt 10: `status` is the ProblemDetails HTTP status member).
- `Over_100_differences_is_400`: insert 101 counted lines with non-zero deltas directly through a tenant scope (`ITenantScopeFactory.BeginScope`, `InventoryDbContext`), creating the items through EF too. Post → 400 `lines`, status Draft.
- `Staff_gets_403_on_post`: no `Location` header, and the status is unchanged.
- `Ledger_health_is_clean_after_stocktakes`: after the scenarios above in one tenant, `GET /api/ledger/health` reports every invariant passing. This proves the stocktake writes nothing but a normal posting.
- `TenantIsolationTests.cs`: `[ProbesRoute("POST /api/stocktakes/{id:guid}/post")]`, where B's id → 404.

### 4. Spec (`docs/Inventory System — Sprint 1 Spec.md`)
- §5: add the `Stocktakes` and `StocktakeLines` sketches (as in prompt 09) with the rule "Stocktakes are work documents, not ledger rows: they post through the engine as one `CountCorrection` adjustment."
- §6: under the idempotency step 2, add "Stocktakes post with the server-derived key `stocktake:<id>`; clients may not send keys with that prefix (plan D10)."
- §9: route rows for `GET /stocktakes`, `POST /stocktakes`, `GET /stocktakes/{id}`, `PUT /stocktakes/{id}/lines/{itemId}` (Staff, Admin), `POST /stocktakes/{id}/post` and `/cancel` (Admin), and a short bodies note.
- §11: add the row `T17 | Stocktake snapshot deltas, exactly-once post, crash heal, roles | Integration | D8–D15`.
- §15 Log: "2026-11-18: Sprint 3 D10 (approved): stocktakes (§5, §6, §9, T17)."

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Change `PostingEngine`, `PostingRules`, `RequestHash`, the adjustment route's behaviour or `ToResult`'s mappings.
- Lock balances in the handler; the engine does that.
- Add a document type or a ledger column.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*StocktakeTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingTests' --filter-class '*AdjustmentTests' --filter-class '*PostingConcurrencyTests' --filter-class '*LedgerHealthTests'
dotnet test
```
