---
type: prompt
plan: "[[2026-11-15-s11-ledger-health]]"
id: 02
unit: IMS-15
status: todo
model: opus
effort: high
depends_on: [01]
parallel_group: B
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Ledger/LedgerEndpoints.cs
    - src/Modules/Inventory/Inventory/Features/Ledger/LedgerHealthResponse.cs
    - src/Modules/Inventory/Inventory/Features/Ledger/GetLedgerHealth/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Ledger/GetLedgerHealth/Handler.cs
    - tests/Inventory.IntegrationTests/LedgerHealthTests.cs
  modify:
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 02 · Health endpoint: `GET /api/ledger/health`

## Task
Add the Admin-only `GET /api/ledger/health`. It reports INV-1 to INV-5 for the current tenant with failing rows, takes no lock and uses one SQL statement per check. Register it, give it a T5 probe, and test that each invariant's failure is detected.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-15`. Stop if `git branch --show-current` says otherwise. .NET 10, `Nullable` on, `TreatWarningsAsErrors`; module types are `internal`. Raw SQL only through EF. `TenantId` comes from `ITenantContext`, never a request. Unnamed `IgnoreQueryFilters()` is banned.
- Prompt 01 added `Persistence/LedgerProjection.ReadAsync(db, tenantId, ct)` → `List<ProjectedBalance>` (`ItemId, Sku, LocationId, LocationCode, IsVirtual, AllowNegativeStock, Stored: BalanceValues?, Ledger: BalanceValues`, `Differs`), with `BalanceValues(OnHand, StockValue, LastUnitCost, LastMovementId)`. Read it first.
- Spec §6 `:335-343` (invariants; *"Health takes no lock: each invariant is a single statement"*), §9 `:549` (`GET /ledger/health` · Admin · *"INV-1 to INV-5 results with failing rows"*).
- Patterns: endpoint `Features/Reversals/ReverseDocument/Endpoint.cs` (`.RequireAuthorization(InventoryPolicies.Admin)`); group `Features/Stock/StockEndpoints.cs`; raw `SqlQuery` with a private `Row` class and `TenantId = {tenantId}` on every table in `Features/Items/ListItemMovements/Handler.cs:32-60`; registration `InventoryModuleExtensions.cs:53-81`.

### 1. Response (`Features/Ledger/LedgerHealthResponse.cs`), exactly this contract (plan "API contract")
```csharp
internal sealed record LedgerHealthResponse(DateTimeOffset CheckedAt, bool Healthy, IReadOnlyList<InvariantResult> Invariants);
internal sealed record InvariantResult(string Id, string Name, bool Passed, int FailureCount, IReadOnlyList<InvariantFailure> Failures);
internal sealed record InvariantFailure(
    Guid? ItemId, string? Sku, Guid? LocationId, string? LocationCode, long? MovementId, string Check, decimal? Expected, decimal? Actual);
```
Ids and names, always all five and in this order: `INV-1` "Projection", `INV-2` "Conservation", `INV-3` "Physical sanity", `INV-4` "Reversal mirror", `INV-5` "Last unit cost". `Failures` holds at most 100 entries (`const int MaxFailures = 100`), while `FailureCount` is the full count. Order the failures by `Sku` (`StringComparer.Ordinal`), then `LocationCode` (Ordinal), then `MovementId`, then `Check` (Ordinal). `Healthy` = all passed. `CheckedAt` = `TimeProvider.GetUtcNow()` (inject `TimeProvider`).

### 2. Handler (`GetLedgerHealthHandler(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)`) (plan D3, D7, D8)
No transaction and no lock hints. Each check below is **one** statement, run in sequence.
- **INV-1 and INV-5, from `LedgerProjection.ReadAsync`.** For each row:
  - `Stored is null` → INV-1 `MissingBalance` (expected and actual null).
  - Otherwise compare field by field. INV-1 `OnHand`, `StockValue` and `LastMovementId` (as decimals: `(decimal?)long?`) go to INV-1, and so does `LastUnitCost` **when the row is virtual**. A physical row's `LastUnitCost` mismatch goes to INV-5 `LastUnitCost`. Expected = `Ledger`, Actual = `Stored`.
- **INV-2.** `SELECT b.ItemId, i.Sku, SUM(b.OnHand) AS Total FROM inventory.StockBalances b JOIN inventory.Items i ON i.TenantId = {t} AND i.Id = b.ItemId WHERE b.TenantId = {t} GROUP BY b.ItemId, i.Sku HAVING SUM(b.OnHand) <> 0` → check `OnHandSum`, expected 0, actual `Total`, location fields null.
- **INV-3.** Physical balances joined to `Locations` (D7: `l.Kind = 'Physical'` and the location's `AllowNegativeStock`, not the balance's mirror). Each row yields at most one failure, in this priority:
  - `NegativeNotAllowed`: `OnHand < 0 AND l.AllowNegativeStock = 0`; actual = `OnHand`.
  - `ValueWithStock`: `OnHand >= 0 AND StockValue < 0`; actual = `StockValue`.
  - `ValueWithoutStock`: `OnHand = 0 AND StockValue <> 0`; actual = `StockValue`.
  - Comment: the balance CHECKs (`InventoryDbContext.cs:133-135`) already forbid the last two on a row whose mirror is right, so the reachable failure is a mirror that says allowed over a location that says no.
