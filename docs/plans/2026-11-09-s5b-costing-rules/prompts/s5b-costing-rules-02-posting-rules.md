---
type: prompt
plan: "[[2026-11-09-s5b-costing-rules]]"
id: 02
unit: IMS-7
status: done
model: opus
effort: high
depends_on: [01]
parallel_group: B
files:
  create:
    - src/Modules/Inventory/Inventory/Domain/PostingRules.cs
    - tests/Inventory.UnitTests/PostingRulesTests.cs
  modify: []
  delete: []
---
# 02 · PostingRules: Post and Reverse

## Task
Add `PostingRules`, the pure step 8 of the posting engine (spec §6). It turns a document's lines, or an original document's movements, plus the current balances, into planned movements and new balances. Cover it with `PostingRulesTests`.

## Context
- **Repo and branch:** the repo root is `/home/apa/Code/portfolio/inventory-management-system`, and you are on branch `IMS-7`. Don't switch it.
- **Prompt 01 is done.** Read these before writing, and use them as they are, without changing them:
  - `src/Modules/Inventory/Inventory/Domain/Balance.cs` (`BalanceKey`, `Balance`);
  - `Domain/LedgerMovement.cs` (`LedgerMovement`, `PlannedMovement`);
  - `Domain/PostingRejections.cs` (`PostingRejectedException`, `InsufficientStockException`, `UnitCostRequiredException`, `ReversalNotLatestException`);
  - `Domain/CostingPolicy.cs` (`Round4`, `InflowCost`, `DefaultAdjustmentCost`, `OutflowCost`, `LastUnitCostAfterInflow`, `LastUnitCost(key, history)`).
- **Domain purity (T12):** namespace `Inventory.Domain`, depending only on `System.*` (not `System.Data`) and `Inventory.Domain`. Every type is `internal`. No `Inventory.Persistence`, no `Microsoft.*`.
- **Conventions:** `decimal` only, and all rounding goes through `CostingPolicy.Round4`. `TreatWarningsAsErrors` is on, and `.editorconfig` has `insert_final_newline = false`. Run `dotnet format`.
- **Error classes:**
  - Business rejections throw the `PostingRejectedException` subtypes from prompt 01.
  - Broken preconditions are programmer errors. They throw `ArgumentException`, `ArgumentOutOfRangeException` or `InvalidOperationException`, because S5c's request validation (spec §6 step 1) and the engine's step 5 guarantee them.

### Shapes, all in `Domain/PostingRules.cs`
```csharp
internal enum PostingKind { Receipt, Issue, Transfer, Adjustment }

/// <summary>A request line, which the handler resolves to its locations (spec §6 table); Quantity is always > 0.</summary>
internal sealed record PostingLine(int LineNo, Guid ItemId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, decimal? UnitCost);

/// <summary>Movements in insert order and the new state of every touched balance. LastMovementId is left for the engine.</summary>
internal sealed record PostingResult(IReadOnlyList<PlannedMovement> Movements, IReadOnlyList<Balance> Balances);

internal static class PostingRules
{
    public static PostingResult Post(PostingKind kind, IReadOnlyList<PostingLine> lines, IReadOnlyDictionary<BalanceKey, Balance> balances);

    public static PostingResult Reverse(
        IReadOnlyList<LedgerMovement> original,
        IReadOnlyDictionary<BalanceKey, Balance> balances,
        IReadOnlyDictionary<BalanceKey, IReadOnlyList<LedgerMovement>> histories);
}
```

### `Post` rules
- **Preconditions:**
  - `lines` is non-empty and `LineNo`s are distinct (`ArgumentException`).
  - Every `Quantity > 0` and `UnitCost is null or >= 0` (`ArgumentOutOfRangeException`).
  - `FromLocationId != ToLocationId` (`ArgumentException`).
  - Both `(ItemId, From)` and `(ItemId, To)` keys are in `balances` (`InvalidOperationException`: "step 5 creates every touched balance").
  - No `BalanceKey` is touched by two lines (`ArgumentException`: each item appears once per document).
