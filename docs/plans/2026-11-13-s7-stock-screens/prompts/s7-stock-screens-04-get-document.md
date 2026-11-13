---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 04
unit: IMS-11
status: todo
model: sonnet
effort: medium
depends_on: [03]
parallel_group: D
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Documents/DocumentDetailResponse.cs
    - src/Modules/Inventory/Inventory/Features/Documents/GetDocument/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Documents/GetDocument/Handler.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Documents/DocumentQueryEndpoints.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/DocumentQueryTests.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 04 · `GET /documents/{id}`

## Task
Add `GET /api/documents/{id}`: the document with every movement as a line (item, from/to location, quantity, unit cost, total cost) and the reversal link either way. An unknown or foreign id is `404 not-found`. Add its T5 probe and extend `DocumentQueryTests`.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11`. Prompt 03 created `Features/Documents/DocumentQueryEndpoints.cs` (group `/documents`, `MapListDocuments()`), `DocumentSummaryResponse` and `tests/Inventory.IntegrationTests/DocumentQueryTests.cs`.
- Copy the get pattern from `Features/Items/GetItem/Endpoint.cs:9-12` (`MapGet("/{id:guid}", (Guid id, Handler handler, CancellationToken ct) => handler.HandleAsync(id, ct))`) and `Handler.cs:12-21` (`Results<Ok<…>, ProblemHttpResult>`, `Problems.NotFound()` from `Features/Problems.cs:18`).
- `DocumentDetailResponse.cs` (namespace `Inventory.Features.Documents`):
  ```csharp
  internal sealed record DocumentDetailResponse(Guid Id, string Type, string? ReasonCode, string? Reference, string? Notes,
      DateTimeOffset PostedAt, Guid PostedBy, Guid? ReversesDocumentId, Guid? ReversedByDocumentId, IReadOnlyList<DocumentLineResponse> Lines);
  internal sealed record DocumentLineResponse(long MovementId, int LineNo, Guid ItemId, string Sku, string ItemName, string BaseUnit,
      Guid FromLocationId, string FromLocationCode, string FromLocationName, Guid ToLocationId, string ToLocationCode, string ToLocationName,
      decimal Quantity, decimal? UnitCost, decimal TotalCost, long? ReversesMovementId);
  ```
  Don't name a type ending in `Line` or `Request` with a date property: T10 scans those names.
- `GetDocument/Handler.cs` (`GetDocumentHandler(InventoryDbContext db)`):
  - Load the document `AsNoTracking()` by `Id`. If it's missing, return `Problems.NotFound()`. Another tenant's id is invisible through the named filter and RLS, so it is `404` too (spec §8).
  - `ReversedByDocumentId`: `db.StockDocuments.Where(r => r.ReversesDocumentId == id).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct)`.
  - Lines: `db.StockMovements.Where(m => m.DocumentId == id)` joined to `db.Items` (on `ItemId`), to `db.Locations` as from (on `FromLocationId`) and to `db.Locations` as to (on `ToLocationId`), ordered by `LineNo`. Include every movement, revaluations (`LineNo >= 1000`, S10) too.
  - `UnitCost = Quantity > 0 ? CostingPolicy.Round4(TotalCost / Quantity) : null`, computed in C# after reading (`Inventory.Domain.CostingPolicy.Round4`, `Domain/CostingPolicy.cs:7`). Never divide in SQL.
  - `Type`/`ReasonCode` as `.ToString()` of the enums (`ReasonCode?.ToString()`), as in `Features/Posting/DocumentResponse.cs:21-31`.
  - Never call `IgnoreQueryFilters`.
- `GetDocument/Endpoint.cs`: `MapGetDocument(this RouteGroupBuilder documents)` → `documents.MapGet("/{id:guid}", …)`. Any signed-in role.
- `DocumentQueryEndpoints.cs`: add `documents.MapGetDocument();`.
- `InventoryModuleExtensions.cs`: `AddScoped<GetDocumentHandler>()`.
- The posting routes already return `Created($"/api/documents/{id}")` (`Features/Posting/DocumentPoster.cs:157`). That forward reference from S5c now resolves.
- **T5.** In `TenantIsolationTests.cs`, add next to `Another_tenants_item_id_is_404` (`:152-168`, copy it):
  ```
  [Fact]
  [ProbesRoute("GET /api/documents/{id:guid}")]
  public async Task Another_tenants_document_id_is_404()
  ```
  Tenant B (Admin) posts a receipt through the API. Tenant A's `GET /api/documents/<B's id>` gives `404` with `type` `/problems/not-found`.
- **`DocumentQueryTests.cs`**: add these facts, reusing the helpers that file already uses (`PostingTests.*`, `TransferTests.Transfers/Transfer/TransferLine`, `AdjustmentTests.Adjustments/Adjustment/Delta`, `LedgerRows.OpenAsync/AddDocumentAsync`). Compare decimals with `GetValue<decimal>()`.
  1. `Location_header_of_a_receipt_resolves`: post a receipt of 10 @ 100.0000 at `WH-1`, then `GET` the `201`'s `Location` header (`response.Headers.Location`). It gives `200` with the same `id`, `type` `"Receipt"`, and one line: `sku`, `fromLocationCode` `"SUP"`, `toLocationCode` `"WH-1"`, `quantity` 10, `unitCost` 100.0000, `totalCost` 1000.0000.
  2. `Transfer_line_shows_from_and_to`: receive at A, transfer 4 to B. The detail line has `fromLocationCode` A and `toLocationCode` B, and `totalCost` = 4 × the average.
  3. `Adjustment_lines_show_direction`: an Admin adjustment `Found` with `Delta(item, +2m, 5m)` and, on another item that has stock, `Delta(item2, -1m)`. Line 1 goes `ADJ` → location; line 2 goes location → `ADJ`. `reasonCode` is `"Found"`.
  4. `Reversal_links_point_both_ways_on_detail`: as in prompt 03's list fact, with a `LedgerRows` reversal row. The original's `reversedByDocumentId` is the reversal, and the reversal's `reversesDocumentId` is the original.
  5. `Unknown_document_is_404`: `GET /api/documents/<Guid.NewGuid()>` gives `404 not-found`.
  6. `Staff_can_read_a_document` → `200`.

## Do not
- Touch `Features/Posting/**`, `Persistence/**` or `Domain/**`.
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*DocumentQueryTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests'
dotnet test
```
- The 6 new facts and the T5 route guard pass, and every pre-existing test passes unmodified.
