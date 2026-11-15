---
type: plan
kind: feature
status: audited
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-15
risk: high
prompts: 7
graph_built: none
tags: [plan]
---
# S9 Reversals (IMS-13)

## Request
> IMS-13

This plans the existing unit `IMS-13 — S9 Reversals · Added · spec: §12 S9 · depends: IMS-10, IMS-11 · model: opus` (TASKS.md, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S9: *"Reversal endpoint (checks at step 7, `LastUnitCost` recomputed from the ledger), Reverse button. Tests: T8; T1 API run of V · code-guardian review. Tier: Should"*.

Spec §9 row: *"`POST /documents/{id}/reversal` · Admin · Requires `Idempotency-Key`; empty body"*. Problem types `reversal-not-latest` (409, `itemId`, `locationId`, `blockingDocumentId`) and `already-reversed` (409, `reversalDocumentId`).

Spec §6 reversal rules (`:328-333`): Admin only; the original is not a reversal and not already reversed (the filtered unique index enforces the second); latest-only per physical balance (`LastMovementId` = the original's last movement on it), else `409 reversal-not-latest`; checks at step 7 under the balance locks, then step 8 like any posting (`422 insufficient-stock`); each reversal movement copies `Quantity` and `TotalCost`, swaps the locations and sets `ReversesMovementId`; `LastUnitCost` is recomputed from the ledger. Hard rule (`:324`): inactive items and locations accept no reversals.

Spec §11: T8 *"Reverse a receipt, an issue and a transfer; reverse twice; reverse a reversal; reverse a non-latest → exact restore of `OnHand`, `StockValue` and `LastUnitCost` (V7: B back to 120.0000); the three rejections"*; T1 *"one API run each"* of set V; T4 *"two parallel same-key reversals"*.

It also covers the S9 items earlier units locked:
- IMS-7 (S5b): map `ReversalNotLatest` → `409 reversal-not-latest` with `blockingDocumentId`; call `PostingRules.Reverse` at step 8 with balances from the step-6 lock read and the step-7 `IsVirtual`/`AllowNegative`; the engine loads each touched physical balance's history through `IX_Movements_Item`.
- IMS-8 (S5c) deferred nit: *"a 2601 on `UX_StockDocuments_Reverses` would look like a lost key, and needs a 409 `already-reversed`"*.
- IMS-10 (S6) plan Downstream: a reversal passes `ReasonCode: null`, including for an adjustment; reuse `LedgerInvariants.AssertHoldAsync` and add INV-4.
- IMS-11 (S7) plan Downstream: the Reverse button (Admin) goes on `documents/document-detail`, shown when `reversedByDocumentId` is null and the type isn't `Reversal`; after a reversal, reload the detail.
- IMS-12 (S8) plan Downstream: a reversal shows in item history as type Reversal with the swapped direction; T8 can assert it via `GET /items/{id}/movements`.

| # | Requirement |
|---|---|
| R1 | `POST /api/documents/{id}/reversal` (Admin; Staff `403`; `Idempotency-Key` required; empty body) posts a `Reversal` document that mirrors every movement of the original (From/To swapped, same `Quantity` and `TotalCost`, `ReversesMovementId` set, same `LineNo`) and restores every touched balance's `OnHand`, `StockValue` and `LastUnitCost` exactly, with `LastUnitCost` recomputed from the ledger. `201` with `Location` and the posting response; replay `200` + `Idempotent-Replayed: true`. |
| R2 | Rejections: unknown or foreign id `404 not-found`; original is a reversal `422 cannot-reverse-reversal` (`reversesDocumentId`); already reversed `409 already-reversed` (`reversalDocumentId`), including when two reversals race (the `UX_StockDocuments_Reverses` 2601 path); not latest `409 reversal-not-latest` (`itemId`, `locationId`, `blockingDocumentId`); inactive item or location `422 inactive-item`/`inactive-location`; short stock `422 insufficient-stock`; key reused `409 idempotency-key-reused`; retries exhausted `409 concurrency-conflict`. A rejection writes nothing and doesn't consume the key. |
| R3 | The latest-only, inactive and short-stock checks run inside the posting transaction, under the step-3 ledger lock and the step-6 balance locks, so a posting that commits while a reversal waits is seen by it (spec §6 `:332`; spec audit FIX `:23(a)`). |
| R4 | `/documents/:id` shows a **Reverse** button to Admins when the type isn't `Reversal` and `reversedByDocumentId` is null, with a confirm step, an `Idempotency-Key` reused on retry, and Submit disabled while in flight. After success the detail reloads and links to the reversal; `reversal-not-latest` and `already-reversed` show a message with a link to the blocking document or the reversal. |
| R5 | New and mapped slugs land in spec §9, Kernel `ProblemTypes` and Angular `PROBLEM_MESSAGES` in the same PR (IMS-3 Downstream; CLAUDE.md Hidden coupling). |
| R6 | Tests: T8 (receipt incl. two lines, issue, transfer, adjustment; twice; reversal of a reversal; non-latest; exact `OnHand`/`StockValue`/`LastUnitCost`); T1 API run of set V (V1–V10); T4 parallel same-key reversals; the different-key race and the latest-only race under the locks; INV-4 in `LedgerInvariants`; T5 probe; T10 for a reversal; `RequestHash` pin; Vitest for the service, messages and button. |
| R7 | Every `## Commands` line and CI `build` and `test` are green; code-guardian reviews the PR (High-stakes paths `Persistence/**`, `Features/Reversals/**`; CLAUDE.md lists S9). |

## Context used
- Note: `CLAUDE.md` (Conventions; Review constraints: decimal, no UPDATE/DELETE on the ledger tables, `TenantId` never from a request, `PostedAt` only from `TimeProvider`, ask before changing §6 or adding a ledger column; High-stakes paths; Hidden coupling: slugs ↔ `PROBLEM_MESSAGES`, `RequestHash` canonical form, `HasTrigger`).
- Note: spec §5 (`StockDocuments.ReversesDocumentId` set iff `Type = 'Reversal'`; `UNIQUE (TenantId, ReversesDocumentId) WHERE NOT NULL`; `StockMovements.ReversesMovementId`; `LineNo` 1000 + n for revaluations), §6 (`:302` reversal cost row; steps 1–10 `:305-316`; hard rules `:320-326`; reversal rules `:328-333`; INV-4 `:340`), §7 (`:380-390` reversal costing and the ledger definition of LUC; set V `:395-408`), §9 (`:545`, `:578-590`), §10 (`:604` "detail with lines, costs, Reverse button, link to the reversal"), §11 (T1, T4, T5, T8, T10), §12 S9.
- Note: spec audit `Inventory System — Sprint 1 Spec.audit.md:13` (step-4 key claim before stock checks, so a same-key duplicate replays, never `409 reversal-not-latest`), `:17` (LUC is ledger-derived; reversals restore it), `:23` (latest-only check under the locks; toggle-off → 422; reversals into inactive locations).
- Note: TASKS.md Downstream for IMS-3 (slug in §9 + Kernel + Angular in one PR; execution strategy), IMS-4 (T5 probe per route; raw SQL only through EF), IMS-5 (`Features/<Area>/<Endpoint>/` layout; route raw text), IMS-6 (documents and movements final at insert; `MovementIds`; lock order; `[LineNo]` reserved; read the ledger without U locks; Testcontainers flake), IMS-7 (above; restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1`), IMS-8 (above; the applock `-3` nit; decimal scale differs between a 201 and a replay).
- Note: S5b plan `2026-11-09-s5b-costing-rules.md:98-111` (`Reverse` folds LUC over the history minus the original; a reversal movement copies `LineNo`; document-level checks stay in S9; `Reverse` leaves `LastMovementId` to the engine; histories via `IX_Movements_Item`), `:216` (`Reverse` skips the short check under `AllowNegative`).
- Note: S6 plan Downstream `:265-269`; S7 plan Downstream `:223-228`; S8 plan Downstream `:174-178`.
- Graph: none (`graphify-out/` does not exist); blast radius from grep (workflow §4).
- Code: `Persistence/PostingEngine.cs:28-59` (`FindExistingAsync`), `:65-89` (`PostAsync`: strategy, lost-key replay, `RetryLimitExceededException`, `ChangeTracker.Clear`), `:92-300` (`AttemptAsync`: steps 3–10; `:102-109` applock; `:111-133` document insert and 2601/2627; `:160-185` the step-6 lock statement and the items × locations count check; `:187-222` step 7; `:254-295` step 9), `:306-341` (helpers). `Persistence/PostingCommand.cs:11-53` (command and outcomes). `Domain/PostingRules.cs:149-278` (`Reverse`: argument checks, latest-only per physical key, `InsufficientStock` unless `AllowNegative`, mirrors in id order, LUC fold minus the original). `Domain/PostingRejections.cs:26-35` (`ReversalNotLatestException(ItemId, LocationId, BlockingMovementId)`). `Domain/CostingPolicy.cs:58-110` (`LastUnitCost`). `Domain/LedgerMovement.cs`.
- Code: `Features/Posting/DocumentPoster.cs:29-128` (key → validate → hash → step 2 → DB checks → engine; `:151-183` outcome mapping), `Features/Posting/DocumentEndpoints.cs`, `Features/Posting/PostAdjustment/Endpoint.cs:10-14` (Admin route pattern), `Features/Posting/DocumentResponse.cs`, `Features/Posting/RequestHash.cs` (forms, `Hash`), `Features/Posting/PostingValidation.cs` (`IdempotencyKey`), `Features/Problems.cs`, `Features/InventoryPolicies.cs`, `InventoryModuleExtensions.cs:48-79`, `Persistence/InventoryDbContext.cs` StockDocument config (`CK_StockDocuments_Reversal`, `CK_StockDocuments_ReasonCode`, `UX_StockDocuments_Reverses`), `Seeding/InventorySeeder.cs:189-226` (calls `FindExistingAsync`/`PostAsync`; `case var outcome` throws), `src/Shared/Kernel/Problems/ProblemTypes.cs`.
- Tests: `PostingTests.cs:556-600` (`PostDocumentAsync`, `Receipt`, `Line`, `Issue`, `IssueLine`, `BalanceAsync`, `NewKey`, `ReceiveAsync`, `NewLocationAsync`, `NewItemAsync`, `AddUserAsync`), `TransferTests.cs:17,342-345`, `AdjustmentTests.cs:17,20-38,437-440`, `PostingConcurrencyTests.cs` (`:122-145` T4 pattern, `:171-234` deterministic step-4 races, `:237-267` step-7 race, `:497-636` helpers), `LedgerRows.cs` (`OpenAsync`, `LocationIdAsync`, `AddDocumentAsync(type, reverses, key)`, `AddMovementAsync`, `WaitUntilBlockedAsync`), `LedgerInvariants.cs`, `PostingVectorTests.cs`, `PostingClockTests.cs:23-66`, `TenantIsolationTests.cs:24-36,343-361,457-489,650-705` (Exempt map, 404 probe, route guard), `tests/Inventory.UnitTests/RequestHashTests.cs:95-111`, `CostingVectorTests.cs` (V7/V8 in the harness).
- Web: `documents/document-detail.{ts,html,spec.ts}`, `documents/documents.service.{ts,spec.ts}`, `documents/posting-form.ts:172-190` (`IdempotencyKey`), `shared/confirm-dialog.ts`, `items/items.ts:120-145` (confirm-dialog usage), `core/auth/auth.service.ts:20` (`isAdmin`), `core/problems/problem-messages.{ts,spec.ts}`.

## Options
| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| A. Extract steps 3/4/6/7/9 of `AttemptAsync` into private helpers; add `ReverseAsync` beside `PostAsync` | ~2 files, moderate diff to proven code | 1 prompt | Medium: refactor of the hot path, caught by T2/T3/T4/seed/step-7 suites | One copy of applock, lock-order and write SQL; S10/S11 change it once | Ledger contract unchanged |
| B. Separate `ReversalEngine` copying those steps | ~2 files, no change to `AttemptAsync` | 1 prompt | Low now, rises later | Two copies of the lock-order SQL to keep in sync (S10, S11) | Drift risk on a §6 lock rule |
| C. Widen `PostingCommand` into a posting/reversal union and branch inside `AttemptAsync` | ~2 files, many branches | 1 prompt | Medium–high: one long method with two document shapes | Hardest to review | — |

**Recommendation:** A, because §6 makes the lock order a single rule, and one copy of it is what code-guardian and S10/S11 should review. **Chosen:** A (user, 2026-11-15).

## Decisions
- **D1 · Engine structure: shared steps, two attempts (user, 2026-11-15).** `PostingEngine` keeps `FindExistingAsync`, `PostAsync` and `AttemptAsync`'s behaviour byte for byte, and gains `ReverseAsync(ReversalCommand)`. The step SQL moves into private helpers that both attempts call: `TakeLedgerLockAsync` (step 3), `TryInsertDocumentAsync` (step 4), `LockBalancesAsync` (step 6 statement), `ReadMasterDataAsync` (step 7 reads), `WriteAsync` (step 9), and `RunAsync` (the execution-strategy wrapper, lost-key fallback, `RetryLimitExceededException` → `RetriesExhaustedOutcome`, `ChangeTracker.Clear`). The SQL text, hints and comments move unchanged.
- **D2 · Reversing a reversal is `422 cannot-reverse-reversal` (user, 2026-11-15).** Extra field `reversesDocumentId` (the document the target already reverses). It is a permanent property of the target, so 422 like the other business-rule rejections. Added to spec §9, Kernel `ProblemTypes` and `PROBLEM_MESSAGES` (R5). §6 is not changed: it already forbids the action.
- **D3 · Request order.** Admin policy (`403`) → `Idempotency-Key` (`400 idempotency-key-missing` / `400 validation`) → hash → step 2 lookup (replay `200` or `409 idempotency-key-reused`) → original exists (`404`) → original is a reversal (`422 cannot-reverse-reversal`) → already reversed (`409 already-reversed`, pre-check) → original's movements → engine. The replay lookup runs before every database check, so a retry of a successful reversal replays even after the state changed (S5c D2).
- **D4 · `RequestHash` reversal form.** SHA-256 over UTF-8 `"POST /api/documents/{id}/reversal\n{}"`, `id` lowercase `D`. The body is empty (§9), so its canonical form is `{}`; anything a client sends in the body binds to nothing and is ignored. The four existing forms are untouched (Hidden coupling). The same key reused for another original, or for any other route, has a different hash → `409 idempotency-key-reused`.
- **D5 · The `already-reversed` race goes through the unique index, no message parsing.** Step 4 inserts the reversal document with `ReversesDocumentId`. A racing reversal blocks on `UX_StockDocuments_Reverses` (or on the key index) and gets 2601/2627 once the holder commits; the attempt returns null (as today). `ReverseAsync`'s fallback then tries `FindExistingAsync(key, hash)` (same key → replay) and, failing that, reads the original's reversal → `AlreadyReversedOutcome(reversalDocumentId)`. If neither exists, it throws (a bug). This closes the IMS-8 nit.
- **D6 · The latest-only check runs at step 8 on the step-6 locked rows.** `PostingRules.Reverse` compares each physical balance's `LastMovementId` (read by the step-6 `UPDLOCK` statement) with the original's last movement on it. The locks are held from step 6 to commit, so this is the §6 "checks at step 7, under the balance locks" with the pure rule doing the comparison (S5b design). Step 7 checks inactive items and locations first. `ReversalNotLatestException.BlockingMovementId` is resolved to its document inside the transaction → `ReversalNotLatestOutcome(itemId, locationId, blockingDocumentId)`.
- **D7 · Reversal lock set.** No step 5 (the original created every touched balance). Step 6 runs the same statement over the distinct items × locations of the original's movements, then asserts that **every touched key** is among the locked rows (not the cross-product count `AttemptAsync` asserts). Only touched rows are passed to `Reverse`, updated and returned. Once S10 adds revaluations against `ADJ`, the cross product can include rows no movement touches; they are locked in key order, never changed.
- **D8 · Histories.** For each physical touched key, the engine reads every movement of the item that touches the location (`IX_Movements_Item`), after the step-6 lock, without lock hints. Under RCSI the statement sees every commit to that balance: postings to it serialize on its `UPDLOCK`, and the read starts after the lock is granted.
- **D9 · The reversal document and movements.** `Type = Reversal`, `ReversesDocumentId = original`, `ReasonCode = null` even for an adjustment (S6 Downstream, `CK_StockDocuments_ReasonCode`), `Reference = Notes = null`, `PostedAt` from `TimeProvider`, `PostedBy` the current user. Movements copy `LineNo` (S5b), take ids from `MovementIds` in id order of the originals, and set `ReversesMovementId`. Balances get `OnHand`, `StockValue`, `LastUnitCost` from `Reverse`, `AllowNegative` from step 7, `LastMovementId` from the new movements. No D1 range check: a reversal restores values the table already held.
- **D10 · Response.** The reversal returns `DocumentResponse` (`201`, `Location: /api/documents/{reversalId}`), which gains `reversesDocumentId` (null on every other type). Additive JSON; no test pins the full property set (grep), and the web reads only `id`/`type`.
- **D11 · Reverse button.** On `documents/document-detail` only (S7 Downstream; S8 says `/items/:id` doesn't need one). Visible when `auth.isAdmin()`, `type !== 'Reversal'` and `reversedByDocumentId === null`. Confirm with `shared/confirm-dialog`. One `IdempotencyKey` (from `posting-form.ts`) per loaded document id: reused on any failed retry, renewed after success, on `idempotency-key-reused`, and when another document loads. After `201`/`200`, reload the detail. `reversal-not-latest` → message + link "the later document" (`/documents/{blockingDocumentId}`); `already-reversed` → message + link "the reversal" (`/documents/{reversalDocumentId}`), then reload.
- **D12 · One unit, IMS-13.** The existing ticket; §12 makes S9 one PR. High-stakes (`Persistence/**`, `Features/Reversals/**`): code-guardian required (CLAUDE.md). The route lives in `Features/Reversals/ReverseDocument/{Endpoint,Handler}.cs` (no `Request.cs`: there is no body) and is mapped on the `/documents` group in `DocumentEndpoints`.

**Assumptions:**
- Every document posted through the API has at least one movement. A movement-less document exists only as a test holder row; `PostingRules.Reverse` throws `ArgumentException` for it (500). No route can create one.
- `insufficient-stock` on a reversal is unreachable until S10's toggle exists (latest-only makes an intervening outflow impossible; only "toggle turned off since the original" reaches it, spec audit `:23(b)`). The handler maps it; S10's T15 tests it.
- The handler does no inactive pre-check outside the transaction; step 7 is the only one. A reversal has no request fields, so there is no "every 400 before any 422" ordering to keep.
- History reads are unbounded per touched balance (hundreds to low thousands of rows at this scale), one query per physical key.
- Reversing an opening-stock seed receipt is allowed. A later `Api seed` replays the original receipt's key and skips it; it never re-posts.
- The Testcontainers start-up flake and the arm64 SQL Server 2025 limit (S8 Log) may need `--max-parallel-test-modules 1` or an x64 host.

**Non-goals:**
- S10 (negative-stock toggle, estimates, revaluation), S11 (health and rebuild endpoints), S12 (T14).
- Partial or line-level reversals; reversal notes, reference or reason; a Reverse button anywhere but `/documents/:id`.
- Any change to `Domain/**` (`PostingRules.Reverse` is final from S5b), migrations, columns, indexes, `security.TenantPolicy`, packages, or spec §6.
- Changing the four existing `RequestHash` forms or `DocumentPoster`'s behaviour.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs` | M | D1 helpers; `ReverseAsync` (steps 3, 4, 6–10 for a reversal; D5–D9) | R1–R3 | 01 |
| `src/Modules/Inventory/Inventory/Persistence/PostingCommand.cs` | M | `ReversalCommand`, `AlreadyReversedOutcome`, `ReversalNotLatestOutcome` | R1, R2 | 01 |
| `src/Shared/Kernel/Problems/ProblemTypes.cs` | M | `ReversalNotLatest`, `AlreadyReversed`, `CannotReverseReversal` | R2, R5 | 02 |
| `src/Modules/Inventory/Inventory/Features/Problems.cs` | M | `ReversalNotLatest`, `AlreadyReversed`, `CannotReverseReversal` results | R2 | 02 |
| `src/Modules/Inventory/Inventory/Features/Posting/RequestHash.cs` | M | `Reversal(Guid)` form (D4) | R1 | 02 |
| `src/Modules/Inventory/Inventory/Features/Posting/DocumentResponse.cs` | M | `ReversesDocumentId` (D10) | R1 | 02 |
| `tests/Inventory.UnitTests/RequestHashTests.cs` | M | Pin the reversal pre-image and hash | R6 | 02 |
| `docs/Inventory System — Sprint 1 Spec.md` | M | §9 row for `cannot-reverse-reversal` (D2) | R5 | 02 |
| `src/Modules/Inventory/Inventory/Features/Reversals/ReverseDocument/Endpoint.cs` | C | `POST /documents/{id:guid}/reversal`, Admin | R1 | 03 |
| `src/Modules/Inventory/Inventory/Features/Reversals/ReverseDocument/Handler.cs` | C | D3 order, command, outcome mapping | R1, R2 | 03 |
| `src/Modules/Inventory/Inventory/Features/Posting/DocumentEndpoints.cs` | M | `documents.MapReverseDocument()` | R1 | 03 |
| `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs` | M | `AddScoped<ReverseDocumentHandler>()` | R1 | 03 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | M | T5 probe for the route | R6 | 03 |
| `tests/Inventory.IntegrationTests/ReversalTests.cs` | C | T8 and route behaviour | R1, R2, R6 | 04 |
| `tests/Inventory.IntegrationTests/LedgerInvariants.cs` | M | INV-4 | R6 | 04 |
| `tests/Inventory.IntegrationTests/PostingVectorTests.cs` | M | T1 API run of set V | R6 | 04 |
| `tests/Inventory.IntegrationTests/PostingClockTests.cs` | M | T10 for a reversal | R6 | 04 |
| `tests/Inventory.IntegrationTests/PostingConcurrencyTests.cs` | M | T4 same-key reversals; different-key and index races; latest-only race under the locks | R2, R3, R6 | 05 |
| `web/src/app/core/problems/problem-messages.{ts,spec.ts}` | M | Three slugs (R5) | R4, R5 | 06 |
| `web/src/app/documents/documents.service.{ts,spec.ts}` | M | `reverse(id, key)` | R4 | 06 |
| `web/src/app/documents/document-detail.{ts,html,spec.ts}` | M | Reverse button (D11) | R4 | 07 |

## Blast radius
**Risk:** high. Prompt 01 refactors the ledger's only write path (`PostingEngine`, used by four posting routes and the seed) and adds a second write path; prompt 03 adds a ledger write route. Both are High-stakes globs; Kernel is Shared code.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `DocumentPoster.PostAsync` (receipts, issues, transfers, adjustments) | Direct | `PostingEngine.PostAsync`, `FindExistingAsync` | No if D1 moves the SQL unchanged | Prompt 01 Done-when runs `PostingTests`, `TransferTests`, `AdjustmentTests`, `PostingConcurrencyTests` (T2, T3, T4, step-4 and step-7 races, deadlock retry, retry budget), `PostingVectorTests`, `PostingClockTests` |
| `InventorySeeder` opening stock | Direct | `FindExistingAsync`, `PostAsync`; `case var outcome` throws on unknown outcomes | No: the seed never receives the new outcomes | `SeedTests` in prompt 01 Done-when |
| `DocumentPoster.ToResult` `default` | Direct | New `PostingOutcome` subtypes | No: a posting never yields them; `default` still throws | — |
| `PostingCommand` / outcomes | Direct | New records added, none changed | No | — |
| `PostingRules.Reverse`, `CostingPolicy.LastUnitCost` | Direct (callee) | First production caller | No: unchanged; unit tests cover them (S5b) | V and T8 through the API |
| `DocumentResponse` (4 posting routes, replays) | Direct | New `reversesDocumentId` | No: additive; no full-shape test (grep `AsObject().Select(p => p.Key)` hits only items/problems); web reads `id`/`type` | — |
| `RequestHash` existing forms | Hidden coupling | New `Reversal` method only | No: the four forms are untouched | `RequestHashTests` stay green; new pin |
| Kernel `ProblemTypes` (Shared code) | Direct | Three constants | No: additive | — |
| ProblemDetails slugs ↔ `PROBLEM_MESSAGES` | Hidden coupling | Three new slugs | **Yes** until mapped | Prompt 06, same PR (R5) |
| T5 route guard (`TenantIsolationTests:650-705`) | Non-graph (test) | Every `/api` route needs a probe | **Yes** until probed | Prompt 03 adds `[ProbesRoute("POST /api/documents/{id:guid}/reversal")]` |
| T10 `No_request_type_carries_a_date` | Non-graph (test) | No `*Request` type for the reversal | No | Prompt 04 adds a reversal to `PostedAt_comes_from_the_clock` |
| T12 architecture rules | Non-graph (test) | New `internal` types in `Inventory.Features.Reversals.ReverseDocument` | No: one public type per module stays | `dotnet test` |
| RLS / named `Tenant` filter | Non-graph (security) | Handler reads through EF; engine raw SQL through EF's connection | No: a foreign id is invisible → 404; the engine's statements run with the session context | T5 probe |
| Triggers, `DENY UPDATE, DELETE`, `CK_StockDocuments_Reversal`/`_ReasonCode`, `UX_StockDocuments_Reverses` | Non-graph (schema) | Reversal inserts only | No: insert-only; `ReversesDocumentId` set, `ReasonCode` null | T8 adjustment case; D5 race test |
| `GET /documents`, `GET /documents/{id}` | Transitive | Reversal docs and links | No: `LineCount` counts `LineNo < 1000`; the reversal copies `LineNo` | T8 asserts links both ways |
| `GET /items/{id}/movements` | Transitive | Reversal rows | No: S8 needs no special case | T8 asserts the restored running values |
| §6 lock order (balances by clustered key; deactivation; SKU edit) | Known risk | Reversal reuses the step-6 statement | No: same statement, same order; deadlocks retry under the strategy | T3 and deadlock tests in prompt 01 Done-when; prompt 05 race tests |
| `document-detail` screen and its spec | Direct (web) | New button, `AuthService.isAdmin`, `MatDialog` | No if the spec's `AuthService` stub gains `isAdmin` | Prompt 07 |
| S10 negative stock | Downstream | `Reverse` skips the short check under `AllowNegative`; revaluation movements are mirrored; D7 lock set | No | Downstream note |
| S11 health and rebuild | Downstream | Rebuild takes the applock exclusively; INV-4 oracle; applock `-3` nit now covers reversals too | No | Downstream note |
| S12/T14 | Downstream | "Admin reverses the issue and sees 10" | No | Downstream note: button label "Reverse", confirm "Reverse" |

**Known risks in the touched area:**
- **Refactoring the posting attempt** (S5c): the step-6 statement's `INDEX(PK_StockBalances)` and `FORCESEEK`, the step-7 `READCOMMITTEDLOCK, FORCESEEK`, the `EF1003` pragma and the step-5 key order must move verbatim. Prompt 01 forbids any change to SQL text.
- **Retries** (IMS-3): `ReverseAsync` runs inside `CreateExecutionStrategy()` like `PostAsync`; each attempt starts with `ChangeTracker.Clear()`.
- **The applock deadlock return `-3` isn't retried** (IMS-8 nit, S11): unchanged; reversals inherit it through the shared helper.
- **`NEXT VALUE FOR` through `SqlQuery`** fails with 11719 (IMS-6); the engine already uses `sp_sequence_get_range`.
- **Decimal scale** (IMS-8): compare JSON numbers with `GetValue<decimal>()`.
- **Testcontainers start-up flake** (IMS-6) and the arm64 limit (S8 Log): `dotnet test -- --max-parallel-test-modules 1`, or an x64 host.
- **Restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1`** on this machine (IMS-7).

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-13 | S9 Reversals | Added | 01, 02, 03, 04, 05, 06, 07 | IMS-10, IMS-11 (merged) | yes (`Persistence/**`, `Features/Reversals/**`, Kernel) | See below |

Acceptance criteria for IMS-13:
- **R1:** Set V through the API: V7 (reverse V6) returns `201`, one movement B → SUP costing 750.0000 with `reversesMovementId` = V6's movement, `reversesDocumentId` = V6, and B is back to 4 / 480.0000 with `LastUnitCost` 120.0000. Reversing a two-line receipt returns both balances to 0 / 0 with `LastUnitCost` null. Reversing an issue (10 @ 100, issue 3) restores 10 / 1000.0000, LUC 100.0000, and CUS back to 0. Reversing a transfer (A 10 @ 100, B 5 @ 120, transfer 4 A→B leaves B at LUC 111.1111) restores A 10 / 1000.0000 LUC 100.0000 and B 5 / 600.0000 LUC 120.0000. Reversing an adjustment stores `reasonCode` null. The same key and route replay with `200` and `Idempotent-Replayed: true`, same id. `GET /documents/{id}` links both ways; item history shows the reversal and the restored running values. Staff `403` writes nothing.
- **R2:** V8 → `409 reversal-not-latest` with `itemId`, `locationId` = A and `blockingDocumentId` = V5's transfer. A second reversal (another key) → `409 already-reversed` with `reversalDocumentId`. Reversing a reversal → `422 cannot-reverse-reversal` with `reversesDocumentId`. Unknown id and tenant B's id → `404`. No key → `400 idempotency-key-missing`. The key reused on another original or on a receipt → `409 idempotency-key-reused`. An inactive item or location (stock issued to 0, then deactivated) → `422 inactive-item`/`inactive-location`. A rejected reversal leaves no document and its key can post later.
- **R3:** A holder that commits a raw issue at A while the reversal of A's latest receipt waits on A's balance lock turns that reversal into `409 reversal-not-latest` naming the holder's document; if the holder rolls back, the reversal posts `201`. A holder's uncommitted Reversal row for the same original makes a different-key reversal block, then answer `409 already-reversed` on commit or `201` on rollback. Two parallel same-key reversals give one `201` and one `200` replay. Invariants (INV-1 to INV-5) hold after each.
- **R4:** As Admin on a Receipt with no reversal, `/documents/:id` shows Reverse; Staff, a Reversal, or a reversed document show none. Confirm → `POST /api/documents/{id}/reversal` with an `Idempotency-Key`, the button disabled meanwhile, then the detail reloads. A failed attempt retried sends the same key. `reversal-not-latest` shows the message and a link to `/documents/{blockingDocumentId}`; `already-reversed` a link to `/documents/{reversalDocumentId}`.
- **R5:** `reversal-not-latest`, `already-reversed`, `cannot-reverse-reversal` are in spec §9, Kernel `ProblemTypes` and `PROBLEM_MESSAGES`.
- **R6:** `ReversalTests`, `PostingVectorTests.Set_V_…`, the new `PostingConcurrencyTests` facts, the T5 probe, T10, `RequestHashTests`, and the web specs pass; `LedgerInvariants` checks INV-4.
- **R7:** Every `## Commands` line and CI `build` and `test` are green; code-guardian review requested at the PR gate.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Engine: shared steps and `ReverseAsync` | IMS-13 | opus | xhigh | — | A | Persistence/{PostingEngine.cs, PostingCommand.cs} |
| 02 | Reversal contracts: slugs, problems, hash, response | IMS-13 | opus | high | — | B | Kernel ProblemTypes.cs, Features/Problems.cs, Posting/{RequestHash.cs, DocumentResponse.cs}, RequestHashTests.cs, spec §9 |
| 03 | Reversal route | IMS-13 | opus | high | 01, 02 | C | Features/Reversals/ReverseDocument/{Endpoint,Handler}.cs, DocumentEndpoints.cs, InventoryModuleExtensions.cs, TenantIsolationTests.cs |
| 04 | T8, set V, INV-4, T10 | IMS-13 | sonnet | high | 03 | D | ReversalTests.cs, LedgerInvariants.cs, PostingVectorTests.cs, PostingClockTests.cs |
| 05 | Reversal races (T4, index race, latest-only under locks) | IMS-13 | opus | high | 04 | E | PostingConcurrencyTests.cs |
| 06 | Web: problem messages and `reverse()` | IMS-13 | haiku | low | 02 | D | problem-messages.{ts,spec.ts}, documents.service.{ts,spec.ts} |
| 07 | Web: Reverse button | IMS-13 | sonnet | medium | 06 | E | document-detail.{ts,html,spec.ts} |

- 01 `opus`: High-stakes `Persistence/**`; it refactors the ledger's only write path and adds a second one with lock-order and retry semantics.
- 02 `opus`: Kernel is `Shared code:`, and `RequestHash` is listed Hidden coupling; the edits are small, so `high`, not `xhigh`.
- 03 `opus`: High-stakes `Features/Reversals/**`; it sets the request order (D3) and maps every ledger outcome.
- 05 `opus`: concurrency; deterministic lock-holder races with raw ledger writes that must leave the invariants intact.

The .NET prompts share one solution build, so 01 → 02 → 03 → 04 → 05 run in order (01 and 02 don't depend on each other but share the build). The web prompts share `web/` but no .NET project, so 06 may run beside 04, and 07 beside 05.

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if the Testcontainers start-up flake shows: dotnet test -- --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false
cd ..
# R1–R3, R6 (API): reversals, set V, races, T5, T10, and the posting regression net
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*ReversalTests' --filter-class '*PostingVectorTests' --filter-class '*PostingConcurrencyTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests' --filter-class '*PostingTests' --filter-class '*TransferTests' --filter-class '*AdjustmentTests' --filter-class '*SeedTests'
dotnet test --project tests/Inventory.UnitTests --filter-class '*RequestHashTests'
# R5: the three slugs in all three places
grep -c -e 'reversal-not-latest' -e 'already-reversed' -e 'cannot-reverse-reversal' src/Shared/Kernel/Problems/ProblemTypes.cs web/src/app/core/problems/problem-messages.ts "docs/Inventory System — Sprint 1 Spec.md"
# Review constraints: no UPDATE/DELETE on the ledger tables, no date in a request, no unnamed IgnoreQueryFilters
git diff develop -- src | grep -nE 'UPDATE +\[?inventory\]?\.\[?Stock(Documents|Movements)|DELETE|IgnoreQueryFilters\(\)|DateTime(Offset)?\.(Now|UtcNow)' || echo "constraints clean"
# R4 (web): problem-messages.spec, documents.service.spec, document-detail.spec run in the Vitest suite above
# Manual smoke (optional, needs the seed-password secret): dotnet run --project src/AppHost; as the demo Admin, Receive stock,
# open its detail, Reverse -> confirm -> the page shows "Reversed by this reversal"; as Staff the button is absent.
```

## Rollback
Revert the PR. No schema change. Reversal documents already posted stay in the ledger (append-only, by design) and remain valid: the reverted build reads them through `GET /documents` and item history as ordinary rows, and S11's rebuild handles them. Re-applying the PR needs nothing extra.

## Downstream (for TASKS.md after merge)
- **S10:** `PostingRules.Reverse` skips the short check under `AllowNegative` while `Post` rejects; T15 should add "toggle off since the original → reversal `422 insufficient-stock`". Reversals mirror revaluation movements (`LineNo` 1000 + n) and never compute their own. `ReverseAsync`'s lock set (D7) already tolerates a third touched location; `AttemptAsync`'s items × locations count check doesn't, once a revaluation adds `ADJ`.
- **S11:** rebuild's exclusive applock blocks reversals too (shared helper). INV-4 is in `LedgerInvariants` as the test oracle; health's SQL must agree. The applock `-3` nit now covers both write paths.
- **S12/T14:** the button reads "Reverse" and the confirm button "Reverse"; after success the detail shows "Reversed by this reversal".

## Log
- 2026-11-15: planned
- 2026-11-15: audited (Codex PASS), 0 fixes applied; ticket step: existing IMS-13, TASKS.md line rewritten in place