- **Direction per kind**, judged by `Balance.IsVirtual` of the from and to balances. A mismatch throws `ArgumentException`:
  - `Receipt`: virtual → physical, and `UnitCost` is required (non-null).
  - `Issue`: physical → virtual, and `UnitCost` is null.
  - `Transfer`: physical → physical, and `UnitCost` is null.
  - `Adjustment`, positive: virtual → physical, and `UnitCost` is optional.
  - `Adjustment`, negative: physical → virtual, and `UnitCost` is null.
  - Virtual → virtual is never allowed.
- **Process the lines in ascending `LineNo`** against a working copy of the touched balances. Use records with `with`, and never mutate the input dictionary. A balance's state after one line is what the next line sees. Each balance is touched once anyway.
- **Cost of a line:**
  - When the from balance is physical, `cost = CostingPolicy.OutflowCost(qty, from)`. It throws `InsufficientStockException` when short.
  - Otherwise (virtual → physical, an inflow):
    - With a `UnitCost`, `cost = CostingPolicy.InflowCost(qty, unitCost)`.
    - Without one (only an `Adjustment` reaches here), `cost = to.OnHand > 0 ? CostingPolicy.DefaultAdjustmentCost(qty, to) : throw new UnitCostRequiredException(line.LineNo)`.
- **Apply the line:**
  - The from balance gets `OnHand -= qty`. If it is physical, also `StockValue -= cost`.
  - Then the to balance:
    - If it is physical and `to.OnHand < 0`, throw `InvalidOperationException`, with the comment "S10: settle with CostingPolicy.Revaluation (spec §7)".
    - Otherwise `OnHand += qty`. If it is physical, also `StockValue += cost` and `LastUnitCost = CostingPolicy.LastUnitCostAfterInflow(newOnHand, newValue, qty, cost)`.
  - Virtual balances never change `StockValue` (it stays 0) or `LastUnitCost`. `LastMovementId` is never changed.
- **The movement** is `new PlannedMovement(line.LineNo, line.ItemId, line.FromLocationId, line.ToLocationId, line.Quantity, cost, null)`.
- **Result:** the movements in `LineNo` order, and the touched balances in first-touched order. If anything throws, nothing is returned. The method is atomic because it's pure.

### `Reverse` rules (spec §6 `:328-333`)
- **Preconditions** (`ArgumentException`):
  - `original` is non-empty and its movements all share one `DocumentId`.
  - None has a `ReversesMovementId` (S9 rejects reversing a reversal earlier with its own slug; this is defence in depth).
  - Every touched key is in `balances` (`InvalidOperationException`).
  - Every touched **physical** key is in `histories` (`InvalidOperationException`).
- **Latest-only:** order `original` by `Id`. For each physical key it touches (From or To side, first-touched order):
  - `expected` = the max `Id` among the original's movements that touch that key.
  - If `balance.LastMovementId` is null, throw `InvalidOperationException` (corrupt projection).
  - If `balance.LastMovementId != expected`, throw `new ReversalNotLatestException(key.ItemId, key.LocationId, balance.LastMovementId.Value)`.
  - Virtual keys are never checked.