- **INV-4.** Read `LedgerInvariants.cs` (the test oracle's INV-4) and match it. Every movement with `ReversesMovementId IS NOT NULL` is joined to its original (the FK guarantees it exists).
  - Mismatches report `MovementId` = the reversal movement, with checks `Item`, `FromLocation` (reversal From ≠ original To) and `ToLocation` (reversal To ≠ original From), with expected/actual null. `Quantity` and `TotalCost` report expected = the original's value, actual = the reversal's.
  - `ReversesReversal`: the original itself has a `ReversesMovementId`.
  - `ReversedTwice`: `GROUP BY ReversesMovementId HAVING COUNT(*) > 1`, with `MovementId` = the original and actual = the count.
  - Return one SQL row per (movement, check). `UNION ALL` of the checks is fine, as long as it's one statement for INV-4. Join `Items` for `Sku`; location fields null.
- `TenantId = {tenantId}` on every table reference, as in prompt 01.
- Return `TypedResults.Ok(new LedgerHealthResponse(...))`.

### 3. Endpoints and wiring
- `Features/Ledger/LedgerEndpoints.cs`: `internal static class LedgerEndpoints { public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder api) { var ledger = api.MapGroup("/ledger"); ledger.MapGetLedgerHealth(); return api; } }`. Prompt 03 adds rebuild to this group.
- `GetLedgerHealth/Endpoint.cs`: `MapGet("/health", (GetLedgerHealthHandler handler, CancellationToken ct) => handler.HandleAsync(ct)).RequireAuthorization(InventoryPolicies.Admin);`.
- `InventoryModuleExtensions`: `AddScoped<GetLedgerHealthHandler>()` and `endpoints.MapLedgerEndpoints();`, in the existing style. Check whether `TimeProvider` is already registered (grep `TimeProvider` in `src/Api/Program.cs` and the module) before you rely on it. `PostingEngine` already injects it.

### 4. T5 (`tests/Inventory.IntegrationTests/TenantIsolationTests.cs`, plan D10)
Add `[Fact][ProbesRoute("GET /api/ledger/health")] Each_tenant_sees_only_its_own_ledger_health`. Copy the shape of `Each_tenant_lists_only_its_own_stock` (`:152-190`):
- Two Admin tenants, each receiving into its own location.
- Drift tenant B's physical balance with a raw `UPDATE … SET OnHand = OnHand + 1, StockValue = StockValue + 10` on a `LedgerRows.OpenAsync(db, tenantB, ct)` connection (`WHERE TenantId = @t AND ItemId = @i AND LocationId = @l`).
- A's health is `200`, `healthy: true`, and no failure carries any of B's item or location ids.
- B's health is `healthy: false` with INV-1 `OnHand` and `StockValue` on B's key.

Leave the route guard test and `Exempt` alone. The new raw text is `GET /api/ledger/health`; check it against the guard's output if it fails.

### 5. `tests/Inventory.IntegrationTests/LedgerHealthTests.cs` (new, `public sealed class LedgerHealthTests(DatabaseFixture db)`)
Own tenant per test. Reuse `PostingTests`, `TransferTests`, `AdjustmentTests`, `ReversalTests`, `LocationsTests.AllowNegativeAsync`, `LedgerRows` and `DatabaseFixture` helpers (see prompt 01's list); `PostingTests.AddUserAsync(app, tenant, Roles.Staff)` for Staff. Parse with `ReadFromJsonAsync<JsonNode>` and compare decimals with `GetValue<decimal>()`.
- `New_tenant_is_healthy`: no postings → `200`, `healthy: true`, five invariants with ids `INV-1`…`INV-5` in order, all `passed`, `failureCount` 0.
- `Mixed_ledger_is_healthy`: receipts, an issue, a transfer, a `+`/`−` adjustment, a reversal, and a negative issue plus a settling receipt at a toggle-on location → healthy.
- `Drifted_projection_fails_INV_1`: raw `UPDATE` of `OnHand`/`StockValue` (keep CHECKs valid) and separately `LastMovementId = NULL` → INV-1 failures `OnHand`, `StockValue`, `LastMovementId` with the right expected/actual, sku and location code. The other invariants that don't depend on these fields stay passed.
- `Virtual_drift_fails_INV_1_and_INV_2`: `UPDATE` the item's `SUP` row `OnHand = OnHand + 1` → INV-1 `OnHand` and INV-2 `OnHandSum` (actual 1).
- `Mirror_drift_fails_INV_3`: on a toggle-off location with stock, `UPDATE … SET AllowNegative = 1, OnHand = -1, StockValue = -10` (allowed by the CHECK through the mirror) → INV-3 `NegativeNotAllowed`, actual −1.
- `Bad_reversal_fails_INV_4`: post a receipt through the API, read its movement. Then with `LedgerRows.AddDocumentAsync(c, ct, type: "Reversal", reverses: receiptDocId)` and `LedgerRows.AddMovementAsync(c, doc, 1, item, from: location, to: sup, quantity, totalCost + 1, ct, reverses: movementId)` → INV-4 `TotalCost` with expected/actual. Add a second movement to **the same** reversal document at `LineNo` 2 with the same `reverses: movementId` and the correct cost (a second reversal document would hit `UX_StockDocuments_Reverses`) → `ReversedTwice` on the original, actual 2. Read `LedgerRows.cs:64-100` first. The direct inserts leave balances stale, so INV-1 fails too; assert only the INV-4 entries.
- `Stale_last_unit_cost_fails_INV_5`: `UPDATE … SET LastUnitCost = 1.2345` on a physical row → INV-5 `LastUnitCost`, expected = the real value, actual 1.2345. INV-1 passes.
- `Staff_gets_403`: Staff `GET /api/ledger/health` → `403`.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Take a lock, open a transaction or add lock hints in the health path.
- Add a ProblemDetails slug, a migration or a column. Change `LedgerProjection` (if it's wrong, stop and report).
- Add dependencies, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerHealthTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests' --filter-class '*LedgerProjectionTests'
dotnet test --project tests/Architecture.Tests
```
- `Every_api_route_is_probed_or_exempt` passes with `GET /api/ledger/health` probed, not exempted.
