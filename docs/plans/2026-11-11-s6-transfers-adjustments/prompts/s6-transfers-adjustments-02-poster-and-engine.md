---
type: prompt
plan: "[[2026-11-11-s6-transfers-adjustments]]"
id: 02
unit: IMS-10
status: done
model: opus
effort: high
depends_on: [01]
parallel_group: B
files:
  create: []
  modify:
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentPoster.cs
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentResponse.cs
    - src/Modules/Inventory/Inventory/Persistence/PostingCommand.cs
    - src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs
    - src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostReceipt/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostIssue/Handler.cs
  delete: []
---
# 02 · `DocumentPoster` for every kind; `ReasonCode` through the engine

## Task
Make `DocumentPoster` post all four kinds: receipt, issue, transfer and adjustment. Derive everything kind-specific from `PostingKind` in one place. Carry `ReasonCode` from the validated request through `PostingCommand` to the step-4 document insert, and return it in `DocumentResponse`. Receipts and issues must behave exactly as today. Don't add routes; prompt 03 does.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-10`. Don't switch it. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any `dotnet` command.
- **High-stakes:** `Features/Posting/**` and `Persistence/**`. Every posting route goes through these files. Keep the diff minimal and don't refactor beyond what's described here.
- **From prompt 01 (already merged on this branch):** `ValidatedPosting(Guid LocationId, string? Reference, string? Notes, IReadOnlyList<ValidatedLine> Lines)` gained the init-only properties `Guid? ToLocationId` (transfer destination; `LocationId` is then the source) and `AdjustmentReason? ReasonCode`. `ValidatedLine(int LineNo, Guid ItemId, decimal Quantity, decimal? UnitCost)`: on an adjustment, `Quantity` is the **signed** `quantityDelta`, never 0, with `UnitCost` only on positive lines. `PostingValidation.Transfer`/`.Adjustment` and `RequestHash.Transfer`/`.Adjustment` exist.

### 1. `Persistence/PostingCommand.cs` (plan D4)
Today: `internal sealed record PostingCommand(PostingKind Kind, StockDocumentType DocumentType, IReadOnlyList<PostingLine> Lines, string IdempotencyKey, byte[] RequestHash, string? Reference, string? Notes, Guid PostedBy);`
- Append a **required** (no default) last positional parameter `AdjustmentReason? ReasonCode`. Doc: "Set on an adjustment, null on every other type, including a reversal of an adjustment (`CK_StockDocuments_ReasonCode`)."
- Its other caller, `Seeding/InventorySeeder.cs:206-207`, passes `ReasonCode: null` (named argument, last).

