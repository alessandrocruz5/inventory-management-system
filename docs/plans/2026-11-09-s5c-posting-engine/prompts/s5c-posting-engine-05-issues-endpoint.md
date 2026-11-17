---
type: prompt
plan: "[[2026-11-09-s5c-posting-engine]]"
id: 05
unit: IMS-8
status: done
model: opus
effort: high
depends_on: [04]
parallel_group: D
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Posting/PostIssue/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostIssue/Handler.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentEndpoints.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
    - tests/Inventory.IntegrationTests/PostingTests.cs
  delete: []
---
# 05 · Issues endpoint

## Task
Add `POST /api/documents/issues` (Staff, Admin) on the shared `DocumentPoster` from prompt 04. Each line moves the physical `locationId` → the tenant's Customer location at the moving-average outflow cost. Add its T5 probe and behaviour tests, including the plan's D2 replay test.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-8`. Don't switch it.
- **Prerequisite:** run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` first.
- **Copy prompt 04's receipt files exactly:**
  - `Features/Posting/PostReceipt/Endpoint.cs`
  - `Features/Posting/PostReceipt/Handler.cs`
  - `Features/Posting/DocumentEndpoints.cs`
  - the receipt tests in `tests/Inventory.IntegrationTests/PostingTests.cs`. Reuse its `internal static PostDocumentAsync` helper and `LocationsTests.CreateAsync` / `ItemsTests.CreateAsync`; don't copy them.
  - the receipt probe in `TenantIsolationTests.cs`
- **Inputs from prompt 01:** `Features/Posting/PostIssue/Request.cs` (`PostIssueRequest`, `PostIssueLine(ItemId, Quantity)`), `PostingValidation.Issue(request, errors)` and `RequestHash.Issue(validated)`.
- **The poster call:** `PostingKind.Issue`, `StockDocumentType.Issue`, `LocationKind.Customer`. The counterpart is the target: `From = location`, `To = CUS`, `UnitCost = null`.
- **Costing is already done:** `PostingRules.Post` calls `CostingPolicy.OutflowCost`. If `qty == OnHand` it takes the remainder; if `qty < OnHand` it's `Round4(qty × StockValue / OnHand)`. Otherwise it throws `InsufficientStockException`, which the poster maps to `422 insufficient-stock { itemId, locationId, available, requested }` (spec §9).
- **A first issue from a balance that has never been received** creates the balance at step 5, then rejects it with `available` 0. The transaction rolls back, so no balance row remains.

### Code
- `PostIssue/Handler.cs`: `PostIssueHandler(DocumentPoster poster)`.
- `PostIssue/Endpoint.cs`: `MapPostIssue(this RouteGroupBuilder documents)` → `MapPost("/issues", …)`. No role policy.
- `DocumentEndpoints.cs`: add `documents.MapPostIssue();`.
- `InventoryModuleExtensions.cs`: register `PostIssueHandler` as scoped.

### T5 probe (`TenantIsolationTests.cs`)
`[ProbesRoute("POST /api/documents/issues")]`. Tenant B's `locationId` gives `400 errors.locationId`. Tenant B's `itemId` gives `400 errors["lines[0].itemId"]`. Tenant B's balances are unchanged. First receive stock into B's location for B's item, as B, through the API, so the balance exists. Then read that balance before and after A's attempts, as B, via `LedgerRows.OpenAsync` + `SELECT OnHand, StockValue`, and assert that B's `StockMovements` row count is unchanged.

### Tests (`PostingTests.cs`)
- **`Issue_costs_at_the_moving_average`.** Receive 10 @ 100.00 and 10 @ 120.00, then issue 15. That's spec §7 V1–V3. Expect a `201` with movement L → CUS, `totalCost` 1650.0000, and balance 5 / 550.0000. `lastUnitCost` is unchanged at 110.0000, because outflows don't set it.
- **`Issue_of_the_whole_balance_takes_the_remainder`.** Receive 1 @ 10.00 and 2 @ 10.01. Issue 3 → cost 30.0200 and balance 0 / 0.0000.
- **`Issue_beyond_on_hand_is_422_and_writes_nothing`.** Receive 6, then issue 7. Expect `422 /problems/insufficient-stock` with `itemId`, `locationId`, `available` 6 and `requested` 7. The document count is unchanged, and the balance stays at 6.
- **`Rejected_issue_does_not_consume_its_key`.** Issue 7 with key K → `422`. Receive 1 more. Then issue 7 with the **same key and body** → `201`. Only a successful post consumes a key (spec §6 step 2).
- **`First_issue_of_an_unreceived_item_is_422_and_leaves_no_balance`.** `available` is 0, and no `StockBalances` row exists for (item, L) or (item, CUS).
- **`Same_key_on_another_endpoint_is_409`.** Post a receipt with key K, then an issue with key K → `409 idempotency-key-reused`. The route is in the hash.
- **`Replay_survives_a_later_deactivation`** (plan D2):
  1. Receive 2 of item X into L with key K1.
  2. Issue 2 → stock is 0 / 0.
  3. `POST /api/items/{X}/deactivate` → `200`.
  4. Replay K1 with the identical body → `200` with `Idempotent-Replayed: true` and the original `id`.
  5. A **new** key with the same body → `422 /problems/inactive-item`.
- **`Issue_validates_fields`** (`[Theory]`): empty `lines`, `quantity` 0, a duplicate item, a virtual `locationId`.

## Do not
- Touch files outside the `files` list.
- Change `DocumentPoster`, `PostingEngine` or the Domain. If one blocks a test, stop and report it.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
```
- Every `PostingTests` case passes, and the T5 route guard is green with both posting routes probed.
