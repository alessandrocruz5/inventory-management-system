---
type: prompt
plan: "[[2026-11-15-s9-reversals]]"
id: 05
unit: IMS-13
status: todo
model: opus
effort: high
depends_on: [04]
parallel_group: E
files:
  create: []
  modify:
    - tests/Inventory.IntegrationTests/PostingConcurrencyTests.cs
  delete: []
---
# 05 · Reversal races (T4, index race, latest-only under the locks)

## Task
Add the reversal concurrency facts to `PostingConcurrencyTests`: T4's two parallel same-key reversals (spec §11), two parallel different-key reversals, the deterministic `UX_StockDocuments_Reverses` race (plan D5, the IMS-8 nit), and the deterministic latest-only race that proves the check runs under the balance locks (plan R3, D6; spec audit FIX `:23(a)`). Tests only; if one exposes a product bug, stop and report it with the failing assertion.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-13`. The route and its tests exist (prompts 03–04). Reuse `ReversalTests.ReverseAsync(client, documentId, key, ct)`, `ReversalTests.IdAsync`, `ReversalTests.BalanceAsync(connection, item, location, ct)` → `(OnHand, StockValue, LastUnitCost)?`, `ReversalTests.AssertProblemAsync`, and `LedgerInvariants.AssertHoldAsync` (INV-1 to INV-5).
- The reversal path (plan D3, D5–D7): handler → step 2 lookup → `404` → `422` reversal-of-reversal → `409 already-reversed` pre-check (an RCSI read: it can't see an uncommitted reversal) → engine: step 3 shared applock, step 4 insert of the `Reversal` document (claims the key **and** `UX_StockDocuments_Reverses (TenantId, ReversesDocumentId)`), step 6 `UPDLOCK` on the original's balances in clustered-key order, step 7 inactive checks, step 8 latest-only against the locked rows' `LastMovementId`, step 9 writes, commit. A 2601/2627 at step 4 → same key replays `200`; another key whose original now has a reversal → `409 already-reversed` with `reversalDocumentId`.
- Patterns in this file to copy: T4 `Two_parallel_same_key_issues_of_the_last_unit` (`:122-145`; `InParallelAsync`, `Count`, `Statuses`, `IdAsync`, `DocumentsWithKeyAsync`); the deterministic step-4 races `Race_on_the_key_replays_when_the_holder_commits` / `…_rolls_back` (`:171-234`: holder = `LedgerRows.OpenAsync(db, tenant, ct)` + `BeginTransaction()`, request in `Task.Run`, `LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx)`, then commit or roll back); the step-7 race (`:237-267`); helpers `LockBalanceAsync` (`:560-571`), `DocumentsOfTypeAsync` (`:613-615`), `MovementsOfDocumentAsync`.
- `LedgerRows` (raw rows as `inventory_app` on the tenant connection; `TenantId` comes from the session-context default): `AddDocumentAsync(c, ct, tx, type: "Reversal", reverses: originalId, idempotencyKey: k)` (hash = `HASHBYTES('SHA2_256', key)`), `AddMovementAsync(c, documentId, lineNo, item, from, to, qty, cost, ct, tx)` → id from `MovementIds`, `LocationIdAsync(c, "CUS", ct, tx)`. `inventory_app` may `UPDATE inventory.StockBalances` (not documents or movements).
- Update the class summary: replace "T4's parallel same-key reversals land with the reversal route in S9." with a sentence naming the reversal races.

### Facts (append before the private helpers)
1. `Two_parallel_same_key_reversals_give_one_document` (T4): receive 5 @ 10 at A (receipt R). `InParallelAsync(2, () => ReversalTests.ReverseAsync(client, R, key, ct))` → one `201`, one `200` with `Idempotent-Replayed: true`, same id; `DocumentsOfTypeAsync(connection, "Reversal")` = 1; A `(0, 0, null)`; invariants hold.
2. `Two_parallel_reversals_with_different_keys_reverse_once`: same setup; `InParallelAsync(2, i => ReversalTests.ReverseAsync(client, R, PostingTests.NewKey(), ct))` → one `201` and one `409 already-reversed` whose `reversalDocumentId` is the `201`'s id (the loser may hit the pre-check or the index; both answer the same); one `Reversal` document; invariants hold.
3. `Race_on_the_reversal_index_is_409_when_the_holder_commits`: receive at A (R). Holder transaction: `var held = await LedgerRows.AddDocumentAsync(holder, ct, tx, type: "Reversal", reverses: R, idempotencyKey: PostingTests.NewKey());` (no movements). Start `ReverseAsync(client, R, otherKey, ct)` in `Task.Run`; `WaitUntilBlockedAsync`; commit. → `409 already-reversed`, `reversalDocumentId` = `held`; `DocumentsWithKeyAsync(holder, otherKey, ct)` = 0; A unchanged; invariants hold (the holder's reversal has no movements, and the invariants read only movements and balances).
4. `Race_on_the_reversal_index_posts_when_the_holder_rolls_back`: same, roll back → `201`; A `(0, 0, null)`; one `Reversal` document; invariants hold.
5. `Posting_committed_while_a_reversal_waits_makes_it_not_latest` (R3): receive 10 @ 10 at A (R0), issue 1 at A through the API (so the item's CUS balance exists), receive 5 @ 10 at A (R, now the latest at A: A = 14 / 140). Holder transaction, playing a raw issue of 1 at A that commits first:
   - `LockBalanceAsync(holder, tx, item, A, ct)` (U-lock A's row);
   - `var issue = await LedgerRows.AddDocumentAsync(holder, ct, tx, type: "Issue");` `var cus = await LedgerRows.LocationIdAsync(holder, "CUS", ct, tx);` `var m = await LedgerRows.AddMovementAsync(holder, issue, 1, item, A, cus, 1m, 10m, ct, tx);`
   - `UPDATE inventory.StockBalances SET OnHand = OnHand - 1, StockValue = StockValue - 10, LastMovementId = @m WHERE ItemId = @item AND LocationId = @a;` and `UPDATE inventory.StockBalances SET OnHand = OnHand + 1, LastMovementId = @m WHERE ItemId = @item AND LocationId = @cus;` (each must affect 1 row). `LastUnitCost` is untouched: an issue doesn't change it.
   Start `ReverseAsync(client, R, key, ct)` in `Task.Run`; `WaitUntilBlockedAsync` (it waits at step 6 on A's lock); commit. → `409 reversal-not-latest`, `locationId` = A, `itemId` = item, `blockingDocumentId` = `issue`; no `Reversal` document; A `(13, 130, 10)`; invariants hold (the holder's rows are a consistent issue).
6. `Posting_rolled_back_while_a_reversal_waits_lets_it_reverse`: same setup and holder, roll back → `201`; one `Reversal` document; A `(9, 90, 10)`; invariants hold.

   The numbers: R0 (10 @ 10) → A 10 / 100; the API issue of 1 → 9 / 90; R (5 @ 10) → 14 / 140. Fact 5's committed holder issue → 13 / 130, LUC still 10. Fact 6 undoes R → 9 / 90, and LUC is the fold without R: the last unreversed inflow is R0, which left 10 / 100, so `Round4(100 / 10)` = 10.

## Do not
- Touch files outside the `files` list. Don't move or rename existing helpers; add new private helpers at the end only if needed.
- Use `UPDATE`/`DELETE` on `StockDocuments`/`StockMovements` (they're denied to `inventory_app` anyway).
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingConcurrencyTests'   # run it 3 times: the races must be deterministic
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingConcurrencyTests'
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingConcurrencyTests'
dotnet test            # full suite
git diff --stat        # only PostingConcurrencyTests.cs
```