### 2. `Persistence/PostingEngine.cs`
- Step 4 (`:111-123`): `ReasonCode = null,` → `ReasonCode = command.ReasonCode,`.
- Step 6 (`:178`): replace the comment "Receipts and issues move every line between the same two locations, so the cross product is the touched set." with one that covers all four kinds. Every kind moves all its lines between exactly two locations: a receipt, issue or adjustment between the location and its counterpart (an adjustment's lines may point either way), and a transfer from A to B. So items × locations is the touched set. Leave the check itself unchanged.
- Nothing else in the engine changes: no lock, SQL, transaction or step order. Spec §6 isn't changed.

### 3. `Features/Posting/DocumentResponse.cs` (plan D7)
Add `string? ReasonCode` right after `string Type`, filled with `outcome.Document.ReasonCode?.ToString()`. Same shape for a `201` and a replay.

### 4. `Features/Posting/DocumentPoster.cs` (plans D1, D5, D6)
Read the whole file first (`:18-140`). Keep its D2 order exactly: key, pure step 1, hash, `FindExistingAsync` replay, step 1's database checks, engine. Keep every existing message and key.
- **New signature:**
  ```csharp
  public async Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> PostAsync(
      HttpContext http,
      PostingKind kind,
      Func<Dictionary<string, string[]>, ValidatedPosting?> validate,
      Func<ValidatedPosting, byte[]> hash,
      CancellationToken ct)
  ```
  This drops `type`, `counterpart` and `counterpartIsSource`. Update the XML docs and the class summary ("every posting route except reversals").
- **One private static switch on `kind`** (an exhaustive `switch` expression that throws `ArgumentOutOfRangeException` on anything else) gives:
  - the `StockDocumentType`: Receipt → `Receipt`, Issue → `Issue`, Transfer → `Transfer`, Adjustment → `Adjustment`;
  - the counterpart `LocationKind?`: Receipt → `Supplier`, Issue → `Customer`, Adjustment → `LocationKind.Adjustment`, Transfer → `null`;
  - the header locations to check, as `(string Field, Guid Id)` in order: Transfer → `("fromLocationId", validated.LocationId)`, `("toLocationId", validated.ToLocationId ?? throw …)`; all others → `("locationId", validated.LocationId)`.
  - `Inventory.Domain.PostingKind.Adjustment` and `Inventory.Persistence.LocationKind.Adjustment` share a name, so qualify where needed.
- **Step 1's database part (D6):**
  - Load the header locations in one query (`db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct)`).
  - For each header `(Field, Id)` whose row is missing or not `Kind == LocationKind.Physical`, set `errors[Field] = UnknownLocation`.
  - Items as today.
  - Every `400` comes before any `422`, as today.
  - Then the inactive checks: the first inactive item in line order → `Problems.InactiveItem`; then the first inactive header location, in header order → `Problems.InactiveLocation(id)`. That's the engine's step-7 order (items, then each line's From before To).
- **Lines (in `LineNo` order):**
  - Receipt: `new PostingLine(l.LineNo, l.ItemId, counterpartId, location, l.Quantity, l.UnitCost)`.
  - Issue: `new PostingLine(l.LineNo, l.ItemId, location, counterpartId, l.Quantity, UnitCost: null)`.
  - Transfer: `new PostingLine(l.LineNo, l.ItemId, from, to, l.Quantity, UnitCost: null)`.
  - Adjustment: `l.Quantity > 0 ? new PostingLine(l.LineNo, l.ItemId, counterpartId, location, l.Quantity, l.UnitCost) : new PostingLine(l.LineNo, l.ItemId, location, counterpartId, -l.Quantity, UnitCost: null)`.
  - This is the only place a signed delta becomes a positive quantity (plan D2). Look up the counterpart only when the kind has one; a missing counterpart still throws (`SingleAsync`), as today.
- **Command:** `new PostingCommand(kind, type, postingLines, key, requestHash, validated.Reference, validated.Notes, user.UserId, validated.ReasonCode)`.
- **`ToResult` (D5)** gains `kind`:
  - Add `case RejectedOutcome { Rejection: UnitCostRequiredException e }:` → `Problems.Validation(new() { [$"lines[{e.LineNo - 1}].unitCost"] = UnitCostRequired })`, with `private static readonly string[] UnitCostRequired = ["Enter a unit cost: there is no stock here to average."];`. `LineNo` is 1-based, and its line maps to request index `LineNo - 1`, as `BalanceOutOfRange` already assumes.
  - `BalanceOutOfRangeOutcome`: the field is `quantityDelta` when `kind == PostingKind.Adjustment`, otherwise `quantity` (today's key).
  - The `default` branch stays a throw. Update its comment: only `ReversalNotLatest` is left, and S9 maps it on its own route.
  - Keep the `InsufficientStockException` mapping. An adjustment's `requested` is the positive quantity the engine saw, which is correct.

### 5. Handlers
- `PostReceipt/Handler.cs`: `poster.PostAsync(http, PostingKind.Receipt, errors => PostingValidation.Receipt(request, errors), RequestHash.Receipt, cancellationToken)`. Drop the unused `using Inventory.Persistence;` if `dotnet format` flags it.
- `PostIssue/Handler.cs`: the same, with `PostingKind.Issue`, `PostingValidation.Issue` and `RequestHash.Issue`.

### Review constraints that apply
- No UPDATE or DELETE on `StockDocuments`/`StockMovements`. The engine still only inserts them.
- `TenantId` never comes from a request, and `PostedAt` only from `TimeProvider` (the engine, unchanged).
- Decimals only.
- Ask before changing spec §6 or adding a column. This prompt does neither: `ReasonCode` already exists.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change `PostingEngine`'s locks, SQL, transaction, retry, or step order, or any `Domain/**` file.
- Change any existing problem message, error key or status for receipts and issues.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
git diff develop -- tests   # prints only prompt 01's unit-test additions: no integration test was edited
git diff -U0 develop -- src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs | grep -E '^[-+][^-+]'   # only the ReasonCode line and the step-6 comment
```
- Every existing receipt, issue, replay, concurrency, clock, T5 and seed test passes unmodified. That's the proof that the receipt and issue behaviour is unchanged.
