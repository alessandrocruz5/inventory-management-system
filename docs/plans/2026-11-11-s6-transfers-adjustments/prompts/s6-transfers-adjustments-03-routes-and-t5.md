---
type: prompt
plan: "[[2026-11-11-s6-transfers-adjustments]]"
id: 03
unit: IMS-10
status: done
model: opus
effort: medium
depends_on: [02]
parallel_group: C
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Posting/PostTransfer/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostTransfer/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostAdjustment/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostAdjustment/Handler.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentEndpoints.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 03 · Transfer and adjustment routes, T5 probes

## Task
Map `POST /api/documents/transfers` (Staff and Admin) and `POST /api/documents/adjustments` (Admin only) on `DocumentPoster`. Register their handlers, and add the T5 probe for each route in the same change: T5's route guard fails the suite for any `/api` route without one.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-10`. Don't switch it. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any `dotnet` command.
- **Already on the branch (prompts 01–02):**
  - `PostTransferRequest`/`PostTransferLine` (namespace `Inventory.Features.Posting.PostTransfer`) and `PostAdjustmentRequest`/`PostAdjustmentLine` (`Inventory.Features.Posting.PostAdjustment`).
  - `PostingValidation.Transfer(request, errors)` and `.Adjustment(request, errors)`; `RequestHash.Transfer` and `.Adjustment`.
  - `DocumentPoster.PostAsync(HttpContext http, PostingKind kind, Func<Dictionary<string,string[]>, ValidatedPosting?> validate, Func<ValidatedPosting, byte[]> hash, CancellationToken ct)`, returning `Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>>`.

### Files to copy exactly
- `Features/Posting/PostIssue/Endpoint.cs` → `PostTransfer/Endpoint.cs`: `PostTransferEndpoint.MapPostTransfer(this RouteGroupBuilder documents)` maps `"/transfers"` with no role policy. Doc: "Staff and Admin may both post transfers (spec §9)."
- `PostAdjustment/Endpoint.cs`: `PostAdjustmentEndpoint.MapPostAdjustment` maps `"/adjustments"` and chains `.RequireAuthorization(InventoryPolicies.Admin)`, as `Features/Items/CreateItem/Endpoint.cs:13` does (`using Inventory.Features;` if needed). Doc: "Admin only (spec §9); Staff gets 403."
- `Features/Posting/PostIssue/Handler.cs` (after prompt 02) → `PostTransferHandler(DocumentPoster poster)` calls `poster.PostAsync(http, PostingKind.Transfer, errors => PostingValidation.Transfer(request, errors), RequestHash.Transfer, cancellationToken)`. `PostAdjustmentHandler` is the same with `PostingKind.Adjustment`, `PostingValidation.Adjustment` and `RequestHash.Adjustment`. Give each a one-line summary of what the document moves (spec §6 table).
- `Features/Posting/DocumentEndpoints.cs:14-17`: add `documents.MapPostTransfer();` and `documents.MapPostAdjustment();` after the issue route.
- `InventoryModuleExtensions.cs:54-55`: add `builder.Services.AddScoped<PostTransferHandler>();` and `builder.Services.AddScoped<PostAdjustmentHandler>();`, with their `using`s.

### T5 probes: `tests/Inventory.IntegrationTests/TenantIsolationTests.cs`
- Copy `Another_tenants_ids_in_an_issue_body_are_400` (`:360-399`): two tenants with Admin users, B's location and item with stock, and A's location and item.
- Add `[Fact] [ProbesRoute("POST /api/documents/transfers")] Another_tenants_ids_in_a_transfer_body_are_400`. Create a second A location (`"A-TWO"`) and receive stock at A's first location for `itemA`, so a request that wasn't stopped at step 1 could succeed. Post as A:
  - from B's location to A's location → `400`, keys exactly `["fromLocationId"]`;
  - from A's location to B's location → `["toLocationId"]`;
  - A → A-TWO with B's item → `["lines[0].itemId"]`.
  - Then assert B's balance and B's movement count are unchanged.
- Add `[Fact] [ProbesRoute("POST /api/documents/adjustments")] Another_tenants_ids_in_an_adjustment_body_are_400`. Post as A's Admin, with `reasonCode` `"Damaged"`:
  - B's location with A's item, `quantityDelta` `-1` → `["locationId"]`;
  - A's location with B's item, `quantityDelta` `1` and `unitCost` `1` → `["lines[0].itemId"]`.
  - Then assert B's ledger is unchanged.
- Build the bodies inline as anonymous objects: `new { fromLocationId, toLocationId, reference = (string?)null, notes = (string?)null, lines = new[] { new { itemId, quantity } } }` and `new { locationId, reasonCode = "Damaged", reference = (string?)null, notes = (string?)null, lines = new[] { new { itemId, quantityDelta, unitCost } } }`. Prompt 04 adds the shared builders in its own files, so don't touch `PostingTests.cs`. Use `PostingTests.PostDocumentAsync`, `PostingTests.BalanceAsync`, `LedgerRows.OpenAsync`, `LocationsTests.CreateAsync` and `ItemsTests.CreateAsync`, as the issue probe does.
- The route guard `Every_api_route_is_probed_or_exempt` (`:401-424`) must pass with no `Exempt` entry added. The raw text is `POST /api/documents/transfers` (no trailing slash, as for receipts).

### Review constraints that apply
- `TenantId` never comes from a request. A body id from another tenant reads as unknown, giving a `400` (spec §8), never a `404` or `403`.
- No new package. Everything is `internal`; T12's public-surface rule must stay green.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add an `Exempt` entry for either route.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*TenantIsolationTests'
dotnet test --project tests/Architecture.Tests
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
```
- `Every_api_route_is_probed_or_exempt` passes, and both new probes pass.
