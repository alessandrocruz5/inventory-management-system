---
type: prompt
plan: "[[2026-11-15-s10-negative-stock]]"
id: 01
unit: IMS-14
status: todo
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create: []
  modify:
    - src/Modules/Inventory/Inventory/Domain/CostingPolicy.cs
    - tests/Inventory.UnitTests/CostingPolicyTests.cs
    - tests/Inventory.UnitTests/PostingRulesTests.cs
  delete: []
---
# 01 · Domain: estimate branch and `Revaluation`

## Task
In `CostingPolicy`, replace the S10 placeholder in `OutflowCost` with spec §7's estimate branch, and add the pure function `Revaluation`. Update the two unit tests that expect the placeholder, and add tests for both. Don't touch `PostingRules` yet: prompt 02 wires `Revaluation` into `Post`.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-14`. Stop if `git branch --show-current` says otherwise. .NET 10, `Nullable` on, `TreatWarningsAsErrors`. Domain types are `internal`; `Inventory.Domain` uses only `System.*` and Kernel.
- Money rules (CLAUDE.md): `decimal` only, never `double`/`float`; every rounding goes through `CostingPolicy.Round4` (4 dp, `MidpointRounding.AwayFromZero`).
- Read `src/Modules/Inventory/Inventory/Domain/CostingPolicy.cs` (111 lines) and `Domain/Balance.cs` first. Today `OutflowCost` (`:32-43`) throws `InsufficientStockException` whenever `quantity > balance.OnHand`, with the comment `// S10: when AllowNegative, estimate instead (spec §7)` at `:36`.

### 1. `OutflowCost`: spec §7 `:358-368`, exactly
```csharp
// Issue, transfer-out, negative adjustment line. b = OnHand, StockValue, LastUnitCost, AllowNegative
if (qty > b.OnHand && !b.AllowNegative) throw new InsufficientStock(...);   // same exception and arguments as today
if (qty == b.OnHand) return b.StockValue;           // empties the balance: take the remainder
if (qty < b.OnHand)  return Round4(qty * b.StockValue / b.OnHand);
// Toggle on and qty > OnHand: drain what is there, estimate the rest
return b.OnHand > 0
    ? b.StockValue + Round4((qty - b.OnHand) * (b.StockValue / b.OnHand))
    : Round4(qty * (b.LastUnitCost ?? 0m));
```
- Keep the multiply-before-round order shown. `(b.StockValue / b.OnHand)` is the unrounded average.
- Remove the `// S10` comment. Update the XML doc to say that with `AllowNegative`, an outflow past zero is costed at an estimate: the current average while stock is positive, then `LastUnitCost`, or ₱0 if the item was never received there (spec §7 `:438`).

### 2. `Revaluation`: spec §7 `:370-378`, exactly
```csharp
/// <summary>
/// The estimate variance an inflow settles (spec §7): when an inflow that isn't a reversal lifts a negative physical
/// balance to zero or above, the stock on hand is revalued at the inflow's unit cost. &gt; 0 posts a revaluation
/// physical → Adjustment, &lt; 0 Adjustment → physical, 0 posts nothing. <paramref name="cost"/> is the inflow's TotalCost.
/// </summary>
public static decimal Revaluation(decimal quantity, decimal cost, Balance balance)
{
    var onHand = balance.OnHand + quantity;
    if (balance.OnHand >= 0 || onHand < 0)
    {
        return 0m;
    }

    return balance.StockValue + cost - Round4(onHand * cost / quantity);
}
```
- Put it after `OutflowCost`. Also update `LastUnitCostAfterInflow`'s doc so it no longer says "(S10)" if it does; leave its code alone.

### 3. Tests
`tests/Inventory.UnitTests/CostingPolicyTests.cs` has `Item`, `Loc` and `BalanceOf(decimal onHand, decimal value, decimal? luc = null, bool allowNegative = false)` (`:20-21`).
- `OutflowCost_above_on_hand_throws_insufficient_stock(bool allowNegative)` (`:107-118`): keep only the `false` case. Turn it into a `[Fact]`, or keep the `[Theory]` with only `[InlineData(false)]`; prefer the `[Fact]`, with the name unchanged.
- Add (`[Theory]` with `TheoryData` where the existing file does):
  - `OutflowCost_past_zero_with_the_toggle_on_drains_then_estimates_at_the_average`: `(6, OnHand 4, Value 200, allowNegative)` → `300.0000` (N2: 200 remainder + 2 × 50); `(4, 3, 30.02)` → `40.0267` (30.02 + Round4(1 × 30.02/3 = 10.006666…) = 30.02 + 10.0067).
  - `OutflowCost_at_or_below_zero_with_the_toggle_on_uses_the_last_unit_cost`: `(1, −2, −100, luc 50)` → `50.0000` (N3); `(2, 0, 0, luc 56)` → `112.0000` (N6); `(0.5, 0, 0, luc 10.0065)` → `5.0033`. 0.5 × 10.0065 = 5.00325, and the midpoint rounds away from zero; banker's rounding would give 5.0032. Put that in a comment.
  - `OutflowCost_of_a_never_received_item_is_zero`: `(2, 0, 0, luc null, allowNegative)` → `0m`.
  - `OutflowCost_within_on_hand_ignores_the_toggle`: `(15, 20, 2200, allowNegative: true)` → `1650` and `(6, 6, 720, allowNegative: true)` → `720`.
  - `Revaluation_settles_the_estimate_variance` (`quantity, cost, onHand, value, expected`): N4 `(10, 560, −3, −150)` → `18.0000`; N7 `(2, 120, −2, −112)` → `8.0000`; X3 `(3, 30, −2, −100)` → `−80.0000`; zero variance `(2, 100, −2, −100)` → `0m`; never received `(3, 30, −2, 0)` → `20.0000` (1 left, worth 10).
  - `Revaluation_is_zero_unless_the_inflow_lifts_a_negative_balance`: from non-negative `(5, 250, 0, 0)` and `(5, 250, 3, 150)` → `0m`; still negative after `(2, 112, −5, −250)` → `0m`.
- `tests/Inventory.UnitTests/PostingRulesTests.cs`: `Issue_above_on_hand_throws_insufficient_stock(bool allowNegative)` (`:109-117`) now fails for `true`. Keep only the `false` case, as above. Add `Issue_past_zero_with_the_toggle_on_estimates` using the file's helpers (`Physical(A, onHand, value, luc, lastMovementId, allowNegative)`, `Virtual(Cus)`, `BalancesOf`, `Line(A, Cus, qty)`, `Of(result, A)`): `Physical(A, 4m, 200m, luc: 50m, allowNegative: true)`, issue 6 → one movement costing `300.0000`; `Of(result, A)` is `OnHand −2`, `StockValue −100.0000`, `LastUnitCost 50.0000` (an outflow never changes LUC). Don't change any `PostingRules.Post` call signature here.

## Do not
- Touch `PostingRules.cs` or any file outside `files`. If you must, stop and report why.
- Change `InflowCost`, `DefaultAdjustmentCost`, `LastUnitCostAfterInflow`'s code or `LastUnitCost`.
- Use `Math.Round`'s default rounding, `double` or `float`.
- Add dependencies, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests
! grep -n 'S10: when AllowNegative' src/Modules/Inventory/Inventory/Domain/CostingPolicy.cs
```
- Every expected value above passes to the fourth decimal; all other unit tests are unchanged and green.
