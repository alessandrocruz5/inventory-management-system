---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 10
unit: IMS-30
status: done
model: opus
effort: high
depends_on: [09]
parallel_group: J
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Stocktakes/StocktakeEndpoints.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/StocktakeResponses.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/StocktakeReader.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/StartStocktake/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/StartStocktake/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/StartStocktake/Request.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/ListStocktakes/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/ListStocktakes/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/ListStocktakes/Request.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/GetStocktake/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Stocktakes/GetStocktake/Handler.cs
    - tests/Inventory.IntegrationTests/StocktakeTests.cs
  modify:
    - src/Shared/Kernel/Problems/ProblemTypes.cs
    - src/Modules/Inventory/Inventory/Features/Problems.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
    - web/src/app/core/problems/problem-messages.ts
    - web/src/app/core/problems/problem-messages.spec.ts
    - docs/Inventory System — Sprint 1 Spec.md
  delete: []
---
# 10 · Stocktakes: start, list, detail, and the two new slugs

## Task
Add `POST /api/stocktakes` (start, prefilled), `GET /api/stocktakes` (paged list) and `GET /api/stocktakes/{id}` (detail). Add the slugs `stocktake-open-exists` and `stocktake-not-open` to spec §9, Kernel, `Features/Problems.cs` and the Angular map, all in this PR. Add T5 probes and tests.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. Prompt 09 added `db.Stocktakes` and `db.StocktakeLines` (`Persistence/{Stocktake,StocktakeLine,StocktakeStatus}.cs`).
- Kernel (`src/Shared/Kernel/**`) is Shared code: add constants only.
- Hidden coupling (CLAUDE.md, IMS-3 Downstream): each new slug lands in spec §9, Kernel `ProblemTypes` and Angular `PROBLEM_MESSAGES` in the same PR.
- T10 (`PostingClockTests.cs:89`): `Inventory.Features` types ending in `Request` or `Line` must carry no date properties. Dated DTOs **must** end in `Response`.
- Plan D11: start is any signed-in user (Staff or Admin; the `/api` group already requires sign-in). Viewing is any user.
- Plan D12: one open (Draft or Posting) stocktake per location.
- Plan D13: the prefill is every **active** item with a **physical** balance at the location where `OnHand <> 0`, uncounted.
- Plan D14: lines are ordered by `Sku` with `StringComparer.Ordinal`, in C#.
- Patterns:
  - `Features/Items/CreateItem/*` (201 with `Location`; `ICurrentUser user`, `TimeProvider clock`).
  - `Features/Documents/ListDocuments/{Request,Handler}.cs` (paging via `Features/Paging.cs`, `Paging.Resolve` and `ToPageAsync`, exact ordinal enum parsing of a filter).
  - `Features/Locations/DeactivateLocation/Handler.cs` (execution strategy plus transaction plus `ChangeTracker.Clear()`).
  - `Features/Problems.cs` (problem factories with extensions, e.g. `IdempotencyKeyReused`).
  - `Features/Items/GetItem/Handler.cs` (joins; average cost = `CostingPolicy.Round4(StockValue / OnHand)` when `OnHand > 0`).

### 1. Slugs
- `ProblemTypes`:
  - `StocktakeOpenExists = "stocktake-open-exists"`: "The location already has an open stocktake (plan D12)."
  - `StocktakeNotOpen = "stocktake-not-open"`: "The stocktake isn't in a state that allows this (plan D10/D12)."
- `Features/Problems.cs`:
  - `StocktakeOpenExists(Guid stocktakeId)`: 409, title "A stocktake is already open here", extension `stocktakeId`.
  - `StocktakeNotOpen(StocktakeStatus status)`: 409, title "This stocktake is closed", extension `status` (the enum name).
- `web/src/app/core/problems/problem-messages.ts`:
  - `'stocktake-open-exists': 'This location already has a stocktake in progress. Open it to continue counting.'`
  - `'stocktake-not-open': 'This stocktake has already been posted or cancelled.'`
  - Extend `problem-messages.spec.ts` in its style.
- Spec §9 problem table: two rows. `stocktake-open-exists` · 409 · `stocktakeId`. `stocktake-not-open` · 409 · `status`.

### 2. Responses (`StocktakeResponses.cs`)
- `StocktakeSummaryResponse(Guid Id, Guid LocationId, string LocationCode, string LocationName, string Status, string? Notes, DateTimeOffset CreatedAt, Guid CreatedBy, int LineCount, int CountedCount, Guid? PostedDocumentId)`.
- `StocktakeDetailResponse(Guid Id, Guid LocationId, string LocationCode, string LocationName, string Status, string? Notes, DateTimeOffset CreatedAt, Guid CreatedBy, DateTimeOffset? UpdatedAt, Guid? PostedDocumentId, IReadOnlyList<StocktakeLineResponse> Lines)`.
- `StocktakeLineResponse(Guid ItemId, string Sku, string Name, string BaseUnit, bool ItemIsActive, decimal? ExpectedQuantity, decimal? CountedQuantity, decimal? Difference, decimal? UnitCost, DateTimeOffset? CountedAt, Guid? CountedBy, decimal CurrentOnHand, decimal? AverageCost, bool MovedSinceCount)`.
  - `Difference` = counted − expected, when counted.
  - `CurrentOnHand` and `AverageCost` come from the current physical balance (0 and null when there is none).
  - `MovedSinceCount` = counted and the balance's current `LastMovementId` ≠ `ExpectedLastMovementId`, comparing null-safely.

