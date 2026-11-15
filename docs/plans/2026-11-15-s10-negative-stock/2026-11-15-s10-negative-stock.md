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
prompts: 9
graph_built: 2026-11-15 17:51 (refreshed for this plan; no C# call edges for these symbols, so blast radius is from grep)
tags: [plan]
---
# S10 Negative stock (IMS-14)

## Request
> IMS-14

This plans the existing unit `IMS-14 — S10 Negative stock · Added · spec: §12 S10 · depends: IMS-8, IMS-11 · model: opus` (TASKS.md, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S10 (`:679`): *"Location toggle (API and switch, with its lock order), `CostingPolicy` estimate and revaluation branch, negative balances in red. Tests: T15 · code-guardian review. Tier: Should"*.

Spec §11 T15 (`:648`): *"Vector sets N and X; toggle off with a negative balance; Staff tries to flip the toggle → Estimate costing, revaluation and value conservation; 422 negative-balances-exist; 403 for Staff"*. §13 (`:693-702`): Staff `403` on the toggle; N and X reproduce to the fourth decimal in unit tests **and through the API**; the toggle can't be turned off over a negative balance; negative balances in red on the stock screen.

Spec rules used: §5 (`:225-230` `AllowNegativeStock`, physical only; `:250-258` `LineNo` 1000 + n, quantity 0 only on a revaluation; `:266-277` `AllowNegative` and the balance CHECKs; `:286` the toggle updates the location and all its balance rows in one transaction, in §6's lock order), §6 (`:303` revaluation row; `:311` step 5; `:313` step 7; `:314` step 8; `:325-326` toggle-off `422 negative-balances-exist`, lock → check → update; `:332` a reversal gets `422 insufficient-stock` when the toggle was turned off since the original), §7 (`:358-390` the estimate branch, `Revaluation` and LUC after it; `:434-467` sets N and X), §9 (`:540` "PUT carries allowNegativeStock"; `:591` `negative-balances-exist` 422 with the blocking balances), §10 (`:600` negative balances in red; `:603` "Allow negative stock switch (Admin, with a warning)").

It also covers what earlier units locked for S10:
- IMS-5 (S4): `PUT /locations/{id}` gains `allowNegativeStock`, with the §6 lock order and the `StockBalances.AllowNegative` update; the Locations screen gains the switch.
- IMS-7 (S5b): replace the `qty > OnHand` rejection in `OutflowCost` (when `AllowNegative`) with the estimate branch; replace the negative-inflow `InvalidOperationException` in `Post` with `Revaluation`, emitting a movement at `LineNo + 1000`; add sets N and X to `CostingVectorTests`.
- IMS-13 (S9) Downstream: T15 adds "toggle off since the original → reversal `422 insufficient-stock`"; reversals mirror revaluation movements; `AttemptAsync`'s items × locations count check must still hold once revaluations add `ADJ`.
- IMS-11 (S7): red negatives on the stock screen were deferred to S10.

| # | Requirement |
|---|---|
| R1 | Estimate costing (§7): with the toggle on, an outflow past zero drains what is on hand at its value and estimates the rest at the current average; at or below zero it costs `Round4(qty × (LastUnitCost ?? 0))`. With the toggle off, `422 insufficient-stock` as today. |
| R2 | Revaluation (§7): any inflow except a reversal (receipt, positive adjustment with a unit cost, transfer-in) that lifts a negative physical balance to zero or above revalues the stock on hand at the inflow's unit cost. It posts the variance as a zero-quantity movement at `LineNo 1000 + n`, physical → ADJ when positive and ADJ → physical when negative, and nothing when zero. `LastUnitCost` is set after the revaluation. Value is conserved, and every movement cost stays ≥ 0. |
| R3 | The engine locks every balance a revaluation can touch inside §6's single step-6 statement, and a cost outside `decimal(19,4)` is a `400`, never a `500`. |
| R4 | `PUT /api/locations/{id}` (Admin; Staff `403`) accepts an optional `allowNegativeStock`. Changing it locks the location's balances through `IX_Balances_Location` (`UPDLOCK, HOLDLOCK`), checks them, then updates the location and every balance's `AllowNegative` in one transaction. Turning it off while any balance has `OnHand < 0` returns `422 negative-balances-exist` with the blocking balances. When the field is absent, the toggle is left unchanged. |
| R5 | `negative-balances-exist` is in spec §9 (already), Kernel `ProblemTypes` and Angular `PROBLEM_MESSAGES` in the same PR. |
| R6 | `/locations` shows Admins an "Allow negative stock" switch on each active physical location. Turning it on asks for confirmation with a warning; turning it off is direct; a rejection shows the mapped message and the list reloads. Staff see a "Negative stock allowed" chip instead. |
| R7 | `/stock` shows negative quantities and values in red. |
| R8 | Tests: T15 (sets N and X in unit tests and through the API; toggle off over a negative balance → 422; Staff → 403), plus the toggle's mirror, lock-order and posting races; transfer-in, adjustment and never-received cases; replay; a reversal restores a negative balance exactly; a reversal after toggle-off → 422; Vitest for the service, messages, switch and red cells. |
| R9 | Every `## Commands` line and CI `build` and `test` are green; code-guardian reviews the PR (High-stakes `Domain/**`, `Persistence/**`; Kernel is Shared code; CLAUDE.md lists S10). |

## Context used
- Note: `CLAUDE.md` (Conventions: decimal, `AwayFromZero` 4 dp; Review constraints; High-stakes paths `Domain/**`, `Persistence/**`; Shared code `src/Shared/Kernel/**`; Hidden coupling: `AllowNegative` mirrors `Locations` under §6's lock order, slugs ↔ `PROBLEM_MESSAGES`, vector tables ↔ `CostingPolicy` tests).
- Note: spec §5, §6, §7, §9, §10, §11 T15, §12 S10, §13, §14 (`:720-721` negative stock is a per-location toggle; settlement is the revaluation movement, audit option A), as cited under Request.
- Note: spec audit `Inventory System — Sprint 1 Spec.audit.md:14` (the BLOCK that produced the revaluation design and set X), `:18` (stale snapshot of `AllowNegative` at step 5, fixed by step 7/9), `:23(b)` (reversal after toggle-off → 422).
- Note: TASKS.md Downstream for IMS-3 (slug in §9 + Kernel + Angular in one PR; execution strategy for anything that holds locks), IMS-4 (raw SQL only through EF; T5 per route), IMS-5 (S10 bullet above; deactivate's lock pattern), IMS-6 (lock order; `HOLDLOCK` next-key; deadlock classes retried under the strategy; Testcontainers flake), IMS-7 (S10 bullet above; restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1`), IMS-8 (decimal scale differs between a 201 and a replay: compare numbers), IMS-9 (seed posts receipts through `PostingEngine`).
- Note: S5b plan `2026-11-09-s5b-costing-rules.md:94,100,104,106,130` (LUC fold already folds "the inflow and its revaluation"; harness CHECK accepts qty-0 revaluations; reversal copies `LineNo`); S9 plan `2026-11-15-s9-reversals.md:77` (D7 reversal lock set tolerates `ADJ`), `:215` (S10 downstream); S4 prompts `s4-master-data-05:48`, `-09:43` (no toggle in S4); S7 prompt `s7-stock-screens-06:34,46` (no red until S10).
- Graph: `graphify update .` (stale since 2026-11-13); `graphify affected "PostingRules"` and `"UpdateLocationHandler"` return no nodes (no C# call edges), so callers come from grep (workflow §4).
- Code: `Domain/CostingPolicy.cs:32-43` (`OutflowCost` with the S10 placeholder at `:36`), `:49-50` (`LastUnitCostAfterInflow`), `:58-110` (`LastUnitCost` fold, `end` = last movement of the inflow's document). `Domain/PostingRules.cs:24-147` (`Post`; placeholder `:126-131`; `Store`/`Current`; the `order` list), `:149-278` (`Reverse`: mirrors every movement, qty 0 included; short check skipped under `AllowNegative`), `:280-284` (`Find`). `Domain/Balance.cs`, `Domain/LedgerMovement.cs`, `Domain/PostingRejections.cs`.
- Code: `Persistence/PostingEngine.cs:22-23` (`MaxColumn`), `:117-246` (`AttemptAsync`: step 5 keys `:148-152`, step-5 insert copies `AllowNegativeStock` `:158-164`, step 6 `:172-183` with the items × locations count check, step 7 `:185-215`, step 8 `:217-226`, D1 range guard `:228-232`), `:426-445` (`LockBalancesAsync`), `:476-527` (`WriteAsync`: `AllowNegative` from step 7, `LastMovementId` from new movements), `:29-60` (`FindExistingAsync` returns physical balances only). `Persistence/PostingCommand.cs` (outcomes). `Features/Posting/DocumentPoster.cs:104-107` (counterpart lookup by `Kind`), `:173-176` (`BalanceOutOfRangeOutcome` → `lines[i].quantity|quantityDelta` by item). `Features/Reversals/ReverseDocument/Handler.cs:104-106` (`InsufficientStock` mapped, "reachable once S10's toggle exists"). `Seeding/InventorySeeder.cs:143,210` (posts through the engine).
- Code: `Features/Locations/UpdateLocation/{Endpoint,Handler,Request}.cs` (validate → load → system → duplicate code → rowversion → save), `Features/Locations/DeactivateLocation/Handler.cs:13-73` (strategy + transaction + `IX_Balances_Location` lock pattern to copy), `Features/Locations/LocationResponse.cs` (already returns `allowNegativeStock`), `Features/Problems.cs:24-31,95-96` (`StockNotZero`, `BlockingBalance`), `src/Shared/Kernel/Problems/ProblemTypes.cs`, `Persistence/InventoryDbContext.cs:45,53,134,141` (CHECKs, `IsRowVersion` on balances). `Features/Stock/ListStock/Handler.cs:24,65` and `Features/Items/GetItem/Handler.cs:24,36` (physical only; average null when `OnHand <= 0`). `Features/Items/DeactivateItem/Handler.cs:39-41` (locks all the item's balances, filters virtual). `Features/Documents/ListDocuments/Handler.cs:13-14,76` (revaluations aren't counted as lines).
- Tests: `tests/Inventory.UnitTests/{CostingPolicyTests.cs:20-21,107-118, PostingRulesTests.cs:20-45,109-117,308-315, LedgerHarness.cs, CostingVectorTests.cs:143-206}`; `tests/Inventory.IntegrationTests/{PostingVectorTests.cs:14-60, PostingTests.cs:556-600,648-660, AdjustmentTests.cs:437-440, TransferTests.cs:345, ReversalTests.cs:20-58, LedgerInvariants.cs:24-110 (toggle-aware, qty-0-aware), LedgerRows.cs, LocationsTests.cs:104-132,315-410, ItemsTests (ExecuteAsync, AssertLockTimeoutAsync, AssertRangeLockedAsync, AssertStockNotZeroAsync), PostingConcurrencyTests.cs:33-60, SeedTests.cs:28,116, DocumentQueryTests.cs:302-327, ItemHistoryTests.cs:216-245, TenantIsolationTests.cs:409-432}`.
- Web: `web/src/app/core/problems/problem-messages.{ts,spec.ts}`, `locations/{locations.ts,html,scss,spec.ts}`, `locations/locations.service.{ts,spec.ts}`, `locations/location-dialog.ts` (sends no toggle), `shared/confirm-dialog.ts`, `stock/{stock.ts,stock.spec.ts}`, `web/src/styles.scss`.

## Options
The real choice is how the `(item, ADJ)` balance that a revaluation touches gets into §6's step-6 lock. Step 5 and step 6 run before step 8 knows whether a revaluation is needed.

| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| A. Always add `(item, ADJ)` to the step-5/6 keys of receipt and transfer lines | About 10 lines in the engine | Part of one prompt | Low: one statement, clustered-key order unchanged; the cross-product check still holds | One empty virtual `(item, ADJ)` row per item received or transferred (seed 50 → 69 balances); receipts and transfers of one item serialize on it | Within §6 ("every balance row the document touches") |
| B. Add it only when the target's toggle is on, with a retry when the toggle flipped between that read and step 6 | Pre-read plus a custom retry path | +1 prompt | Medium: a rare race path on the high-stakes engine, hard to test | No extra rows | Within §6 |
| C. Lock `ADJ` in a second statement after step 8 | Small | Part of one prompt | Medium: out-of-order lock, deadlocks rely on 1205 retries | — | **Changes §6** (one-statement rule) |

**Recommendation:** A, because the lock order stays a single, reviewable statement on the ledger's only write path, and the costs don't show at SMB scale. **Chosen:** A (user, 2026-11-15).

## Decisions
- **D1 · `ADJ` in the lock set (user, 2026-11-15, option A).** `AttemptAsync` reads the tenant's Adjustment location id (`db.Locations.AsNoTracking().Where(l => l.Kind == LocationKind.Adjustment).Select(l => l.Id).SingleAsync`, as `DocumentPoster.cs:106` does). Virtual locations are immutable, so a snapshot read is safe. For every line of a `Receipt` or a `Transfer` it adds `(line.ItemId, adjustmentId)` to step 5's keys. An `Adjustment` line already touches `ADJ`, and an `Issue` never revalues. Step 6's single statement locks it in clustered-key order with the rest. The items × locations count check still holds, because every receipt line touches `{location, SUP, ADJ}` and every transfer line touches `{A, B, ADJ}`. `ReverseAsync` is unchanged (S9 D7): the original's revaluation movements put `ADJ` in its touched set. Spec §6 text isn't edited; this decision records the reading of step 5.
- **D2 · `PostingRules.Post` gains a required `Guid adjustmentLocationId`.** The pure rules decide the revaluation, and the engine passes the id. When a revaluation is needed and `(item, ADJ)` is missing from `balances`, or isn't virtual, it's a bug: `InvalidOperationException`.
- **D3 · Estimate branch, exactly §7 `:359-368`.** `qty > OnHand && !AllowNegative` → `InsufficientStockException` (unchanged). `qty == OnHand` → `StockValue`. `qty < OnHand` → `Round4(qty × StockValue / OnHand)`. Otherwise, `OnHand > 0` → `StockValue + Round4((qty − OnHand) × (StockValue / OnHand))`, else `Round4(qty × (LastUnitCost ?? 0m))`. An outflow never changes `LastUnitCost`.
- **D4 · Revaluation, exactly §7 `:373-387`.** `CostingPolicy.Revaluation(qty, cost, b)`: `onHand = b.OnHand + qty`; `0` if `b.OnHand >= 0 || onHand < 0`; else `b.StockValue + cost − Round4(onHand × cost / qty)`. In `Post`, for a physical target: `value = target.StockValue + cost − revaluation`; `LastUnitCost = LastUnitCostAfterInflow(onHand, value, qty, cost)`. A revaluation > 0 posts `PlannedMovement(1000 + LineNo, item, target, ADJ, 0, revaluation, null)`; < 0 posts `(1000 + LineNo, item, ADJ, target, 0, −revaluation, null)`; 0 posts nothing. The `(item, ADJ)` balance joins the result (unchanged quantity), so the engine sets its `LastMovementId`. Never on a reversal: `PostingRules.Reverse` mirrors the original's revaluation and computes none (unchanged).
- **D5 · Movement order: all lines by `LineNo`, then all revaluations by `LineNo`** (`1, 2, 1001, 1002`). The engine takes ids in that order. Each item appears once per document, so on every balance the revaluation is the document's last movement. That is what S9's latest-only check and `CostingPolicy.LastUnitCost`'s `end` expect. `DocumentResponse` already sorts by `LineNo`.
- **D6 · The range guard covers movement costs.** After step 8, a planned movement with `TotalCost > MaxColumn` returns `BalanceOutOfRangeOutcome(m.ItemId, m.FromLocationId)`, which `DocumentPoster` already maps to `400 validation` on `lines[i].quantity` or `quantityDelta`. An estimate (`StockValue + extra`) or a revaluation can exceed `decimal(19,4)` while both new balances fit, which would otherwise fail with 8115 and a `500`. Reversals keep no guard (S9 D9).
- **D7 · `allowNegativeStock` is optional on PUT (user, 2026-11-15).** `UpdateLocationRequest(string? Code, string? Name, byte[]? Version, bool? AllowNegativeStock)`. Absent or null leaves the toggle unchanged, so the edit dialog, the T5 probe (`TenantIsolationTests.cs:422`) and the Staff test keep working. `POST /locations` doesn't take it: a new location starts off (§9 names only PUT).
- **D8 · Toggle flow (§5 `:286`, §6 `:325-326`).** The handler validates as today (`400`), then runs `UpdateAsync` inside `db.Database.CreateExecutionStrategy()`. Each attempt clears the change tracker and begins a transaction; then come load (`404`), system (`400`) and duplicate code (`400`), as today. **Only when the value changes:** it locks the location's balances with Deactivate's statement, `SELECT * … WITH (UPDLOCK, HOLDLOCK, FORCESEEK, INDEX([IX_Balances_Location])) WHERE [LocationId] = {id}`, **tracked**. When turning off, any row with `OnHand < 0` → `422 negative-balances-exist`, with those rows ordered by `ItemId` and nothing written. It then sets `AllowNegative = allow` on every locked row and `AllowNegativeStock = allow` on the location. Code, name, `UpdatedAt`/`UpdatedBy` and the rowversion `OriginalValue` are set as today, then one `SaveChanges` and a commit. A stale version → `409` (rolled back). The balance updates are per-row by primary key from tracked entities, never a set-based `UPDATE` that could scan other rows. A posting that waits on these locks reads the new flag at step 7. A posting that inserts a new balance after the commit may copy the old flag at step 5, and step 9 overwrites it from step 7 (existing design; spec audit `:18`).
- **D9 · Turning the toggle on** takes the same locks and mirrors to every balance, with no check. The server allows the toggle on an inactive physical location, as it allows code and name edits; the UI hides the switch there.
- **D10 · `negative-balances-exist`.** `Problems.NegativeBalancesExist(IEnumerable<StockBalance>)`: 422, title "Negative balances exist", extension `balances` = `BlockingBalance(ItemId, LocationId, OnHand, StockValue)` (the `stock-not-zero` shape). Kernel `ProblemTypes.NegativeBalancesExist = "negative-balances-exist"`. `PROBLEM_MESSAGES` gets `'negative-balances-exist': 'Some items here are below zero. Receive or correct them to zero or above before turning negative stock off.'`. Spec §9 already lists the slug.
- **D11 · UI.** `/locations`: for Admins, a `mat-slide-toggle` labelled "Allow negative stock" on each **active, non-system** row. Turning it on opens `ConfirmDialog` (title "Allow negative stock?", message: "Sales and issues at {code} can then take stock below zero. Costs past zero are estimated from the last cost and corrected when stock arrives.", confirm "Allow"). Cancel reverts the switch and sends nothing. Turning it off is direct. Both call `LocationsService.setAllowNegativeStock(location, allow)` (PUT with the row's `code`, `name`, `version`). Success shows a snack bar and reloads; failure shows `problemMessage` and reloads, which reverts the switch. The switch is disabled while its request is in flight. Staff see a "Negative stock allowed" chip on rows where it's on. `/stock`: the Qty and Value columns get `cellClassRules: { 'stock-negative': p => typeof p.value === 'number' && p.value < 0 }`, styled in the global `web/src/styles.scss` as `.ag-cell.stock-negative { color: var(--mat-sys-error); }`, because AG Grid renders cells outside Angular's emulated encapsulation.
- **D12 · One unit, IMS-14.** The existing ticket; §12 makes S10 one PR. High-stakes (`Domain/**`, `Persistence/**`) plus Kernel (Shared code): code-guardian required (CLAUDE.md lists S10).

**Assumptions:**
- Each tenant has exactly one `Adjustment` location (filtered unique index `(TenantId, Kind)`, `ProvisionTenantAsync`), and users can't edit or deactivate it (S4). So D1's snapshot read never changes under a posting.
- The empty `(item, ADJ)` rows are harmless to every reader: `GET /stock`, `GET /items/{id}` and the posting response filter `!IsVirtual`; item deactivation filters virtual rows (`DeactivateItem/Handler.cs:41`); `LedgerInvariants` accepts a row with no movements (0, null `LastMovementId`). The only count that changes is `SeedTests.BalanceTotal`.
- Contention on `(item, ADJ)` doesn't matter at SMB scale. T3 (50 parallel transfers) still passes, because the extra row is locked in the same statement and order.
- The item detail screen, document detail and item history need no red styling or revaluation label: §10 asks for red only on `/stock`, and S7/S8 already render revaluation rows (`DocumentQueryTests.cs:302`, `ItemHistoryTests.cs:216`).
- The post-stock form's "warn before submit if a line exceeds on-hand" (§10) stays a warning at toggle-on locations; the server decides.
- D6's movement-cost overflow needs balances near ₱10^15; the guard exists so no input reaches a `500`.
- The Testcontainers start-up flake (IMS-6) may need `dotnet test -- --max-parallel-test-modules 1`.

**Non-goals:**
- S11 (health and rebuild SQL), S12 (T14), S13/S14.
- Migrations, columns, indexes, `security.TenantPolicy` or trigger changes: `AllowNegativeStock`, `AllowNegative`, `LastUnitCost`, both CHECKs and the `Quantity = 0` rule exist since S5a.
- Editing spec §6 or §7; changing `PostingRules.Reverse`, `CostingPolicy.LastUnitCost`, `RequestHash`, `DocumentPoster`, the reversal route, or any read endpoint.
- A toggle on `POST /locations`; red or revaluation labels outside `/stock`; changes to the post-stock form.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Domain/CostingPolicy.cs` | M | Estimate branch (D3); `Revaluation` (D4) | R1, R2 | 01 |
| `tests/Inventory.UnitTests/CostingPolicyTests.cs` | M | Estimate and `Revaluation` cases; drop the placeholder's `true` case | R1, R2, R8 | 01 |
| `tests/Inventory.UnitTests/PostingRulesTests.cs` | M | 01: issue past zero estimates (N2). 02: every `Post` call passes `Adj`; revaluation cases replace the placeholder test | R1, R2, R8 | 01, 02 |
| `src/Modules/Inventory/Inventory/Domain/PostingRules.cs` | M | `adjustmentLocationId`; revaluation movements and balances (D2, D4, D5) | R2 | 02 |
| `src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs` | M | ADJ id and lock keys (D1); pass the id; movement-cost guard (D6) | R2, R3 | 02 |
| `tests/Inventory.UnitTests/LedgerHarness.cs` | M | 02: pass the ADJ id and add `(item, ADJ)` to the working copy. 03: per-location toggle; toggle-aware INV-3 | R2, R8 | 02, 03 |
| `tests/Inventory.IntegrationTests/SeedTests.cs` | M | `BalanceTotal` 50 → 69 (D1) | R3 | 02 |
| `tests/Inventory.UnitTests/CostingVectorTests.cs` | M | Sets N and X; reversal restore and toggle-off reversal in the harness | R8 | 03 |
| `src/Shared/Kernel/Problems/ProblemTypes.cs` | M | `NegativeBalancesExist` (D10) | R4, R5 | 04 |
| `src/Modules/Inventory/Inventory/Features/Problems.cs` | M | `NegativeBalancesExist(...)` (D10) | R4 | 04 |
| `src/Modules/Inventory/Inventory/Features/Locations/UpdateLocation/Request.cs` | M | `bool? AllowNegativeStock` (D7) | R4 | 04 |
| `src/Modules/Inventory/Inventory/Features/Locations/UpdateLocation/Handler.cs` | M | Strategy, transaction, lock → check → mirror (D8, D9) | R4 | 04 |
| `tests/Inventory.IntegrationTests/LocationsTests.cs` | M | 04: rewrite the "ignores" test; toggle API tests; `AllowNegativeAsync` helper. 06: lock-order tests | R4, R8 | 04, 06 |
| `tests/Inventory.IntegrationTests/PostingVectorTests.cs` | M | T1/T15 API run of sets N and X | R1, R2, R8 | 05 |
| `tests/Inventory.IntegrationTests/NegativeStockTests.cs` | C | Transfer-in, adjustment, never-received, replay, reversal cases | R1–R3, R8 | 05 |
| `tests/Inventory.IntegrationTests/PostingConcurrencyTests.cs` | M | 20 parallel issues past zero, then a settling receipt | R1, R2, R8 | 06 |
| `web/src/app/core/problems/problem-messages.{ts,spec.ts}` | M | `negative-balances-exist` (D10) | R5 | 07 |
| `web/src/app/locations/locations.service.{ts,spec.ts}` | M | `setAllowNegativeStock` | R6 | 07 |
| `web/src/app/locations/{locations.ts,locations.html,locations.scss,locations.spec.ts}` | M | Switch, confirm, chip (D11) | R6 | 08 |
| `web/src/app/stock/{stock.ts,stock.spec.ts}`, `web/src/styles.scss` | M | Red negatives (D11) | R7 | 09 |

## Blast radius
**Risk:** high. Prompts 01–02 change the money math and the ledger's only posting write path (High-stakes `Domain/**`, `Persistence/**`), and prompt 04 adds a §6 lock-order write to master data plus a Kernel constant (Shared code).

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `PostingRules.Post` callers: `PostingEngine.cs:221`, `LedgerHarness.cs:154`, ~20 calls in `PostingRulesTests` | Direct | New required parameter (D2) | **Yes** (compile) | Prompt 02 updates all three in the same prompt |
| `CostingPolicy.OutflowCost` caller `PostingRules.cs:102`; `CostingPolicyTests.cs:107-118` and `PostingRulesTests.cs:109-117` (`allowNegative: true` expects a throw) | Direct | Estimate branch | **Yes** (two theory cases) | Prompt 01 keeps the `false` case and adds estimate tests |
| `PostingRulesTests.cs:308-315` (`…until_S10`) | Direct | Placeholder removed | **Yes** | Prompt 02 replaces it with revaluation tests |
| `PostingEngine.AttemptAsync` step 5/6 (receipts, transfers, seed) | Direct | `(item, ADJ)` added (D1) | No: one statement, key order; count check holds | Prompt 02 Done-when runs `PostingTests`, `TransferTests`, `AdjustmentTests`, `PostingConcurrencyTests` (T2, T3, T4, step-4/7 races), `PostingVectorTests`, `ReversalTests`, `SeedTests` |
| `SeedTests.BalanceTotal` (`SeedTests.cs:28,116`) | Non-graph (test) | 19 empty `(item, ADJ)` rows from 31 opening receipts | **Yes** | Prompt 02: 50 → 69, after checking the distinct item count of `InventorySeeder.OpeningStock` |
| `FindExistingAsync` (replays) and `DocumentResponse` | Direct | Revaluation movements in `movements`; balances still physical only | No: additive rows, sorted by `LineNo` | Prompt 05 replay test |
| `ReverseAsync`, `PostingRules.Reverse`, the reversal route | Transitive | Mirrors qty-0 movements; `InsufficientStock` now reachable after toggle-off | No: unchanged code; S9 D7 lock set includes `ADJ` | Prompt 05 reversal tests (toggle on → exact restore; toggle off → 422) |
| `DocumentPoster` `BalanceOutOfRangeOutcome` mapping | Direct | D6 reuses it for movement costs | No: maps by item | Existing D1 tests stay green |
| Item deactivation lock (`DeactivateItem/Handler.cs:39`) | Non-graph (lock order) | Locks every balance of the item, now including `(item, ADJ)` | No: one more row in the same `HOLDLOCK` range; virtual filtered | `ItemsTests` in `dotnet test` |
| `UpdateLocationHandler` callers: Angular `LocationsService.update` / edit dialog, T5 probe `TenantIsolationTests.cs:422`, `LocationsTests` Staff and update tests | Direct | Optional field; now inside a strategy + transaction | No, except `Update_changes_code_and_name_and_ignores_allowNegativeStock` | Prompt 04 rewrites that test; T5 still gets 404 (validation needs no toggle) |
| `StockBalances.AllowNegative` ↔ `Locations.AllowNegativeStock` | Hidden coupling | Toggle mirror (D8) | No if lock → check → update | Prompts 04 (mirror tests) and 06 (lock-order tests) |
| Posting step 7 / step 9 `AllowNegative` | Hidden coupling | Reads the flag under `READCOMMITTEDLOCK` and writes it to balances | No: D8 commits flag and balances together | Prompt 06 "holds off postings" test |
| `StockBalances` CHECKs and `StockMovements` CHECK (`Quantity = 0 AND TotalCost > 0`) | Non-graph (schema) | Negative balances and qty-0 movements | No: the schema allows both since S5a; D4 posts a revaluation only when it is ≠ 0 | Harness CHECK assertion; integration runs |
| Kernel `ProblemTypes` (Shared code) | Direct | One constant | No: additive | — |
| ProblemDetails slug ↔ `PROBLEM_MESSAGES` | Hidden coupling | New slug | **Yes** until mapped | Prompt 07, same PR (R5) |
| Vector tables ↔ `CostingPolicy` unit tests | Hidden coupling | Sets N and X | No | Prompts 03 and 05 copy spec `:442-467` exactly |
| `GET /stock`, `GET /items/{id}` | Transitive | Negative `OnHand`/`StockValue` | No: physical only; average null when `OnHand <= 0` | Prompt 05 never-received case asserts the stock row |
| `GET /documents`, `GET /documents/{id}`, item history | Transitive | Revaluation rows | No: S7/S8 handle `LineNo >= 1000` and qty 0 | Existing `DocumentQueryTests:302`, `ItemHistoryTests:216` |
| T5 route guard, T10 `No_request_type_carries_a_date`, T12 | Non-graph (tests) | No new route; `bool?` field; no new public types | No | `dotnet test` |
| `locations` screen and spec; `stock` screen and spec | Direct (web) | New switch, chip, service method; `cellClassRules` | **Yes**: `mat-slide-toggle` renders an inner `<button role="switch">`, which breaks `locations.spec`'s `buttons(row('QC'))` expectations | Prompt 08 narrows the spec's `buttons` helper to `button:not([role="switch"])` and stubs the service method, `MatDialog` and `MatSnackBar`; prompt 09 |
| S11 health and rebuild | Downstream | Empty `ADJ` rows; qty-0 movements; INV-5 includes revaluations | No (not built yet) | Downstream note |

**Known risks in the touched area:**
- **Lock order (IMS-6, §6):** the step-6 statement keeps `UPDLOCK, ROWLOCK, FORCESEEK, INDEX(PK_StockBalances)` and one round trip; prompt 02 only adds keys. The toggle reuses Deactivate's `IX_Balances_Location` statement verbatim. `HOLDLOCK` also locks the next key, so a toggle can briefly block an unrelated posting (IMS-6).
- **Stale `AllowNegative` at step 5** (spec audit `:18`): kept correct by step 9 writing the step-7 flag. Prompt 02 must not touch `WriteAsync`'s `row.AllowNegative` line.
- **Retries** (IMS-3): the toggle holds locks, so it runs inside `CreateExecutionStrategy()` with `ChangeTracker.Clear()` per attempt, like Deactivate.
- **Decimal scale** (IMS-8): compare JSON numbers with `GetValue<decimal>()`.
- **Testcontainers start-up flake** (IMS-6): `dotnet test -- --max-parallel-test-modules 1`.
- **Restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1`** on this machine (IMS-7).

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-14 | S10 Negative stock | Added | 01, 02, 03, 04, 05, 06, 07, 08, 09 | IMS-8, IMS-11 (merged) | yes (`Domain/**`, `Persistence/**`, Kernel) | See below |

Acceptance criteria for IMS-14:
- **R1:** `OutflowCost(6, 4/200, toggle on)` = 300.0000; `(1, −2/−150 LUC 50)` = 50.0000; `(2, 0/0 LUC 56)` = 112.0000; never-received (`LUC` null) = 0.0000; toggle off over on-hand → `InsufficientStockException`. Through the API, an issue past zero at a toggle-on location is `201` with those costs; at a toggle-off location it's still `422 insufficient-stock`.
- **R2:** Set N reproduces N1–N9 (costs, qty/value and `LastUnitCost`), with N4 posting a `LineNo 1001` K → ADJ movement of 18.0000 and N7 one of 8.0000; set X reproduces X1–X4, with X3 posting `LineNo 1001` ADJ → K2 of 80.0000. Both run in unit tests and through the API, and set N conserves value (received 1,180.0000 = issued 1,154.0000 + revaluations 26.0000). A transfer into a negative balance revalues at the destination; a positive adjustment with a unit cost revalues against ADJ; an inflow that leaves the balance negative, or settles with zero variance, posts no revaluation.
- **R3:** A receipt or transfer locks and, if new, creates `(item, ADJ)` in step 5/6. The seed creates 69 balances. A movement cost above 999,999,999,999,999.9999 returns `400 validation`, not `500`.
- **R4:** Admin PUT with `allowNegativeStock: true` → `200` with `allowNegativeStock: true`, and every balance of the location has `AllowNegative = 1`; `false` mirrors back to 0. With a balance at −2 / −100.0000, PUT `false` → `422 negative-balances-exist` with that balance, writing nothing. Absent field → unchanged. Stale version → `409`. Staff → `403`, nothing changes. A toggle-off waits for an uncommitted negative balance and then answers `422`. An issue past zero that starts while a toggle-off is committing gets `422 insufficient-stock` once it commits.
- **R5:** `negative-balances-exist` is in spec §9, Kernel `ProblemTypes` and `PROBLEM_MESSAGES`.
- **R6:** Admin sees the switch on active physical rows only. Turning it on opens the warning confirm (cancel → no request, switch off); confirm → PUT `allowNegativeStock: true` with the row's code, name and version, then reload. Turning it off → PUT `false` without a confirm. A `422` shows the mapped message and reloads. Staff see the chip, no switch.
- **R7:** Stock rows with `onHand < 0` or `stockValue < 0` get the `stock-negative` class on those cells, shown in the theme's error colour.
- **R8:** `CostingPolicyTests`, `PostingRulesTests`, `CostingVectorTests` (N, X), `PostingVectorTests` (N, X), `NegativeStockTests`, the new `LocationsTests` and `PostingConcurrencyTests` facts, and the web specs pass. Reversing a revaluing receipt with the toggle on restores −2 / −100.0000, `LastUnitCost` 50.0000 and ADJ 0; with the toggle off since, it's `422 insufficient-stock`. 20 parallel issues past zero all post `201`; the settling receipt revalues by 30.0000. `LedgerInvariants` holds after every case.
- **R9:** Every `## Commands` line and CI `build` and `test` are green; a code-guardian review is requested at the PR gate.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Domain: estimate branch and `Revaluation` | IMS-14 | opus | high | — | A | Domain/CostingPolicy.cs, CostingPolicyTests.cs, PostingRulesTests.cs |
| 02 | Revaluation in `Post` and the engine's `ADJ` lock | IMS-14 | opus | xhigh | 01 | B | Domain/PostingRules.cs, Persistence/PostingEngine.cs, LedgerHarness.cs, PostingRulesTests.cs, SeedTests.cs |
| 03 | Vector sets N and X in the harness | IMS-14 | sonnet | high | 02 | C | LedgerHarness.cs, CostingVectorTests.cs |
| 04 | Toggle API: `PUT allowNegativeStock` with §6 lock order | IMS-14 | opus | high | 01 | D | Kernel ProblemTypes.cs, Features/Problems.cs, UpdateLocation/{Request,Handler}.cs, LocationsTests.cs |
| 05 | T15 through the API: N, X and posting cases | IMS-14 | sonnet | high | 02, 04 | E | PostingVectorTests.cs, NegativeStockTests.cs |
| 06 | Toggle lock order and parallel issues past zero | IMS-14 | opus | high | 04, 05 | F | LocationsTests.cs, PostingConcurrencyTests.cs |
| 07 | Web: problem message and `setAllowNegativeStock` | IMS-14 | haiku | low | — | A | problem-messages.{ts,spec.ts}, locations.service.{ts,spec.ts} |
| 08 | Web: Allow negative stock switch | IMS-14 | sonnet | medium | 07 | B | locations.{ts,html,scss,spec.ts} |
| 09 | Web: red negatives on `/stock` | IMS-14 | haiku | low | — | C | stock.{ts,spec.ts}, styles.scss |

- 01 `opus`: High-stakes `Domain/**`; it's the money math that every outflow runs through.
- 02 `opus`/`xhigh`: High-stakes `Domain/**` and `Persistence/**`; it changes the ledger's only posting write path and its §6 lock set.
- 04 `opus`: Kernel is `Shared code:`, and the handler implements a §6 lock-order rule and the `AllowNegative` Hidden coupling.
- 06 `opus`: concurrency; deterministic lock-holder races that must prove the lock → check → update order.

The .NET prompts share one solution build, so 01 → 02 → 03 → 04 → 05 → 06 run in order (04 needs only 01's estimate branch, but shares the build). The web prompts share `web/` but no .NET project: 07 may run beside 01, 08 beside 02, and 09 beside 03. 07 → 08 → 09 never overlap each other.

## End-to-end verification
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if the Testcontainers start-up flake shows: dotnet test -- --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false
cd ..
# R1, R2, R8 (unit): estimate, revaluation, sets N and X
dotnet test --project tests/Inventory.UnitTests --filter-class '*CostingPolicyTests' --filter-class '*PostingRulesTests' --filter-class '*CostingVectorTests'
# R1-R4, R8 (API): N and X, posting cases, toggle API and lock order, races, and the posting regression net
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*NegativeStockTests' --filter-class '*PostingVectorTests' --filter-class '*LocationsTests' --filter-class '*PostingConcurrencyTests' --filter-class '*ReversalTests' --filter-class '*PostingTests' --filter-class '*TransferTests' --filter-class '*AdjustmentTests' --filter-class '*SeedTests' --filter-class '*TenantIsolationTests'
# R5: the slug in all three places
grep -c 'negative-balances-exist' src/Shared/Kernel/Problems/ProblemTypes.cs web/src/app/core/problems/problem-messages.ts "docs/Inventory System — Sprint 1 Spec.md"
# No S10 placeholders left
! grep -n 'S10' src/Modules/Inventory/Inventory/Domain/*.cs
# Review constraints: no UPDATE/DELETE on the ledger tables, no double/float, no unnamed IgnoreQueryFilters, no wall clock
git diff develop -- src | grep -nE 'UPDATE +\[?inventory\]?\.\[?Stock(Documents|Movements)|DELETE|IgnoreQueryFilters\(\)|DateTime(Offset)?\.(Now|UtcNow)|\b(double|float)\b' || echo "constraints clean"
# R6, R7 (web): problem-messages.spec, locations.service.spec, locations.spec, stock.spec run in the Vitest suite above
# Manual smoke (optional, needs the seed-password secret): dotnet run --project src/AppHost; as the demo Admin on /locations,
# turn on "Allow negative stock" for MKT (confirm), use/sell more than on hand -> /stock shows the row in red; try to
# turn it off -> "Some items here are below zero..."; as Staff the switch is absent and the chip shows.
```

## Rollback
Revert the PR. There's no schema change. Before reverting, turn every toggle off, which needs each negative balance brought back to zero or above. Otherwise the reverted build hits the old placeholders: an inflow into a negative balance throws `InvalidOperationException` (`500`), and an outflow past zero is `422`. Revaluation movements already posted stay valid in the append-only ledger, and the reverted read side shows them (S7/S8 handle `LineNo >= 1000`). Empty `(item, ADJ)` balance rows are harmless to the reverted build. Re-applying the PR needs nothing extra.

## Downstream (for TASKS.md after merge)
- **S11:** rebuild must also reset balance rows with no movements (the empty `(item, ADJ)` rows: 0 / 0 / null `LastMovementId`). INV-5's SQL must match `CostingPolicy.LastUnitCost`, including revaluation movements (qty 0, same document as the inflow). INV-3 is toggle-aware (`LedgerInvariants` is the oracle). The toggle doesn't take the ledger applock: it only locks balances and the location row.
- **§6 reading (D1):** a receipt or transfer line touches `(item, ADJ)` at step 5/6, so a possible revaluation is locked in the one statement. Record this in spec §6 only with the user's sign-off (CLAUDE.md: ask before changing §6).
- **S12/T14:** the switch reads "Allow negative stock" with confirm "Allow"; negative cells carry `stock-negative`.

## Log
- 2026-11-15: planned
- 2026-11-15: applied Codex audit (0 applied, 0 disputed): PASS, nothing to apply
- 2026-11-15: audited; ticket step: existing IMS-14, TASKS.md line rewritten in place
