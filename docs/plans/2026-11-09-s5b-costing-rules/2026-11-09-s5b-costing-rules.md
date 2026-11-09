---
type: plan
kind: feature
status: audited
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-09
risk: medium
prompts: 3
graph_built: none
tags: [plan]
---
# S5b Costing rules (IMS-7)

## Request
> IMS-7

This plans the existing unit `IMS-7 — S5b Costing rules · Added · spec: §12 S5b · depends: IMS-2 · model: opus` (TASKS.md:14, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S5b (`:671`): *"`CostingPolicy` (inflow, outflow, default adjustment cost, `LastUnitCost`) and `PostingRules` in `Domain`, pure. The estimate and revaluation branch lands in S10. Tests: T1 unit part (V, R, A) · code-guardian review."*

Planned on branch `IMS-7`, not `develop`. Both point to the same commit, `d66e1f57` (= `origin/develop`), and the tree is clean, so the code planned against is the code the unit branches from.

| # | Requirement |
|---|---|
| R1 | `Inventory.Domain.CostingPolicy` is pure and static. It has: `Round4` (4 dp, `MidpointRounding.AwayFromZero`); `InflowCost`; `DefaultAdjustmentCost` (multiply first, and only when `OnHand > 0`); `OutflowCost` (the remainder when `qty == OnHand`, a rounded share when `qty < OnHand`, `InsufficientStockException` when `qty > OnHand`); `LastUnitCostAfterInflow`; and `LastUnitCost(key, history)`, the ledger fold from spec §7. |
| R2 | `PostingRules.Post` covers receipts, issues, transfers and positive and negative adjustment lines. It returns the planned movements with their final `TotalCost` and the new state of every touched balance. Physical balances move quantity and value, and inflows set `LastUnitCost`. Virtual balances move quantity only (`StockValue` stays 0). It rejects a short outflow with `InsufficientStockException`, and a positive adjustment line with no unit cost while `OnHand <= 0` with `UnitCostRequiredException`. |
| R3 | `PostingRules.Reverse` applies the latest-only rule to every physical balance the original touched (`ReversalNotLatestException`). It mirrors each original movement: From and To swapped, the same `Quantity`, `TotalCost` and `LineNo`, with `ReversesMovementId` set. It restores `OnHand` and `StockValue` exactly and recomputes `LastUnitCost` from the ledger (V7: B back to 120.0000). |
| R4 | The negative-stock estimate and revaluation stay out until S10. `OutflowCost` rejects `qty > OnHand` even when `AllowNegative` is true, and applying an inflow to a physical balance with `OnHand < 0` throws `InvalidOperationException`. Both are marked for S10 to replace. |
| R5 | `Inventory.Domain` is pure: it references only `System.*` (not `System.Data`), itself and `Kernel`, and every type is `internal`. T12's `Inventory_domain_depends_only_on_the_BCL_and_Kernel` now checks real types and passes. |
| R6 | T1 unit part (spec §11): the new `tests/Inventory.UnitTests` (in `InventorySystem.slnx`) reproduces vector sets V (V1–V10), R (R1–R5) and A (A1–A7) exactly, to the fourth decimal, including the three rejections. INV-1 to INV-5 hold after every step in an in-memory ledger harness, and a rejected operation leaves no state behind. |
| R7 | Done: every `## Commands` line is green, CI `build` and `test` are green, and code-guardian has reviewed the PR (`Domain/**` is a high-stakes path). |

## Context used
- Spec `docs/Inventory System — Sprint 1 Spec.md`:
  - §4 `:170-190`. `Domain/` holds `CostingPolicy`, `PostingRules` and the invariants, with no EF, ASP.NET or SQL. `PostingEngine` in `Persistence` calls them "for every decision". `tests/Inventory.UnitTests/` is in the layout.
  - §5 `:258-276` and `:281-283`. The `StockBalances` columns and CHECKs. Virtual locations track quantity only (`StockValue` stays 0). The average is derived and never stored.
  - §6 `:296-304`, the cost table per document type. `:314`, step 8: costs and new balances come from `PostingRules` and `CostingPolicy`, and a short balance gets `422 insufficient-stock`. `:328-333`, the reversal rules: latest-only on physical balances, copy `Quantity`/`TotalCost`, swap the locations, then recompute LUC from the ledger. `:335-341`, INV-1 to INV-5.
  - §7 `:347-391` gives the `CostingPolicy` sketch and the LUC definition, and `:393-440` gives vector sets V, R and A.
  - §9 `:582-590` gives the slugs the rejections map to later: `insufficient-stock` (`itemId`, `locationId`, `available`, `requested`), `reversal-not-latest` (`itemId`, `locationId`, `blockingDocumentId`) and `validation`.
  - §11 `:633`, T1. §12 `:662`: S5b is pure and can start any time. `:671` is S5b and `:678` is S10.
- Spec audit `docs/Inventory System — Sprint 1 Spec.audit.md`:
  - `:15`: `PostingRules` stays pure and `PostingEngine` owns the SQL.
  - `:17`: LUC is defined in ledger terms, and reversals recompute it.
  - `:24`: the default adjustment cost multiplies first (30.0200, not 30.0201).
  - `:50`: S5b runs on `opus` because it is the money math.
- TASKS.md:
  - `:14`, the IMS-7 line.
  - `:28`: a test project uses `xunit.v3` only, `OutputType Exe` and MTP, and is created with its first test.
  - `:29`, `Inventory.Domain`'s dependency rule.
  - `:44`: a new slug lands in spec §9, `ProblemTypes` and `PROBLEM_MESSAGES` in the same PR, so S5b, which adds no endpoint, adds no slug.
  - `:74`: endpoints and validation live in `Features/`.
  - `:77-94`: IMS-6's S5c notes.
- Code:
  - `tests/Architecture.Tests/ModuleBoundaryTests.cs:52-62`: the Domain rule (it passes vacuously today, because no type is in `Inventory.Domain`).
  - `ModuleBoundaryTests.cs:31-47`: only `InventoryModuleExtensions` may be public.
  - `tests/Architecture.Tests/ProjectReferenceTests.cs:66-79`: `tests/**` may reference module implementations.
  - `src/Modules/Inventory/Inventory/Inventory.csproj:23-26`: the `InternalsVisibleTo` list.
  - `tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj`: the csproj template.
  - `src/Modules/Inventory/Inventory/Persistence/StockBalance.cs` and `StockMovement.cs`: the columns the Domain records mirror.
  - `Features/Problems.cs`: where S5c maps rejections.
  - `Features/Items/ItemRules.cs`: the request-validation pattern, which stays in `Features`.
  - `InventorySystem.slnx`.
  - `.github/workflows/ci.yml:44`: a bare `dotnet test` over the slnx.
  - `.editorconfig`: `insert_final_newline = false`, sorted usings in separate groups.
- Graph: none. There is no `graphify-out/`, and grep and reads cover a slice this size.

## Options
| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| A. Full pure model: `CostingPolicy` and `PostingRules.Post` (4 types) and `.Reverse`, with V/R/A complete in unit tests | 3 prompts, about 13 files | 1 PR | The reversal API is fixed before S9 exists. Mitigated by records that mirror the table columns. | S5c, S6 and S9 only wire it up, and every money decision is in one tested place | Matches spec §12's "T1 unit part (V, R, A)" |
| B. No reversals: V7, V8 and the LUC fold move to S9 | about 10 files | 1 PR | Low now, but S9 adds domain code | Splits the money rules across two PRs | Drifts from §12 |
| C. Costing functions only | about 6 files | small | S5c (the biggest slice) takes on the balance math | Balance math is tested only through the API | Drifts from §12 |

**Recommendation:** A, because the spec puts the whole of V, including the reversal steps, in S5b's unit tests, and the domain is where those decisions belong. **Chosen:** A.

## Decisions
- **Scope (user, 2026-11-09):** the full pure model (option A).
- **Step-1 validation (user, 2026-11-09):** the pure checks in §6 step 1 stay in S5c's request handlers, beside the DTOs and their JSON field names. These are: 1–100 lines, each item once, `qty > 0`, `unitCost >= 0`, at most 4 dp, at most 999,999,999.9999, and from ≠ to. The domain only guards its own preconditions with `ArgumentException`/`ArgumentOutOfRangeException`/`InvalidOperationException`. Those are programmer errors that surface as a 500, never as a user rejection.
- **Rejections are typed exceptions** under `abstract PostingRejectedException`:
  - `InsufficientStockException(ItemId, LocationId, Available, Requested)`;
  - `UnitCostRequiredException(LineNo)`;
  - `ReversalNotLatestException(ItemId, LocationId, BlockingMovementId)`.

  This follows the spec §7 sketch (`throw new InsufficientStock(...)`). Each one aborts the whole posting, which rolls back anyway. S5c, S6 and S9 map them to `insufficient-stock`, `validation` (`errors["lines[i].unitCost"]`) and `reversal-not-latest`, where the engine looks up `blockingDocumentId` from the movement id.
- **No slug in S5b:** no endpoint is added. Each slug lands with its first endpoint, per TASKS.md:44.
- **Domain owns its types.** `Inventory.Domain` declares `BalanceKey`, `Balance`, `LedgerMovement`, `PlannedMovement`, `PostingKind`, `PostingLine` and `PostingResult`, mirroring the `StockBalances`/`StockMovements` columns. It never references `Inventory.Persistence`, which keeps T12 green, and the engine maps between the two by hand (no AutoMapper).
- **`Round4` lives in `CostingPolicy`**, as in the spec §7 sketch, not in Kernel's "Money helpers". Kernel is shared code, and nothing else rounds yet.
- **The LUC fold is the definition of record.** `CostingPolicy.LastUnitCost(key, history)` works as follows:
  - It takes the latest inflow into the balance that is neither a reversal nor reversed.
  - It then computes running `OnHand`/`StockValue` over every movement on that balance up to the last movement of that inflow's document. That is the spec's "after the inflow and its revaluation", so it already works for S10.
  - It returns `OnHand > 0 ? Round4(SV/OH) : Round4(cost/qty)`, or `null` when there is no surviving inflow.

  S9 uses it, and S11's windowed SQL must match it.
- **`Reverse` computes LUC over the balance's history minus the original's movements.** The latest-only rule makes that equal to the fold over the full history after the reversal: a reversed pair is always adjacent on a balance. The harness checks INV-5 against the full fold after every step, which proves the equivalence on V.
- **Harness atomicity (Codex audit, 2026-11-09):** the in-memory harness stages missing balances in a per-operation working copy. A rejected operation, including a first touch, leaves no balance, movement or state behind, the same way the engine's step 5 insert rolls back with its transaction. `Rejected_first_touch_leaves_no_state` proves it.
- **Harness CHECK backstop matches the schema (Codex audit, 2026-11-09):** `Quantity > 0 || (Quantity == 0 && TotalCost > 0)`, `TotalCost >= 0` and `From != To` (`InventoryDbContext.cs:112-114`). S10's zero-quantity revaluations then pass it unchanged.
- **Tests:** the new `tests/Inventory.UnitTests` (spec §4) uses `xunit.v3` 4.0.2 and `Shouldly` 4.3.0 (both allowed, both already pinned in the repo) and gets `InternalsVisibleTo`. The invariants are asserted in a test-only in-memory harness. Their SQL form is S11.

**Assumptions:**
- Until S10, `AllowNegative` is always false: the Locations API ignores the field (TASKS.md IMS-5 Downstream), and it defaults to 0. So rejecting `qty > OnHand` even with the toggle on, and throwing on an inflow into a negative balance, are both unreachable, and both are safe failures.
- Each balance is touched by at most one line per document, because §6 step 1 allows each item only once (S5c validates this). `Post` throws `ArgumentException` otherwise.
- A reversal movement copies the original's `LineNo`. `(DocumentId, LineNo)` is unique within the reversal document, and S10's revaluation `1000 + n` mirrors cleanly.
- The document-level reversal checks stay in S9, because they need document rows: the original is itself a reversal, it is already reversed, or an item or location is inactive. `Reverse` throws `ArgumentException` when handed a reversal's movements, as defence in depth.
- Only physical balances are latest-checked (spec §6 `:331`). The virtual Supplier, Customer and Adjustment balances are touched by everything.
- `qty × StockValue` can't overflow C# `decimal` within step 1's bounds and `decimal(19,4)`: at most about 1e24, against a maximum of 7.9e28.
- `Post` and `Reverse` leave `LastMovementId` unchanged. The engine sets it after taking ids at step 9.
- For `Reverse`, the engine (S9) loads the history of each physical balance the original touched through `IX_Movements_Item (TenantId, ItemId, Id)`, filtered to the location.

**Non-goals:**
- The pure request checks in step 1, the RequestHash and idempotency (S5c).
- `PostingEngine`, SQL, locks, endpoints and DTOs (S5c, S6, S9).
- ProblemDetails slugs and the Angular map.
- The negative-stock estimate and revaluation, and sets N and X (S10).
- Invariants as SQL, health and rebuild (S11).
- Kernel Money/Quantity helpers.
- The document-level reversal checks (S9).
- Any UI.
- Any change to `Persistence/*`.

**Downstream (for TASKS.md when IMS-7 merges):**
- **S5c/S6/S9:**
  - Call `PostingRules.Post`/`.Reverse` at step 8, with balances built from the step 6 lock read and the step 7 `IsVirtual`/`AllowNegative`.
  - Insert `PlannedMovement`s with ids from `MovementIds` in `LineNo` order, then set each balance's `LastMovementId` to its last new movement.
  - Catch `PostingRejectedException` subtypes and map them: `InsufficientStock` → `422 insufficient-stock`; `UnitCostRequired` → `400 validation` with `errors["lines[i].unitCost"]` (S6); `ReversalNotLatest` → `409 reversal-not-latest` with `blockingDocumentId` (S9).
  - Every other exception from Domain is a bug.
- **S10:** replaces the `qty > OnHand` rejection in `OutflowCost` (when `AllowNegative`) with the estimate branch. Replaces the negative-inflow `InvalidOperationException` in `Post` with `Revaluation`, which emits a movement at `LineNo + 1000`. Adds sets N and X to `CostingVectorTests`. The LUC fold already groups by document.
- **S11:** INV-5's windowed SQL must return what `CostingPolicy.LastUnitCost` returns. T9 can compare the two.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Domain/Balance.cs` | C | `BalanceKey` and `Balance` (mirrors `StockBalances`) | R1–R3, R5 | 01 |
| `src/Modules/Inventory/Inventory/Domain/LedgerMovement.cs` | C | `LedgerMovement` (a posted row) and `PlannedMovement` (to insert) | R1–R3, R5 | 01 |
| `src/Modules/Inventory/Inventory/Domain/PostingRejections.cs` | C | `PostingRejectedException` and its three subtypes | R1–R3 | 01 |
| `src/Modules/Inventory/Inventory/Domain/CostingPolicy.cs` | C | The costing functions and the LUC fold | R1, R4 | 01 |
| `src/Modules/Inventory/Inventory/Inventory.csproj` | M | `InternalsVisibleTo Inventory.UnitTests` | R6 | 01 |
| `InventorySystem.slnx` | M | Adds the unit test project, so `dotnet test` and CI run it | R6, R7 | 01 |
| `tests/Inventory.UnitTests/Inventory.UnitTests.csproj` | C | The new test project (spec §4) | R6 | 01 |
| `tests/Inventory.UnitTests/CostingPolicyTests.cs` | C | Function-level tests, including the R4 midpoint and the A3 multiply-first case | R1, R4 | 01 |
| `src/Modules/Inventory/Inventory/Domain/PostingRules.cs` | C | `PostingKind`, `PostingLine`, `PostingResult`, `Post` and `Reverse` | R2–R4 | 02 |
| `tests/Inventory.UnitTests/PostingRulesTests.cs` | C | Branch and guard tests for `Post` and `Reverse` | R2–R4 | 02 |
| `tests/Inventory.UnitTests/LedgerHarness.cs` | C | An in-memory ledger: id assignment, balance creation and INV-1 to INV-5 checks | R6 | 03 |
| `tests/Inventory.UnitTests/CostingVectorTests.cs` | C | T1 unit part: sets V, R and A, step by step | R6 | 03 |

## Blast radius
**Risk:** medium. The unit touches the `Domain/**` high-stakes glob and the money math, but it only adds code. Nothing calls it yet, and no existing behaviour changes.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `ModuleBoundaryTests.Inventory_domain_depends_only_on_the_BCL_and_Kernel` | Direct | NetArchTest over `Inventory.Domain` | Not if the Domain stays BCL-only. It turns from vacuous to real, so a stray `using Inventory.Persistence` or `System.Data` fails it | Prompts 01 and 02 forbid both; Done-when runs `Architecture.Tests` |
| `ModuleBoundaryTests.Module_implementations_expose_only_their_registration_class` | Direct | Public types in the Inventory assembly | Fails if any Domain type is `public` | Every type is `internal`; Done-when runs it |
| `ProjectReferenceTests` (3 rules) | Non-graph | Scans every `tests/**/*.csproj` | No: a test project may reference `Inventory.csproj`, and it references no Contracts or Kernel rule target | — |
| `InventorySystem.slnx` → `dotnet build`, `dotnet format`, `dotnet test`, CI `build` and `test` | Non-graph | The solution file | The new project joins every command. A test that fails or doesn't format fails CI | Prompt 01's Done-when runs the full `build -warnaserror` and `format` |
| `Inventory.csproj` `InternalsVisibleTo` | Direct | MSBuild | No: it only adds a friend assembly | — |
| `Persistence/StockBalance.cs`, `StockMovement.cs` | Non-graph | The Domain records mirror their columns | No: not referenced. Drift between the two is S5c's mapping concern | Downstream note |
| Spec §7 vector tables | Hidden coupling | CLAUDE.md: "the vector tables in section 7 match the CostingPolicy unit tests" | This unit creates the coupling | The prompt 03 tables are copied verbatim from `:395-440`; a later §7 edit must update `CostingVectorTests` |
| Future S5c, S6, S9, S10, S11 | Transitive (future) | The API shape of `PostingRules`/`CostingPolicy` | Could churn if S5c needs other inputs | Records mirror the table columns. Downstream notes fix the contract |
| `web/` | — | — | Untouched | The web command still runs at the end |

**Known risks in the touched area:**
- C#'s default `Math.Round` is banker's rounding, and R4 fails with it (spec `:420`).
- The default adjustment cost must multiply before dividing; A3 gives 30.0201 otherwise (spec audit `:24`).
- LUC is history-dependent and must come from the ledger (spec audit `:17`); V7 proves it.
- A Testcontainers start-up crash can hit a plain `dotnet test` (TASKS.md IMS-6 Downstream). If it does, use `--max-parallel-test-modules 1`.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-7 | S5b Costing rules | Added | 01, 02, 03 | IMS-2 (merged) | yes (`src/Modules/Inventory/Inventory/Domain/**`) | R1: `CostingPolicy` passes its function tests, including `Round4(10.00665m) == 10.0067m` and the default cost of 30.0200. R2: `Post` covers all four types with exact costs and balances, plus both rejections. R3: `Reverse` restores exactly, recomputes LUC and rejects a non-latest original. R4: there is no estimate or revaluation path, and the S10 placeholders are tested. R5: T12 is green with real Domain types. R6: V1–V10, R1–R5 and A1–A7 match spec §7 exactly, with INV-1 to INV-5 after every step. R7: Commands and CI are green, and code-guardian has approved. |

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Domain types, CostingPolicy and the unit test project | IMS-7 | opus | high | — | A | 8 |
| 02 | PostingRules: Post and Reverse | IMS-7 | opus | high | 01 | B | 2 |
| 03 | Ledger harness and vector sets V, R, A (T1 unit part) | IMS-7 | sonnet | high | 02 | C | 2 |

- 01 `opus`: it is the high-stakes `Domain/**` glob and the money math. Rounding and the LUC fold define what S9 and S11 must reproduce.
- 02 `opus`: it is the high-stakes `Domain/**` glob, and the reversal latest-only rule and exact restore are the ledger's correctness core.

## End-to-end verification
```bash
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false
# R1, R2, R3, R4, R6
dotnet test --project tests/Inventory.UnitTests/Inventory.UnitTests.csproj
# R5 (T12: BCL-only Domain, and no public type besides InventoryModuleExtensions)
dotnet test --project tests/Architecture.Tests/Architecture.Tests.csproj
grep -rnE '^using (Inventory\.(Persistence|Features)|Microsoft\.|System\.Data)' src/Modules/Inventory/Inventory/Domain   # expect no output
# R4: no estimate/revaluation code path yet
grep -n 'S10' src/Modules/Inventory/Inventory/Domain/*.cs
```
- R6 is checked by eye: `CostingVectorTests` has every step V1–V10, R1–R5 and A1–A7, with the costs and quantity/value pairs of spec `:395-440`.
- R7: the CI `build` and `test` checks are green on the PR, and the code-guardian review is recorded.

## Rollback
Revert the PR. The unit is additive: no schema, no data, no endpoint, and nothing calls it until S5c.

## Log
- 2026-11-09: planned (option A; step-1 validation stays in S5c).
- 2026-11-09: audited, 2 fixes applied (Codex PASS-WITH-CHANGES: harness atomicity, harness CHECK matches schema).
- 2026-11-09: applied Codex audit (2 applied, 0 disputed).