### 3. `StocktakeReader.cs` (shared by GET, start, and prompts 11–12)
`internal sealed class StocktakeReader(InventoryDbContext db)` with `Task<StocktakeDetailResponse?> ReadAsync(Guid id, CancellationToken ct)`:
- Load the stocktake (null → caller 404), the location, the lines joined to items, and the current balances for `(item, LocationId)`.
- Order the lines in C# by `Sku` with `StringComparer.Ordinal` (D14).
- Expose `internal static IReadOnlyList<StocktakeLine> InLineOrder(...)` or an equivalent helper, so prompt 12 maps error indexes with the same order.
- Register it as scoped.

### 4. POST /stocktakes
- `StartStocktakeRequest(Guid? LocationId, string? Notes)`.
- Pure checks: `locationId` is required (400). `notes` is trimmed, empty becomes null, at most 500 characters (400).
- Location lookup: missing or not `Physical` → 400 `locationId` "Choose an existing physical location." (A tenant B id is invisible, so it's 400.) Inactive → `Problems.InactiveLocation(id)` (422).
- Then, in a strategy transaction:
  - Insert a `Stocktake` with `Status = Draft`, `CreatedAt = clock.GetUtcNow()` and `CreatedBy = user.UserId`.
  - Insert prefill lines for the D13 items (all count fields null).
  - Save. On `Problems.IsUniqueViolation(ex)` (`UX_Stocktakes_Open`), clear the tracker, look up the open stocktake for that location and return `Problems.StocktakeOpenExists(existing.Id)`.
  - Commit.
- Return `TypedResults.Created($"/api/stocktakes/{id}", detail)`, where `detail` comes from `StocktakeReader`.

### 5. GET /stocktakes
- `ListStocktakesRequest(int? Page, int? PageSize, string? Status, Guid? LocationId)`.
- `status` is parsed by exact ordinal name, as in ListDocuments; an unknown value is 400 `status`.
- Order by `CreatedAt` descending, then `Id`. Page with `ToPageAsync`.
- Each row has its `LineCount` and `CountedCount` (lines with `CountedQuantity != null`).

### 6. GET /stocktakes/{id}
`StocktakeReader.ReadAsync`, or 404.

### 7. Endpoints
`StocktakeEndpoints.MapStocktakeEndpoints(this IEndpointRouteBuilder api)`, with group `/stocktakes`, routes `""`, `""` and `"/{id:guid}"`. Call it from `InventoryModuleExtensions.MapInventoryEndpoints` (`:80-88`) and register the handlers and the reader (`:56-76`). Prompts 11 and 12 add more routes to this file.

### 8. Tests (`StocktakeTests.cs`)
Copy the shape from `LocationsTests`/`PostingTests`: own tenant per test, Admin client, `PostingTests.AddUserAsync` for Staff, `PostingTests.NewLocationAsync`, `NewItemAsync`, `ReceiveAsync`, `PostDocumentAsync`, `Issue`, `IssueLine`, `NewKey`, and `LocationsTests.CreateAsync`.
- Add a helper `internal static async Task<JsonNode> StartAsync(HttpClient client, Guid locationId, CancellationToken ct)` that asserts 201 and returns the body. Prompts 11 and 12 use it.
- `Start_prefills_items_with_stock_and_orders_by_sku`: receive B-ITEM 5 and A-ITEM 3 at L; C-ITEM has none at L; D-ITEM is received then deactivated after being issued to 0 (or simply has no balance). The lines are exactly A-ITEM then B-ITEM, uncounted, with `currentOnHand` 3 and 5. Status `Draft`, `createdBy` = the user.
- `Staff_can_start_and_view`: Staff POST → 201, and the GET works.
- `Second_open_stocktake_is_409_with_its_id`: start twice at L → 409 `stocktake-open-exists`, with `stocktakeId` = the first. Then two parallel starts on a fresh location → exactly one 201 and one 409.
- `Start_validates_location`: none → 400. The SUP id (virtual) → 400 `locationId`. An inactive location → 422 `inactive-location`.
- `List_filters_and_pages`: two locations, three stocktakes. Filter by `locationId` and by `status=Draft`. `status=draft` → 400. Newest first.
- `Unknown_id_is_404`.
- `TenantIsolationTests.cs`, with `[ProbesRoute]` using the exact `RawText`:
  - `GET /api/stocktakes/`: tenant A sees zero B rows. Note the trailing slash for a `""` route, per the IMS-5 Downstream note.
  - `POST /api/stocktakes/`: B's location id in the body → 400.
  - `GET /api/stocktakes/{id:guid}`: B's id → 404.

## Do not
- Touch files outside `files`. If you must, stop and report why. Count, cancel and post are prompts 11 and 12. The stocktake screens are IMS-31.
- Add triggers, grants, columns or dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*StocktakeTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests'
dotnet test --project tests/Architecture.Tests
cd web && npm run lint && npm test -- --watch=false
```