- **Movements:** for each original `m` in `Id` order, `new PlannedMovement(m.LineNo, m.ItemId, m.ToLocationId, m.FromLocationId, m.Quantity, m.TotalCost, m.Id)`. That's From and To swapped, with the same quantity and cost, never re-costed.
- **Apply each one**, with no costing calls and no LUC-after-inflow:
  - The new from balance (the original's To) gets `OnHand -= qty`, and if it is physical, `StockValue -= cost`.
    - Before applying, if it is physical, `!AllowNegative` and `qty > OnHand`, throw `new InsufficientStockException(item, loc, OnHand, qty)`. Spec: a reversal goes through step 8 like any posting.
  - The new to balance (the original's From) gets `OnHand += qty`, and if it is physical, `StockValue += cost`.
  - Zero-quantity movements (revaluations, S10) go through the same arithmetic.
- **Then for each physical touched key:** `LastUnitCost = CostingPolicy.LastUnitCost(key, histories[key].Where(h => !originalIds.Contains(h.Id)))`.
  - The latest-only rule makes this equal to the fold over the full history after the reversal, because a reversed pair is adjacent on every balance it touches.
  - Write that reasoning as a short comment.
- **Result:** the mirrored movements in original `Id` order, and the touched balances.

### `tests/Inventory.UnitTests/PostingRulesTests.cs`
Use `public sealed class PostingRulesTests`, Shouldly, and underscore method names. Use fixed Guids for SUP, CUS and ADJ (virtual) and A and B (physical), and a helper that builds the balance dictionary. Cover:
- **Receipt** of 10 @ 100 into A from SUP:
  - the movement is `(1, item, SUP, A, 10, 1000.0000, null)`;
  - A is 10 / 1000 with LUC 100;
  - SUP is −10 / 0 with LUC null;
  - `LastMovementId` is unchanged on both;
  - the input dictionary is unchanged.
- **Issue:**
  - proportional (15 of 20 / 2200 → 1650, leaving 5 / 550; LUC unchanged);
  - remainder (6 of 6 / 720 → 720, leaving 0 / 0);
  - short (7 of 6 → `InsufficientStockException` with `Available == 6`, `Requested == 7`, `LocationId == A`);
  - `AllowNegative: true` and short → still `InsufficientStockException` (until S10).
- **Transfer** of 4 from A (10 / 1200) to B (empty):
  - the cost is 480, and A is 6 / 720 with LUC unchanged;
  - B is 4 / 480 with LUC 120.
- **Adjustment:**
  - positive with a cost (+2 @ 12.50 → 25);
  - positive without a cost into 3 / 30.02 (+3 → 30.0200, leaving 6 / 60.04);
  - positive without a cost into 0 / 0 → `UnitCostRequiredException` carrying that `LineNo`;
  - negative (A → ADJ, 1 of 6 / 60.04 → 10.0067).
- **Multi-line:** two items in one receipt give two movements in `LineNo` order. A two-line issue whose second line is short throws, and nothing is returned.
- **Guards:**
  - a missing balance → `InvalidOperationException`;
  - an issue into a physical location, a receipt without `UnitCost`, an issue with a `UnitCost`, and virtual → virtual → `ArgumentException`;
  - the same key in two lines → `ArgumentException`;
  - `Quantity 0` → `ArgumentOutOfRangeException`;
  - From == To → `ArgumentException`;
  - a receipt into a physical balance with `OnHand < 0` (`AllowNegative: true`) → `InvalidOperationException`.
- **Reverse a receipt**, which is the V6/V7 shape:
  - B starts at 10 / 1230 with LUC 123 and `LastMovementId` 6.
  - B's history is a transfer-in (id 5, 4 / 480, doc d1) and a receipt (id 6, 6 / 750, doc d2).
  - The result is a movement `(1, item, B, SUP, 6, 750, ReversesMovementId 6)`.
  - B ends at 4 / 480 with LUC 120, and SUP gets +6.
- **Reverse an issue:** the LUC is unchanged, and quantity and value are restored exactly.
- **Reverse a transfer:** both physical balances are restored, and each LUC is recomputed from its history.
- **Not latest:** a balance's `LastMovementId` is past the original's → `ReversalNotLatestException` with `ItemId`, `LocationId` and `BlockingMovementId == LastMovementId`.
- **More reversal guards:**
  - reversing movements that carry a `ReversesMovementId` → `ArgumentException`;
  - a missing history for a physical key → `InvalidOperationException`.

## Do not
- Touch files outside the `files` list, or change prompt 01's files. If one is wrong, stop and report why.
- Add packages, interfaces, DI registrations or any `public` type.
- Implement the estimate or `Revaluation` (S10), request validation (S5c), document-level reversal checks (S9) or any SQL.
- Create or switch branches, commit, or push.

## Done when
```bash
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests/Inventory.UnitTests.csproj
dotnet test --project tests/Architecture.Tests/Architecture.Tests.csproj
```
- Every `PostingRulesTests` and `CostingPolicyTests` case passes, and T12 stays green.
