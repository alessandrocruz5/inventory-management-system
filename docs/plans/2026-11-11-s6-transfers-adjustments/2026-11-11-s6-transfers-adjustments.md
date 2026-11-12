---
type: plan
kind: feature
status: done
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-11
risk: high
prompts: 5
graph_built: none
tags: [plan]
---
# S6 Transfers and adjustments (IMS-10)

## Request
> IMS-10

This plans the existing unit `IMS-10 — S6 Transfers and adjustments · Added · spec: §12 S6 · depends: IMS-8 · model: opus` (TASKS.md, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S6: *"Both document types; the clustered-key lock path. Tests: T3; T1 API run of A · code-guardian review."*

Spec §11: *"T3 · 50 parallel transfers A→B and B→A · Integration · No unhandled deadlock; invariants hold afterwards."* Spec §13 (DoD): *"Staff can receive, issue and transfer. Staff gets `403` on adjustments …"* and *"Vector sets V, R, A … reproduce to the fourth decimal, in unit tests and through the API."*

It also covers the S6 items that earlier units locked:
- IMS-6 (S5a): validate `reasonCode` at the request. It is required and in the `AdjustmentReason` enum on adjustments, and absent on every other type. A bad value returns `400 validation` with `errors.reasonCode`; otherwise `CK_StockDocuments_ReasonCode` surfaces as an unmapped 547 and a `500`.
- IMS-7 (S5b): map `UnitCostRequiredException` to `400 validation` with `errors["lines[i].unitCost"]`.
- IMS-8 (S5c): `DocumentPoster.PostAsync` is the base for every posting route. S5c's plan (`:226`) says *"T3 (S6) is the real proof"* that the parameterized `IN` seek locks in clustered-key order.

| # | Requirement |
|---|---|
| R1 | `POST /api/documents/transfers` (Staff, Admin; `Idempotency-Key` required). Body: `{ fromLocationId, toLocationId, reference, notes, lines: [{ itemId, quantity }] }`. Each line moves stock from physical A to physical B, costed at A's moving average. B receives the same cost, and its `LastUnitCost` is set as for any inflow that isn't a reversal (spec §6 table, §7). |
| R2 | `POST /api/documents/adjustments` (Admin only; Staff gets `403`; `Idempotency-Key` required). Body: `{ locationId, reasonCode, reference, notes, lines: [{ itemId, quantityDelta, unitCost? }] }`. A positive line moves Adjustment → location at `quantity × unitCost`. Without a unit cost it takes `Round4(qty × StockValue / OnHand)`, and the cost is required when `OnHand <= 0` (`400` on `lines[i].unitCost`). A negative line moves location → Adjustment at the moving average, and a unit cost on it is a `400`. `reasonCode` is validated and stored on the document. |
| R3 | Both routes run on `DocumentPoster`, in the fixed D2 order: key, pure step 1, hash, replay lookup, step 1's database checks, engine. They use the engine's steps 3–10 unchanged, except that step 4 now writes `ReasonCode`. Each route has its own canonical `RequestHash` form, and the receipt and issue hashes are byte-for-byte unchanged. Replays return `200` with `Idempotent-Replayed: true`. Problems: `insufficient-stock`, `inactive-item`/`-location`, `idempotency-key-*`, `concurrency-conflict`, `validation`. No new slugs. |
| R4 | Tests in the same PR, plus a code-guardian review: **T3** (50 parallel multi-item transfers A→B and B→A: all `201`, zero execution-strategy retries, INV-1/2/3/5 hold, value conserved); **T1 API run of set A** (A1–A7 exact); T5 probes for both routes; route, role and validation tests; unit tests for the new validation and canonical forms. |
| R5 | Every `## Commands` line is green, and CI `build` and `test` are green. |

**Clarify pass (2026-11-11):** nothing that would change the design is undefined. The small gaps are listed under Assumptions for `/audit-plan` to challenge.

## Context used
- Spec §6 (`docs/Inventory System — Sprint 1 Spec.md:291-343`): the document table (transfer at the average at A; positive adjustment at the request cost or `Round4(qty × StockValue / OnHand)`, required when `OnHand <= 0`; negative at the average), steps 1–10, the step-6 clustered-key lock claim marked **[Likely]**, and INV-1 to INV-5.
- Spec §7 (`:345-431`): `DefaultAdjustmentCost`, the `LastUnitCost` rule, and vector set A (`:422-432`).
- Spec §9 (`:524-590`): routes and roles (`POST /documents/transfers` Staff+Admin, `/adjustments` Admin), the transfer and adjustment bodies (`:564-571`), the problem table. Spec §10 (`:611`): the post-stock form's header fields include "reference and notes". Spec §11 (`:633-636`): T1, T3, T5. Spec §13 (`:692-693`): Staff `403` on adjustments; vector set A through the API.
- TASKS.md Downstream: IMS-4 (T5 probe for every new route, a body id from tenant B → `400`), IMS-5 (endpoint folder layout `Features/<Area>/<Endpoint>/…`), IMS-6 (the S6 `reasonCode` line; tests `LedgerRows`; the Testcontainers flake and `--max-parallel-test-modules 1`), IMS-7 (rejection mapping, `UnitCostRequired` → `lines[i].unitCost`), IMS-8 (`DocumentPoster` is the base; `PostingTests` helpers; `DOTNET_SYSTEM_NET_DISABLEIPV6=1`).
- S5a plan `2026-11-05-s5a-ledger-schema.md:101,107,139` and audit `:19`: the stricter `CK_StockDocuments_ReasonCode` and why S6 must validate first.
- S5c plan `2026-11-09-s5c-posting-engine.md:127` (cross product = touched set; "S6 transfers … keep that shape"), `:144` (`UnitCostRequired` left for S6), `:149` (S6 owns transfers, adjustments, T3, T1-A), `:226` (T3 is the real lock-order proof).
- Code: `Features/Posting/DocumentPoster.cs:18-140`: today's poster, bound to "one physical location + a counterpart" via `counterpart`/`counterpartIsSource`; its `default` throws on `UnitCostRequired` (`:136`). The `BalanceOutOfRange` key is `lines[i].quantity` (`:131-132`).
- Code: `Features/Posting/PostingValidation.cs:12-15` (`ValidatedLine`, `ValidatedPosting`, positional), `:91-166` (the private `Validate`), `:186-205` (`Amount`). `RequestHash.cs:16-93` (routes, `CanonicalJson(posting, withUnitCost)`, `FormatDecimal`).
- Code: `Persistence/PostingEngine.cs:115` (`ReasonCode = null` hard-coded at step 4), `:163-183` (step 6 lock statement and the `items × locations` count check), `:205-220` (step 7 inactive order: items, then each line's From then To). `PostingCommand.cs:9-17` (positional; one other caller, `Seeding/InventorySeeder.cs:206`).
- Code: `Domain/PostingRules.cs:24-147` (`Post` already handles `Transfer` and `Adjustment`: `CheckDirection` `:286-309`; default adjustment cost and `UnitCostRequiredException` `:104-114`; transfer-in sets `LastUnitCost` `:133-140`). `Domain/PostingRejections.cs:39-43`. No Domain change is needed.
- Code: `Persistence/StockDocument.cs:15` (`AdjustmentReason? ReasonCode`), `InventoryDbContext.cs:88-96` (the CHECK; string conversion), `AdjustmentReason.cs`, `LocationKind.cs`. `Features/InventoryPolicies.cs:110` (`Admin`), `Items/CreateItem/Endpoint.cs:13` (`.RequireAuthorization(InventoryPolicies.Admin)`). `InventoryModuleExtensions.cs:53-55,63` (DI, mapping). `DocumentEndpoints.cs:12-18`.
- Code: Kernel `Problems/ProblemTypes.cs` and `web/src/app/core/problems/problem-messages.ts:2-15`: every slug S6 returns already exists in both, so there is no web change.
- Tests: `PostingTests.cs:556-600` (helpers), `:189-212` (the column-range pattern), `:320-337` (role pattern). `PostingVectorTests.cs:14-60` (the set R API run to mirror). `PostingConcurrencyTests.cs:28-56` (T2 pattern), `:367-404` (the deadlock-retry test), `:411-424` (`InParallelAsync`). `TenantIsolationTests.cs:24-32,326-399,401-424` (T5 probes and the route guard). `ApiFactory.cs:13-33` (the `Clock` init hook to mirror). `RequestHashTests.cs:38-99`, `PostingValidationTests.cs:12-60,195-258` (construct `ValidatedPosting`/`ValidatedLine` positionally). `PostingRulesTests.cs:124-189` (Domain already covers transfers and adjustments).
- Config: `src/Api/appsettings*.json` set `Default: Information`, with no EF override. A provider-specific filter makes the retry counter independent of that.
- Graph: none (`graphify-out/` doesn't exist). The blast radius was found by grep (workflow §4).

## Decisions
- **D1 · One poster, derived from `PostingKind`.** `DocumentPoster.PostAsync(http, kind, validate, hash, ct)` drops its `type`, `counterpart` and `counterpartIsSource` parameters. One private switch on `kind` derives the document type, the counterpart kind (Supplier, Customer, Adjustment, or none for a transfer), the header location fields to check, and each line's From/To. This keeps the D2 order and the outcome mapping in one place for four routes (S9 adds its own path). It follows IMS-8's lock. Rejected alternative: sibling `TransferPoster`/`AdjustmentPoster` classes. They'd copy the D2 order and outcome mapping three times, and they conflict with the locked decision.
  - **Supersedes part of IMS-8's note** (audit 2026-11-11). `TASKS.md:17` says "add its `counterpartIsSource` flag". One boolean can't express a transfer, which has no counterpart, or an adjustment, whose direction varies per line, so D1 derives both from `kind`. The part of the lock that says "base for every posting route" still holds.
- **D2 · Widen `ValidatedPosting` without breaking its positional constructor.** It gains two init-only properties: `Guid? ToLocationId` (transfers; `LocationId` is then the source) and `AdjustmentReason? ReasonCode` (adjustments). `ValidatedLine` keeps its shape. On an adjustment, `Quantity` holds the **signed** `quantityDelta` (never 0), so the canonical hash is the request as sent. `DocumentPoster` is the one place that turns it into a positive quantity plus a direction. `PostingRules.Post` throws `ArgumentOutOfRangeException` on `Quantity <= 0`, so a leak of a signed value into any other kind fails loudly as a `500`, never silently. The existing positional constructions in `RequestHashTests`, `PostingValidationTests` and the seeder keep compiling.
- **D3 · Canonical forms (hidden coupling).** `RequestHash.CanonicalJson(posting, withUnitCost)` and the receipt and issue routes stay byte-identical; their exact-JSON tests are the guard. New forms, with the same rules (no whitespace, fixed order, null for absent, lowercase `D` Guids, decimals without trailing zeros, lines in `LineNo` order):
  - `POST /api/documents/transfers`: `{"fromLocationId":…,"toLocationId":…,"reference":…,"notes":…,"lines":[{"itemId":…,"quantity":…}]}`
  - `POST /api/documents/adjustments`: `{"locationId":…,"reasonCode":"Damaged","reference":…,"notes":…,"lines":[{"itemId":…,"quantityDelta":-2,"unitCost":null}]}`
- **D4 · `PostingCommand` gains a required `AdjustmentReason? ReasonCode`, as its last positional parameter.** It is required, not optional, so no future caller can forget it. The seeder passes `ReasonCode: null`. Engine step 4 writes `command.ReasonCode`. The CHECK stays the backstop.
- **D5 · Problem mapping.**
  - `UnitCostRequiredException(lineNo)` → `400 validation`, `errors["lines[{lineNo-1}].unitCost"]`.
  - `BalanceOutOfRangeOutcome` names `lines[i].quantityDelta` on adjustments, and `lines[i].quantity` elsewhere: the key matches the request's own field. **User-approved reading of spec §6 step 8** (audit 2026-11-11). The spec text says `lines[i].quantity`, but an adjustment has no such field, so the key follows the field the request actually sent.
  - `ReversalNotLatest` still falls to `default` and throws (S9).
  - A step-8 `400` (unit cost required, out of range) can follow a step-1 `422` in precedence. That's inherent, because it depends on the locked balance.
- **D6 · Step 1's database checks per kind.**
  - Receipt, issue and adjustment check `locationId`. A transfer checks `fromLocationId`, then `toLocationId`.
  - Each must be an existing physical location of the tenant, or it's a `400` on that field. Another tenant's id reads as unknown (spec §8).
  - Then the inactive checks run, in the engine's step-7 order: items in line order, then the header locations in that order (`422`).
  - `from == to` is a pure step-1 `400` on `toLocationId`.
- **D7 · `DocumentResponse` gains `string? ReasonCode`, after `Type`.** Spec §9 says a successful post returns "the full document". Receipts, issues and replays now carry `"reasonCode": null`. No client consumes the response yet (S7 will).
- **D8 · T3's proof is zero retries, not only zero 500s.** EF's strategy retries a 1205 up to 3 times, so "all 201" alone can hide deadlocks. A test-only `RetryLog` `ILoggerProvider` counts `CoreEventId.ExecutionStrategyRetrying` per app (per `ApiFactory`, so parallel test classes can't pollute it). `ApiFactory` gains a `Retries` init hook that mirrors `Clock`. The existing `Deadlocked_posting_is_retried_and_posts_once` asserts `Retries.Count >= 1` as the positive control, which proves the probe isn't vacuous.
  - T3's A→B lines list the items in one order and B→A in the reverse order, so request order can't be what keeps them safe.
  - Every balance exists before the race, so step 5 inserts nothing; that isolates the step-6 lock path.
  - If T3 sees a retry, the executor stops and reports it as a finding about spec §6 step 6, never weakening the test (CLAUDE.md: ask before you disable a test).
- **D9 · Invariant checks live in a test helper, `LedgerInvariants`.** It reads the tenant's movements and balances on an RLS connection and checks, in C#:
  - INV-1: the projection, including `LastMovementId` = the max touching movement.
  - INV-2: conservation.
  - INV-3: physical sanity.
  - INV-5: `LastUnitCost` = `CostingPolicy.LastUnitCost` over the touching movements.

  INV-4 waits for reversals (S9). S9 and S11's T9 reuse the helper; S11's own SQL is still the product check.
- **Code review.** CLAUDE.md requires code-guardian on S6, and the unit touches High-stakes globs (`Features/Posting/**`, `Persistence/**`).

**Assumptions:**
- **`reference` is accepted on transfers and adjustments.** §9's example bodies omit it, but §10's form has "reference and notes" for every type, and the column exists. It's optional, trimmed, ≤ 100 characters, and in the canonical form.
- **`reasonCode` matches an `AdjustmentReason` name exactly** (ordinal, case-sensitive, not trimmed): `Damaged`, `Lost`, `Found`, `CountCorrection`, `Other`. Numeric strings (`"1"`) are rejected; `Enum.TryParse` would accept them, so the code doesn't use it. Anything else, or a missing value, is `400` with `errors.reasonCode` only. The client picks from a fixed list (S7).
- **"Absent on every other type" holds by construction.** The receipt, issue and transfer request records have no `reasonCode` property, so a stray one in JSON is ignored like any unknown field. No request rejects unknown fields today, and changing that isn't S6's job. Those routes always pass `ReasonCode: null`. That meets the intent of the IMS-6 lock (`TASKS.md:96`, no unmapped 547 → `500`) without rejecting the field (audit 2026-11-11, accepted).
- **An adjustment may mix positive and negative lines** (spec §9's example does), with each item at most once.
- **`quantityDelta` rules:** required, ≠ 0, |value| ≤ 999,999,999.9999, ≤ 4 dp. `unitCost` follows the receipt rules (≥ 0, ≤ max, ≤ 4 dp, and the D1 line-total guard) on positive lines, and must be absent on negative lines.
- No new problem slug, so the Angular map is untouched (hidden coupling holds).
- The Adjustment virtual location exists for every tenant (S2's `ProvisionTenant`). A missing one throws, as a missing Supplier does today (`DocumentPoster.cs:100-101`).

**Non-goals:**
- Reversals, T8 and the T1 API run of V (S9). The negative-stock estimate and revaluation (S10): a transfer or positive adjustment into a negative balance still hits the S10 `InvalidOperationException`, which is unreachable while every toggle is off.
- `GET /documents`, `GET /documents/{id}`, `GET /stock` and the post-stock form (S7). Any web change.
- Ledger health and rebuild endpoints (S11). `LedgerInvariants` is a test helper only.
- Any change to `Domain/**`, the schema, migrations, grants, RLS or spec §6.
- Rejecting unknown JSON members on any route.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Features/Posting/PostTransfer/Request.cs` | C | `PostTransferRequest`, `PostTransferLine` | R1 | 01 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostAdjustment/Request.cs` | C | `PostAdjustmentRequest`, `PostAdjustmentLine` | R2 | 01 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostingValidation.cs` | M | `ValidatedPosting.ToLocationId`/`.ReasonCode` (D2); `Transfer` and `Adjustment` step-1 checks | R1–R3 | 01 |
| `src/Modules/Inventory/Inventory/Features/Posting/RequestHash.cs` | M | `TransfersRoute`, `AdjustmentsRoute`, `Transfer`, `Adjustment` and their canonical forms (D3) | R3 | 01 |
| `tests/Inventory.UnitTests/PostingValidationTests.cs` | M | Transfer and adjustment step-1 cases | R4 | 01 |
| `tests/Inventory.UnitTests/RequestHashTests.cs` | M | Exact canonical JSON and pre-image for both new forms; route separation | R3, R4 | 01 |
| `src/Modules/Inventory/Inventory/Features/Posting/DocumentPoster.cs` | M | D1 kind switch, D6 header checks, line building (signed delta), D5 mapping | R1–R3 | 02 |
| `src/Modules/Inventory/Inventory/Features/Posting/DocumentResponse.cs` | M | `ReasonCode` (D7) | R2 | 02 |
| `src/Modules/Inventory/Inventory/Persistence/PostingCommand.cs` | M | `AdjustmentReason? ReasonCode` (D4) | R2 | 02 |
| `src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs` | M | Step 4 writes `command.ReasonCode`; the step-6 comment covers all four kinds | R2, R3 | 02 |
| `src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs` | M | Pass `ReasonCode: null` | R3 | 02 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostReceipt/Handler.cs` | M | New `PostAsync` signature | R3 | 02 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostIssue/Handler.cs` | M | New `PostAsync` signature | R3 | 02 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostTransfer/Endpoint.cs` | C | `MapPostTransfer`, no role policy | R1 | 03 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostTransfer/Handler.cs` | C | `PostTransferHandler` → `DocumentPoster` | R1 | 03 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostAdjustment/Endpoint.cs` | C | `MapPostAdjustment`, `RequireAuthorization(InventoryPolicies.Admin)` | R2 | 03 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostAdjustment/Handler.cs` | C | `PostAdjustmentHandler` → `DocumentPoster` | R2 | 03 |
| `src/Modules/Inventory/Inventory/Features/Posting/DocumentEndpoints.cs` | M | Map both routes | R1, R2 | 03 |
| `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs` | M | Register both handlers | R1, R2 | 03 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | M | T5 probes for both routes (the route guard fails without them) | R4 | 03 |
| `tests/Inventory.IntegrationTests/TransferTests.cs` | C | Transfer behaviour, roles, validation, replay, inactive | R1, R3, R4 | 04 |
| `tests/Inventory.IntegrationTests/AdjustmentTests.cs` | C | Adjustment behaviour, Staff `403`, reason, unit cost rules, replay | R2, R3, R4 | 04 |
| `tests/Inventory.IntegrationTests/PostingVectorTests.cs` | M | T1 API run of set A | R4 | 04 |
| `tests/Inventory.IntegrationTests/PostingTests.cs` | M | `AddUserAsync`, `AssertValidationAsync` and `AssertInsufficientStockAsync` go from `private` to `internal` (modifier only), so they're reused, not copied (audit) | R4 | 04 |
| `tests/Inventory.IntegrationTests/RetryLog.cs` | C | Counting `ILoggerProvider` (D8) | R4 | 05 |
| `tests/Inventory.IntegrationTests/LedgerInvariants.cs` | C | INV-1/2/3/5 checks (D9) | R4 | 05 |
| `tests/Inventory.IntegrationTests/ApiFactory.cs` | M | `Retries` init hook (D8) | R4 | 05 |
| `tests/Inventory.IntegrationTests/PostingConcurrencyTests.cs` | M | T3; positive control in the deadlock test | R4 | 05 |

`Domain/**`, migrations, Kernel `ProblemTypes` and `web/**` need no change.

**Review constraints, checked against the design:**
- **Decimals:** `decimal` throughout. Rounding is `CostingPolicy.Round4` (AwayFromZero, 4 dp). The tests use `m` literals.
- **No UPDATE or DELETE on documents or movements:** the engine only inserts them. Its existing balance update is unchanged. Tests only read.
- **No hard deletes.**
- **`TenantId` never comes from a request:** no new request has a tenant field, and the column default and interceptor stay as they are. There's no `IgnoreQueryFilters()`.
- **`PostedAt` comes from the engine's `TimeProvider`:** no new request carries a date, and T10's "no request type has a date property" still holds.
- **`inventory_app` only:** unchanged.
- **Packages:** no new package. `Microsoft.Extensions.Logging` and `Microsoft.EntityFrameworkCore` are already referenced by the test project through the API and EF.
- **Section 6 and ledger columns:** no new column. `ReasonCode` already exists, and S6 is the first writer the CHECK anticipated. The one reading of section 6 that isn't literal is D5's `quantityDelta` key, which the user approved at audit.

## Blast radius
**Risk:** high. It rewrites `DocumentPoster`, the base of every posting route, edits `PostingEngine` step 4, adds two canonical hash forms (hidden coupling), and owns the proof of the step-6 lock order. All of it is in High-stakes globs.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `PostReceiptHandler`, `PostIssueHandler` | Direct | `DocumentPoster.PostAsync` signature (D1) | Compile break | Updated in 02. The existing receipt and issue suites (`PostingTests`, `PostingConcurrencyTests`, `PostingVectorTests`, `PostingClockTests`, T5) must pass unchanged |
| Receipt/issue `RequestHash` | Direct (hidden coupling) | `RequestHash.cs` edits (D3) | Only if `CanonicalJson` or the routes change, which would turn every stored key into a false `409` | `CanonicalJson` and the routes stay untouched. The exact-JSON tests `RequestHashTests.cs:38-62` stay unmodified and green |
| `InventorySeeder` | Direct | `PostingCommand` positional change (D4) | Compile break | 02 adds `ReasonCode: null`. `SeedTests` and `Seed_twice_changes_nothing` stay green, and seeded hashes are unchanged |
| `PostingEngine` step 4 | Direct (High-stakes) | `ReasonCode = command.ReasonCode` | No: receipts, issues and the seed pass null, as now | The CHECK backstops a mismatch. The adjustment tests read `ReasonCode` back from the database |
| `PostingEngine` step 6 count check | Direct | New kinds | No: every kind moves all lines between exactly two locations (transfer A/B; the others the location and its counterpart), so items × 2 is the touched set | 02 updates the comment. T3 and the mixed-sign adjustment test exercise it |
| `DocumentResponse` JSON | Direct | `reasonCode` field (D7) | No: tests read fields by name, and the 201/replay shapes stay identical (plan D3) | — |
| `PostingRules.Post` | Callee (unchanged) | Transfer/Adjustment kinds, first live use | No: unit-tested for both kinds (`PostingRulesTests.cs:124-189`, set A) | Signed-delta leaks fail loudly (D2) |
| T5 route guard | Non-graph | Two new `/api` routes | Yes, the guard fails until probes exist | 03 adds both probes with the routes |
| T10 `PostingClockTests` | Non-graph | New request types | No: they carry no date property | — |
| T12 architecture | Non-graph | New internal types in `Inventory` | No: all `internal`, and the public surface is unchanged | — |
| `ApiFactory` | Direct (test) | `Retries` init hook | No: optional, and null keeps today's behaviour | Mirrors `Clock` |
| `Deadlocked_posting_is_retried_and_posts_once` | Direct (test) | Gains a positive-control assertion | Only if the probe is wrong, which is the point | D8 |
| Angular `PROBLEM_MESSAGES` | Non-graph (hidden coupling) | Slugs | No new slug | — |
| S7 (stock screens, post-stock form) | Downstream | Request bodies, `reasonCode` in the response | No | Downstream notes |
| S9 reversals | Downstream | `DocumentPoster` `default` branch, `PostingCommand.ReasonCode` | No | Reversals pass `ReasonCode: null` (S5a lock) and map `ReversalNotLatest` on their own route |
| S10 negative stock | Downstream | Transfer-in and positive adjustment into a negative balance | Not reachable while every toggle is off | S10 replaces the `InvalidOperationException` (IMS-7 Downstream) |
| S11 / T9 | Downstream | `LedgerInvariants`, `RetryLog` | No | Reuse them |
| `LedgerHarness.AssertInvariants` | Existing oracle (audit) | `tests/Inventory.UnitTests/LedgerHarness.cs:85-125` | No, but the two oracles could drift | Prompt 05 mirrors it rule for rule |
| `PostingTests` private helpers | Reuse (audit) | `AddUserAsync` `:602`, assertions `:648-666` | No: modifier change only | Prompt 04 makes them `internal` |
| `RetryLog` scope | Test fidelity (audit) | Counts retries from every DbContext, Platform's included | False positive only | If T3 sees a retry, the report names the context before blaming §6 step 6 |

**Known risks in the touched area:**
- **Spec §6 step 6's lock order is still marked [Likely].** T3 is the first test that can show it holds or doesn't. If T3 sees any retry, it's a finding to report (D8), not a test to loosen.
- **Deactivation vs a posting that inserts a new balance can deadlock** (IMS-6 Downstream). Transfers can insert `(I, B)` at step 5. That's a known case, retried under the strategy, and T3 avoids it by pre-creating every balance.
- **The `HOLDLOCK` next-key lock of a deactivation** can briefly block an unrelated transfer (IMS-6). Accepted.
- **The Testcontainers start-up flake** (IMS-6). Use `--max-parallel-test-modules 1` if it shows.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-10 | S6 Transfers and adjustments | Added | 01, 02, 03, 04, 05 | IMS-8 | yes. `Features/Posting/**` and `Persistence/**`. code-guardian required (CLAUDE.md) | See below |

Acceptance criteria for IMS-10:
- **R1:** A Staff user's `POST /api/documents/transfers` of 4 from A (holding 10 / 1,200.0000) to B returns `201`. The document has type `Transfer` and one movement A → B costing 480.0000, and the response lists both physical balances: A 6 / 720.0000, and B 4 / 480.0000 with `lastUnitCost` 120.0000 (V5). `from == to` gives `400` on `toLocationId`. A virtual or unknown location gives `400` on `fromLocationId`/`toLocationId`. An inactive B gives `422 inactive-location`. A shortfall gives `422 insufficient-stock`, and nothing is written.
- **R2:** Staff gets `403` on `/adjustments`, with nothing written. As Admin, set A posts exactly: A3 30.0200 → 6 / 60.0400; A4 10.0067 → 5 / 50.0333; A5 25.0000 → 7 / 75.0333; A6 75.0333 → 0 / 0.0000. A7 (+1, no cost, OnHand 0) gives `400` with only `lines[0].unitCost`, writes nothing, and doesn't consume its key. A missing, unknown, lower-case or numeric `reasonCode` gives `400` with only `reasonCode`. A unit cost on a negative line gives `400` on `lines[i].unitCost`. A `quantityDelta` of 0 gives `400` on `lines[i].quantityDelta`. The posted document's `ReasonCode` in the database and in the response equals the request's.
- **R3:** Same key and body on either route replays with `200` and `Idempotent-Replayed: true`. Same key with a different body, or on another route, gives `409 idempotency-key-reused`. `RequestHashTests`' receipt and issue exact-JSON tests are unchanged and green. Every pre-existing receipt, issue, seed and concurrency test passes unmodified (apart from the D8 positive-control line).
- **R4:** **T3:** 50 parallel 3-item transfers (25 A→B, 25 B→A, lines in opposite orders) all return `201`. `RetryLog` counts 0 retries, and the positive control counts ≥ 1. Each item ends at 100 at A and 100 at B. Each item's A+B value equals its initial A+B value exactly. `LedgerInvariants` reports no failure. **T1-A** passes. **T5** probes for both routes give `400` with the foreign id's key, and tenant B's ledger is unchanged. The code-guardian review has no open Blocker.
- **R5:** Every `## Commands` line and CI `build` and `test` are green.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Transfer and adjustment request contracts: step 1 and canonical hash | IMS-10 | opus | high | — | A | `PostTransfer/Request.cs`, `PostAdjustment/Request.cs`, `PostingValidation.cs`, `RequestHash.cs`, `PostingValidationTests.cs`, `RequestHashTests.cs` |
| 02 | `DocumentPoster` for every kind; `ReasonCode` through the engine | IMS-10 | opus | high | 01 | B | `DocumentPoster.cs`, `DocumentResponse.cs`, `PostingCommand.cs`, `PostingEngine.cs`, `InventorySeeder.cs`, `PostReceipt/Handler.cs`, `PostIssue/Handler.cs` |
| 03 | Transfer and adjustment routes, T5 probes | IMS-10 | opus | medium | 02 | C | `PostTransfer/{Endpoint,Handler}.cs`, `PostAdjustment/{Endpoint,Handler}.cs`, `DocumentEndpoints.cs`, `InventoryModuleExtensions.cs`, `TenantIsolationTests.cs` |
| 04 | Route behaviour tests and the T1 API run of set A | IMS-10 | sonnet | high | 03 | D | `TransferTests.cs`, `AdjustmentTests.cs`, `PostingVectorTests.cs`, `PostingTests.cs` (modifiers only) |
| 05 | T3: the clustered-key lock path, retry counter, ledger invariants | IMS-10 | opus | high | 04 | E | `RetryLog.cs`, `LedgerInvariants.cs`, `ApiFactory.cs`, `PostingConcurrencyTests.cs` |

- **01 opus:** a High-stakes path (`Features/Posting/**`) and the `RequestHash` canonical form, a hidden coupling where a slip breaks every stored key.
- **02 opus:** it rewrites `DocumentPoster`, the base of every posting route, and edits `PostingEngine` step 4 (High-stakes `Persistence/**`).
- **03 opus (medium effort):** the files are in a High-stakes glob (`Features/Posting/**`). The work itself is mechanical, copying `PostIssue/*`, hence medium effort.
- **05 opus:** a concurrency proof. A test that is vacuous or flaky would make the spec's [Likely] lock-order claim look proven.
- **04 sonnet:** integration tests on existing helpers, with the expected values spelled out.
- Groups A → E run in order: every prompt shares the .NET build and the test tree (workflow §5). 05 depends on 04, because it uses `TransferTests`' builders, and both share `Inventory.IntegrationTests`.

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false
cd ..

# R3: receipt/issue canonical forms untouched; new forms exact
dotnet test --project tests/Inventory.UnitTests --filter-class '*RequestHashTests' --filter-class '*PostingValidationTests'
! git diff -U0 develop -- tests/Inventory.UnitTests/RequestHashTests.cs | grep -E '^-[^-]'   # no existing line removed or changed
# R1, R2, R3: routes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*TransferTests' --filter-class '*AdjustmentTests' --filter-class '*PostingVectorTests'
# R4: T3, the positive control, T5 route guard
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingConcurrencyTests' --filter-class '*TenantIsolationTests'
# R3: pre-existing receipt/issue/seed suites unchanged and green
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingTests' --filter-class '*PostingClockTests' --filter-class '*SeedTests'
dotnet test --project tests/Platform.IntegrationTests --filter-method '*Seed_twice_changes_nothing'
dotnet test --project tests/Architecture.Tests

# Review constraints and scope
git diff --stat develop -- src/Modules/Inventory/Inventory/Domain src/Modules/Inventory/Inventory/Persistence/Migrations web src/Shared   # prints nothing
! git diff develop -- src | grep -nE '^\+.*\b(double|float)\b|^\+.*DateTime(Offset)?\.(Utc)?Now|^\+.*IgnoreQueryFilters\(\)'
! git diff develop -- src | grep -nE '^\+.*\b(UPDATE|DELETE)\b.*Stock(Documents|Movements)'

# Full stack smoke (Staff transfers; Staff 403 on adjust; Admin adjusts)
SCRATCH=$(mktemp -d)
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"
dotnet run --project src/AppHost > "$SCRATCH/apphost.log" 2>&1 &
for i in $(seq 120); do [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:4200/api/auth/me)" = 401 ] && break; sleep 2; done
# $SEED_PASSWORD: the AppHost's generated `seed-password` parameter, read into the shell and never echoed
for who in admin staff; do
  curl -s -c "$SCRATCH/$who" -H 'Content-Type: application/json' -d "{\"email\":\"$who@demo.test\",\"password\":\"$SEED_PASSWORD\"}" \
    http://localhost:4200/api/auth/login -o /dev/null -w "$who login %{http_code}\n"                       # 200
done
QC=$(curl -s -b "$SCRATCH/admin" 'http://localhost:4200/api/locations?pageSize=100' | jq -r '.items[] | select(.code=="QC") | .id')
MKT=$(curl -s -b "$SCRATCH/admin" 'http://localhost:4200/api/locations?pageSize=100' | jq -r '.items[] | select(.code=="MKT") | .id')
RICE=$(curl -s -b "$SCRATCH/admin" 'http://localhost:4200/api/items?pageSize=100' | jq -r '.items[] | select(.sku=="RICE-WHT") | .id')
curl -s -b "$SCRATCH/staff" -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuidgen)" \
  -d "{\"fromLocationId\":\"$QC\",\"toLocationId\":\"$MKT\",\"reference\":null,\"notes\":null,\"lines\":[{\"itemId\":\"$RICE\",\"quantity\":1}]}" \
  -o /dev/null -w 'staff transfer %{http_code}\n' http://localhost:4200/api/documents/transfers                # 201
curl -s -b "$SCRATCH/staff" -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuidgen)" \
  -d "{\"locationId\":\"$QC\",\"reasonCode\":\"Damaged\",\"notes\":null,\"lines\":[{\"itemId\":\"$RICE\",\"quantityDelta\":-1}]}" \
  -o /dev/null -w 'staff adjust %{http_code}\n' http://localhost:4200/api/documents/adjustments                # 403
curl -s -b "$SCRATCH/admin" -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuidgen)" \
  -d "{\"locationId\":\"$QC\",\"reasonCode\":\"Damaged\",\"notes\":\"smoke\",\"lines\":[{\"itemId\":\"$RICE\",\"quantityDelta\":-1}]}" \
  http://localhost:4200/api/documents/adjustments | jq '{type, reasonCode, cost: .movements[0].totalCost}'    # Adjustment, Damaged
kill %1
```

## Rollback
Revert the PR. There's no schema or migration change. Transfer and adjustment documents posted meanwhile stay in the ledger, which is append-only by design. They remain valid rows: the `Type` enum, the `ReasonCode` column and the CHECK predate S6. Reverting removes only the routes that create them. Their idempotency keys stay held, which is correct.

## Downstream (for TASKS.md after merge)
- **S7:** the post-stock form sends the D3 bodies: transfers `fromLocationId`/`toLocationId`; adjustments `reasonCode` from the five `AdjustmentReason` names (exact case) and a signed `quantityDelta`, with `unitCost` only on positive lines. `reference` is accepted on every type. Responses carry `reasonCode` (null except on adjustments). A `400` on `lines[i].unitCost` can arrive after submit, when the location holds none (A7). An adjustment's out-of-range balance arrives as `lines[i].quantityDelta` (D5), so map that key to the delta field.
- **S9:** a reversal passes `PostingCommand.ReasonCode: null`, including for an adjustment (the S5a CHECK). Map `ReversalNotLatest` on the reversal route; `DocumentPoster`'s `default` still throws for it.
- **S10:** a transfer-in or positive adjustment into a negative balance reaches the `InvalidOperationException` that S10 replaces with `Revaluation`.
- **S9/S11 tests:** reuse `LedgerInvariants.AssertHoldAsync` (INV-1/2/3/5; add INV-4 in S9) and `RetryLog` for T9 and the parallel same-key reversals.

## Log
- 2026-11-11: planned
- 2026-11-11: audited, 6 fixes applied (1 BLOCK resolved by the user's D5 decision, 5 FIX)
- 2026-11-11: build started on IMS-10; rollback point c4253487ed66f743a48c7a253b414dad704c4cde
- 2026-11-11: 01 done (opus, 0 retries): request records, Transfer/Adjustment step 1 (Validate split into Location/Body/StandardAmounts helpers, receipt/issue order unchanged), two canonical forms; 199 unit tests
- 2026-11-11: 02 done (opus, 0 retries): DocumentPoster.Shape(kind) switch, D6 header checks, signed-delta line building, UnitCostRequired/quantityDelta mapping, ReasonCode through PostingCommand → step 4 → DocumentResponse; dropped the now-unused `using Inventory.Persistence` in both handlers; 404 tests
- 2026-11-12: 03 done (opus, 0 retries): transfers (Staff+Admin) and adjustments (Admin) routes, handlers registered, two T5 probes; route guard green; 406 tests
- 2026-11-12: 04 done (sonnet, 0 retries): TransferTests (9), AdjustmentTests (14), T1 API run of set A; PostingTests modifiers only; 441 tests
- 2026-11-12: 05 done (opus, 0 retries): RetryLog, ApiFactory.Retries, LedgerInvariants (ports LedgerHarness), T3 green 3/3 with 0 execution-strategy retries (spec §6 step 6 lock order held), positive control counts ≥ 1; 442 tests
- 2026-11-12: shipped as PR #9 (commit 83aa3bb8); local Commands green (442 .NET, 64 web). CI `build` red: NU1903 on Aspire.Hosting/Aspire.Hosting.AppHost 13.6.4 (GHSA-f7vh-h5g3-7jrh, published 2026-11-10, patched in 13.6.5). Unrelated to the slice; build stopped at the CI gate. AppHost smoke skipped (needs the seed-password secret); covered by Transfer/AdjustmentTests
- 2026-11-13: merged develop (IMS-19 Aspire 13.6.5) into IMS-10 at 11fc0832; PR #9 CI green (build, test). Gate: CI + code-guardian.
- 2026-11-13: code-guardian APPROVE WITH NITS (0 Blockers). Fixes (opus, 0 retries) in 3e4e6f37: T3 forces contention (holder U-locks the middle balance until all 50 transfers queue), 4 inactive item/location tests, ReasonCode passed only for adjustments. 446 .NET green; CI green. Remaining nits to Downstream.
- 2026-11-13: PR #9 merged to develop; plan done
