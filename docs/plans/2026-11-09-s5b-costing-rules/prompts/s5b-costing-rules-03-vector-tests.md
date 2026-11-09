---
type: prompt
plan: "[[2026-11-09-s5b-costing-rules]]"
id: 03
unit: IMS-7
status: todo
model: sonnet
effort: high
depends_on: [02]
parallel_group: C
files:
  create:
    - tests/Inventory.UnitTests/LedgerHarness.cs
    - tests/Inventory.UnitTests/CostingVectorTests.cs
  modify: []
  delete: []
---
# 03 · Ledger harness and vector sets V, R, A (T1 unit part)

## Task
Write T1's unit part (spec §11): run vector sets V, R and A from spec §7 step by step through `PostingRules` and `CostingPolicy`. Each step is checked against the spec tables to the fourth decimal, and the ledger invariants INV-1 to INV-5 are asserted after every step. You write test code only.

## Context
- **Repo and branch:** the repo root is `/home/apa/Code/portfolio/inventory-management-system`, and you are on branch `IMS-7`. Don't switch it.
- **Already built:** prompts 01 and 02. Read these first and use them unchanged:
  - `src/Modules/Inventory/Inventory/Domain/Balance.cs`;
  - `Domain/LedgerMovement.cs`;
  - `Domain/PostingRejections.cs`;
  - `Domain/CostingPolicy.cs`;
  - `Domain/PostingRules.cs`.

  Every type is `internal`, and the test assembly has `InternalsVisibleTo`.
- **Test project:** `tests/Inventory.UnitTests`, xUnit v3 (`Xunit` is a global using) and Shouldly. Copy the style of `tests/Inventory.UnitTests/PostingRulesTests.cs`: `namespace Inventory.UnitTests;` and underscore method names.
- **Hidden coupling (CLAUDE.md):** the vector tables in spec §7 must match these tests. Copy the expected numbers exactly as given below, and never adjust an expected value to make a test pass. If a vector fails, the domain code is wrong. **Stop and report** the step, the expected value and the actual value, and don't edit `Domain/`.
- **Conventions:** `decimal` literals (`m`) only. Shouldly's `ShouldBe` on decimals is exact, which is what we want.

### `LedgerHarness.cs`: `internal sealed class LedgerHarness`, an in-memory stand-in for the engine
- **State:**
  - Named locations. Physical ones are created on demand by name ("A", "B", "C", "D"). The virtual "SUP", "CUS" and "ADJ" exist from the start. All have `AllowNegative = false`.
  - Named items, created on demand.
  - A `Dictionary<BalanceKey, Balance>`.
  - A `List<LedgerMovement>`.
  - A `long` id counter starting at 1.
- **Step 5 stand-in:** per operation, build a **local working dictionary**: a copy of the harness balances, plus every missing touched balance as `new Balance(item, loc, isVirtual, false, 0m, 0m, null, null)`. Pass that copy to the rules. Never add a balance to the harness state before the rules return. The engine's step 5 insert rolls back with its transaction, and the harness mirrors that.
- **Operations.** Each returns a `Posted` record holding the `DocumentId` and that document's `LedgerMovement`s.
  - `Receive(string item, string loc, decimal qty, decimal unitCost)` posts `PostingKind.Receipt`, SUP → loc.
  - `Issue(string item, string loc, decimal qty)` posts `Issue`, loc → CUS.
  - `Transfer(string item, string from, string to, decimal qty)` posts `Transfer`.
  - `Adjust(string item, string loc, decimal quantityDelta, decimal? unitCost = null)` posts `Adjustment`:
    - a positive delta goes ADJ → loc;
    - a negative delta goes loc → ADJ with `Quantity = -delta`.
  - `Reverse(Guid documentId)` takes the document's movements as `original`. For `histories`, it takes every movement touching each physical key the original touched. It calls `PostingRules.Reverse`.
  - Each operation posts one line, `LineNo` 1.
- **Commit, only when the rules return:**
  - Assign ids in movement order and a new `DocumentId`, and append the `LedgerMovement`s.
  - Add or replace the touched balances in the harness state with the result's balances. This is the only place a new balance enters the state.
  - Set each touched balance's `LastMovementId` to the max new id that touches it.
- **On exception:** commit nothing: no movement, no balance change and no new balance. The exception propagates.
- **`int BalanceCount`** and **`int MovementCount`**, for the rejection assertions.
- **`(decimal OnHand, decimal Value, decimal? Luc) State(string item, string loc)`.**
- **`void AssertInvariants(string step)`.** Every assertion message includes `step`.
  - **INV-1:**
    - For every balance, `OnHand` equals Σ `Quantity` in minus Σ `Quantity` out, over the movements of that item touching that location.
    - For physical balances, `StockValue` equals Σ `TotalCost` in minus Σ out.
    - For virtual balances, `StockValue == 0` and `LastUnitCost is null`.
  - **INV-2:** for every item, Σ `OnHand` over all its balances is `0`.
  - **INV-3:** every physical balance has `OnHand >= 0` and `StockValue >= 0`, and `OnHand == 0` implies `StockValue == 0`. All toggles are off.
  - **INV-4:** every movement with `ReversesMovementId` has the same item, `Quantity` and `TotalCost` as its original, with From and To swapped.
  - **INV-5:** every physical balance's `LastUnitCost` equals `CostingPolicy.LastUnitCost(key, movements touching key)` over the **full** ledger.
  - **DB CHECK backstop:** every movement satisfies the schema's CHECKs exactly (`Persistence/InventoryDbContext.cs:112-114`): `TotalCost >= 0`, `Quantity > 0 || (Quantity == 0 && TotalCost > 0)`, and `From != To`. Zero-quantity revaluations are valid; they arrive in S10.

