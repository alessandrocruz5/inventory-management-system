---
type: prompt
plan: "[[2026-11-15-s10-negative-stock]]"
id: 03
unit: IMS-14
status: done
model: sonnet
effort: high
depends_on: [02]
parallel_group: C
files:
  create: []
  modify:
    - tests/Inventory.UnitTests/LedgerHarness.cs
    - tests/Inventory.UnitTests/CostingVectorTests.cs
  delete: []
---
# 03 · Vector sets N and X in the harness

## Task
Give the in-memory `LedgerHarness` a per-location negative-stock toggle and a toggle-aware INV-3. Then add spec §7's vector sets N and X to `CostingVectorTests`, plus two reversal facts. Test code only; production code is done (prompts 01–02).

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-14`. Stop if `git branch --show-current` says otherwise. xUnit v3 + Shouldly; `decimal` only.
- Hidden coupling (CLAUDE.md): the vector tables in spec §7 must match these tests exactly. Copy the numbers below; they come from spec `:442-467`.
- Read `tests/Inventory.UnitTests/LedgerHarness.cs` and `CostingVectorTests.cs` in full first. The harness mirrors the engine: `WorkingCopy` is step 5, and `Commit` sets each returned balance's `LastMovementId`. After prompt 02, `Post` puts `(item, ADJ)` in the working copy and passes `LocationId("ADJ")` to `PostingRules.Post`. `AssertInvariants` (`:85-144`) asserts "INV-3 (all toggles are off)", including `b.AllowNegative.ShouldBeFalse(...)`. `CostingVectorTests` uses `const string Item`, `Expect(h, loc, onHand, value)` and `h.AssertInvariants(step)` after every step (see `Vector_set_A_adjustments` `:142-187`).

### 1. Harness toggle
- Add `private readonly HashSet<Guid> _allowNegative = [];` and `public void AllowNegative(string loc, bool allow = true)`, which adds or removes `LocationId(loc)` (physical only: throw `InvalidOperationException` for `SUP`/`CUS`/`ADJ`).
- The engine takes `AllowNegative` from the step-7 location read, not from the row (spec §6). So in `WorkingCopy`, set `AllowNegative = _allowNegative.Contains(key.LocationId)` on **every** touched physical balance, existing ones included, and on new ones. Virtual balances stay `false`. Do the same for the keys `Reverse` builds; it already goes through `WorkingCopy`.
- `AssertInvariants` INV-3 becomes toggle-aware, mirroring `tests/Inventory.IntegrationTests/LedgerInvariants.cs:57-70`: `OnHand >= 0` ⇒ `StockValue >= 0`; `OnHand == 0` ⇒ `StockValue == 0`; and when the balance's `AllowNegative` is false, `OnHand >= 0`. Delete the "all toggles are off" assertion. INV-1, INV-2, INV-4, INV-5 and the CHECK backstop stay as they are; the backstop already accepts `Quantity == 0 && TotalCost > 0`.
- Add `public LedgerMovement? Revaluation(Posted posted)`: the posted movement with `LineNo > 1000`, or null. Tests use it to assert the revaluation.

### 2. `CostingVectorTests`: new facts (assert each step's movement cost, the revaluation if any, `Expect(...)`, `State(...).Luc`, and `h.AssertInvariants(step)`)
`Vector_set_N_negative_stock` at location `K`, with `h.AllowNegative("K")` before N1:

| Step | Action | Movement cost | Revaluation (`LineNo 1001`) | K after | LUC |
|---|---|---|---|---|---|
| N1 | `Receive(Item, "K", 4, 50.00)` | 200.0000 | — | 4 / 200.0000 | 50.0000 |
| N2 | `Issue(Item, "K", 6)` | 300.0000 | — | −2 / −100.0000 | 50.0000 |
| N3 | `Issue(Item, "K", 1)` | 50.0000 | — | −3 / −150.0000 | 50.0000 |
| N4 | `Receive(Item, "K", 10, 56.00)` | 560.0000 | K → ADJ, qty 0, 18.0000 | 7 / 392.0000 | 56.0000 |
| N5 | `Issue(Item, "K", 7)` | 392.0000 | — | 0 / 0.0000 | 56.0000 |
| N6 | `Issue(Item, "K", 2)` | 112.0000 | — | −2 / −112.0000 | 56.0000 |
| N7 | `Receive(Item, "K", 2, 60.00)` | 120.0000 | K → ADJ, qty 0, 8.0000 | 0 / 0.0000 | 60.0000 |
| N8 | `Receive(Item, "K", 5, 60.00)` | 300.0000 | — | 5 / 300.0000 | 60.0000 |
| N9 | `Issue(Item, "K", 5)` | 300.0000 | — | 0 / 0.0000 | 60.0000 |

Then value conservation (spec `:456`): receipts 1,180.0000 = issues 1,154.0000 + revaluations 26.0000. Sum the costs you captured, don't hard-code the totals only. `State(Item, "ADJ").OnHand` is 0.

`Vector_set_X_estimate_above_the_next_price` at `K2` (toggle on):

| Step | Action | Movement cost | Revaluation | K2 after | LUC |
|---|---|---|---|---|---|
| X1 | Receive 4 @ 50.00 | 200.0000 | — | 4 / 200.0000 | 50.0000 |
| X2 | Issue 6 | 300.0000 | — | −2 / −100.0000 | 50.0000 |
| X3 | Receive 3 @ 10.00 | 30.0000 | ADJ → K2, qty 0, 80.0000 | 1 / 10.0000 | 10.0000 |
| X4 | Issue 1 | 10.0000 | — | 0 / 0.0000 | 10.0000 |

Add a comment from spec `:467`: without the revaluation, X3 would leave 1 / −70.0000 and X4 would post a cost of −70, which the `TotalCost >= 0` check refuses.

`Reversal_restores_a_negative_balance_exactly`: K toggle on; N1, N2, N3 (K −3 / −150, LUC 50); N4 → K 7 / 392, revaluation 18. `h.Reverse(n4.DocumentId)` mirrors both movements (`(1, K→SUP, 10, 560)` then `(1001, ADJ→K, 0, 18)`, each with `ReversesMovementId` set), and K is back to −3 / −150.0000, LUC `50.0000`, with `State(Item, "ADJ").OnHand` 0. Assert invariants.

`Reversal_after_the_toggle_is_turned_off_is_rejected`: the same up to N4 (K 7 / 392); `h.AllowNegative("K", false)`; `Should.Throw<InsufficientStockException>(() => h.Reverse(n4.DocumentId))` with `Available` 7, `Requested` 10; `MovementCount`, `BalanceCount` and K's state unchanged; invariants hold.

`Issue_past_zero_with_the_toggle_off_is_still_rejected`: K without the toggle; receive 4 @ 50, issue 6 → `InsufficientStockException` (`Available` 4, `Requested` 6); state unchanged.

## Do not
- Touch files outside `files`, or production code. If a vector doesn't reproduce, stop and report the step, the expected and the actual values; never adjust an expected value.
- Remove or weaken the existing V, R, A facts or any other invariant.
- Add dependencies, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests
```
- Sets N and X reproduce to the fourth decimal, and the existing V, R and A facts still pass.