### `CostingVectorTests.cs`: `public sealed class CostingVectorTests`
- Write one `[Fact]` per set: `Vector_set_V_two_locations`, `Vector_set_R_rounding` and `Vector_set_A_adjustments`. The steps depend on each other, so each set is a single sequential walk.
- After every step, assert:
  - the movement cost: the posted movement's `TotalCost`, or the reversal movement's for V7;
  - each listed location's qty / value;
  - the LUC where the plan lists one;
  - then `harness.AssertInvariants("V3")`, and so on.
- For rejected steps:
  - assert the exception type and its fields;
  - assert that every location's state is unchanged;
  - assert that `MovementCount` and `BalanceCount` are unchanged.
- Add a fourth `[Fact]`, `Rejected_first_touch_leaves_no_state`:
  - On a fresh harness, receive 1 @ 10.00 of item "X" into A.
  - Record `BalanceCount` and `MovementCount`.
  - `Issue("Y", "E", 1m)`, for an item and a location never touched before, throws `InsufficientStockException` with `Available == 0m` and `Requested == 1m`.
  - `Transfer("X", "A", "F", 2m)` throws `InsufficientStockException`, with F never touched before.
  - After both, `BalanceCount` and `MovementCount` equal the recorded values, and `AssertInvariants("first-touch")` passes.
- Add a class `<summary>`: "T1 unit part (spec §11): vector sets V, R and A of spec §7 :395-440. Must match the spec tables (CLAUDE.md Hidden coupling)."

**Set V: one item, locations A and B**

| Step | Action | Movement cost | A after: qty / value | B after: qty / value |
|---|---|---|---|---|
| V1 | Receive 10 @ 100.00 into A | 1000.0000 | 10 / 1000.0000 | — |
| V2 | Receive 10 @ 120.00 into A | 1200.0000 | 20 / 2200.0000 | — |
| V3 | Issue 15 from A | 1650.0000 | 5 / 550.0000 | — |
| V4 | Receive 5 @ 130.00 into A | 650.0000 | 10 / 1200.0000 | — |
| V5 | Transfer 4 from A to B | 480.0000 | 6 / 720.0000 | 4 / 480.0000 |
| V6 | Receive 6 @ 125.00 into B | 750.0000 | 6 / 720.0000 | 10 / 1230.0000 |
| V7 | Reverse V6 | 750.0000 | 6 / 720.0000 | 4 / 480.0000 |
| V8 | Reverse V4 | rejected: `ReversalNotLatestException` | unchanged | unchanged |
| V9 | Issue 7 from A | rejected: `InsufficientStockException` | unchanged | unchanged |
| V10 | Issue 6 from A | 720.0000 | 0 / 0.0000 | 4 / 480.0000 |

LUC checks:
- A: 100 after V1, 110 after V2, 110 after V3, 120 after V4 and V5.
- B: 120 after V5, 123 after V6, and **120 after V7** (restored from the ledger: T8's claim).

Rejection details:
- V8: `ItemId` is the item, `LocationId` is A, and `BlockingMovementId` is the id of V5's movement (the transfer A → B).
- V9: `LocationId` is A, `Available == 6m` and `Requested == 7m`.

**Set R: one item, location C**

| Step | Action | Movement cost | C after: qty / value |
|---|---|---|---|
| R1 | Receive 1 @ 10.00 | 10.0000 | 1 / 10.0000 |
| R2 | Receive 2 @ 10.01 | 20.0200 | 3 / 30.0200 |
| R3 | Issue 1 | 10.0067 | 2 / 20.0133 |
| R4 | Issue 1 | 10.0067 | 1 / 10.0066 |
| R5 | Issue 1 | 10.0066 | 0 / 0.0000 |

Also assert that the total issued (R3 + R4 + R5) is `30.0200m`, which equals the total received (R1 + R2). R4 is the midpoint case: banker's rounding would give 10.0066.

**Set A: one item, location D (toggle off)**

| Step | Action | Movement cost | D after: qty / value |
|---|---|---|---|
| A1 | Receive 1 @ 10.00 | 10.0000 | 1 / 10.0000 |
| A2 | Receive 2 @ 10.01 | 20.0200 | 3 / 30.0200 |
| A3 | Adjust +3, no unit cost | 30.0200 | 6 / 60.0400 |
| A4 | Adjust −1 | 10.0067 | 5 / 50.0333 |
| A5 | Adjust +2 @ 12.50 | 25.0000 | 7 / 75.0333 |
| A6 | Adjust −7 | 75.0333 | 0 / 0.0000 |
| A7 | Adjust +1, no unit cost | rejected: `UnitCostRequiredException` (`LineNo == 1`) | unchanged |

A3 must be 30.0200, not 30.0201 (rounding the average first is wrong).

## Do not
- Touch files outside the `files` list, and in particular `src/**`. If a vector fails, stop and report it (see Context).
- Change any expected number from the tables above.
- Add packages, or test sets N and X (S10).
- Create or switch branches, commit, or push.

## Done when
```bash
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests/Inventory.UnitTests.csproj
dotnet test --project tests/Architecture.Tests/Architecture.Tests.csproj
```
- The three vector facts and `Rejected_first_touch_leaves_no_state` pass. Every step of V1–V10, R1–R5 and A1–A7 is asserted, and INV-1 to INV-5 are checked after each step.
