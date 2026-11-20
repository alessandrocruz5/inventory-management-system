---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 01
unit: IMS-33
status: todo
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory.Contracts/IInventoryReader.cs
    - src/Modules/Inventory/Inventory.Contracts/InventoryReadModels.cs
    - src/Modules/Inventory/Inventory/InventoryReader.cs
  modify:
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
  delete: []
---
# 01 · Inventory read contract and reader

## Task
Add a read-only contract, `IInventoryReader`, to `Inventory.Contracts` with snapshot records, and implement it inside the Inventory module by **reusing the existing read handlers**, so another module (the Assistant, later in this sprint) sees exactly what the screens show. Register it. No endpoint, no schema change.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Why: spec §4, "Later modules (packs, AI) plug in beside Inventory and call it only through `Inventory.Contracts`." Today the contract is only `IInventoryModule.ProvisionTenantAsync` (`src/Modules/Inventory/Inventory.Contracts/IInventoryModule.cs`), a write. Plan D7, D10.
- Locked rules (TASKS.md Downstream IMS-2/IMS-4):
  - `Inventory.Contracts` references nothing except Kernel and other contracts; T12 `Contracts_reference_only_Kernel_and_other_contracts` checks this. Contract types are `public`; everything in `Inventory` stays `internal`.
  - `Inventory` exposes exactly one public type, `InventoryModuleExtensions`.
  - Querying a tenant table needs a tenant scope. The handlers run in the request's DI scope, where `TenantMiddleware` has set the tenant, and the named `Tenant` filter plus RLS apply.
  - Raw SQL only through EF. Never call `IgnoreQueryFilters()`.
- Money and quantities are `decimal`. Never `double`.
- **D10: no user identity in any contract type.** Drop `PostedBy`, `CreatedBy`, `Version`/rowversion and any email. The handlers' response records carry `PostedBy` (`Features/Items/ItemMovementResponse.cs`, `Features/Documents/DocumentDetailResponse.cs`, `DocumentSummaryResponse.cs`); map field by field and leave it out.
- Handlers to reuse. They are scoped and already registered at `InventoryModuleExtensions.cs:52-98`. Each returns `Results<…>`; unwrap with `result.Result switch { Ok<T> ok => ok.Value!, … }`.
  - `ListItemsHandler.HandleAsync(ListItemsRequest(string? Search, bool? Active, int? Page, int? PageSize), ct)` → `Results<Ok<PagedResponse<ItemResponse>>, ValidationProblem>`. `ItemResponse(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive, byte[] Version)`. `Active: null` means all; `true` means active only.
  - `GetItemHandler.HandleAsync(Guid id, ct)` → `Results<Ok<ItemDetailResponse>, ProblemHttpResult>` (404 → null). `ItemDetailResponse(..., IReadOnlyList<ItemBalanceResponse> Balances, IReadOnlyList<ItemUnitResponse> Units)`. `ItemBalanceResponse(Guid LocationId, string LocationCode, string LocationName, decimal OnHand, decimal StockValue, decimal? AverageCost, decimal? LastUnitCost)`. `ItemUnitResponse(string Code, decimal Factor, bool IsActive)`.
  - `ListLocationsHandler.HandleAsync(ListLocationsRequest(int? Page, int? PageSize), ct)` (read its signature in `Features/Locations/ListLocations/Handler.cs`). `LocationResponse(Guid Id, string Code, string Name, string Kind, bool IsSystem, bool AllowNegativeStock, bool IsActive, byte[] Version)`. Read every page (100 at a time) until done.
  - `ListStockHandler.HandleAsync(ListStockRequest(Guid? ItemId, Guid? LocationId, int? Page, int? PageSize), ct)` → `Results<Ok<StockPageResponse>, ValidationProblem>`. `StockPageResponse(Items, Page, PageSize, Total, StockTotals(decimal StockValue))`. `StockRowResponse(ItemId, Sku, ItemName, BaseUnit, LocationId, LocationCode, LocationName, OnHand, StockValue, AverageCost, LastUnitCost)`. Physical locations only.
  - `ListItemMovementsHandler.HandleAsync(Guid id, ListItemMovementsRequest(int? Page, int? PageSize), ct)` → `Results<Ok<PagedResponse<ItemMovementResponse>>, ValidationProblem, ProblemHttpResult>` (404 → null), newest first. `ItemMovementResponse(long MovementId, Guid DocumentId, string DocumentType, int LineNo, DateTimeOffset PostedAt, Guid PostedBy, string Direction, Guid LocationId, string LocationCode, string LocationName, Guid CounterpartLocationId, string CounterpartLocationCode, string CounterpartLocationName, decimal QuantityChange, decimal ValueChange, decimal RunningOnHand, decimal RunningValue)`.
  - `ListDocumentsHandler.HandleAsync(ListDocumentsRequest(string? Type, DateTimeOffset? PostedFrom, DateTimeOffset? PostedTo, int? Page, int? PageSize), ct)` → `Results<Ok<PagedResponse<DocumentSummaryResponse>>, ValidationProblem>`. Type names are exact and ordinal: `Receipt`, `Issue`, `Transfer`, `Adjustment`, `Reversal`. `DocumentSummaryResponse(Guid Id, string Type, string? ReasonCode, string? Reference, DateTimeOffset PostedAt, Guid PostedBy, int LineCount, Guid? ReversesDocumentId, Guid? ReversedByDocumentId)`.
  - `GetDocumentHandler.HandleAsync(Guid id, ct)` → `Results<Ok<DocumentDetailResponse>, ProblemHttpResult>` (404 → null). `DocumentLineResponse(long MovementId, int LineNo, Guid ItemId, string Sku, string ItemName, string BaseUnit, Guid FromLocationId, string FromLocationCode, string FromLocationName, Guid ToLocationId, string ToLocationCode, string ToLocationName, decimal Quantity, decimal? UnitCost, decimal TotalCost, long? ReversesMovementId)`.
  - `GetLedgerHealthHandler.HandleAsync(ct)` → `Ok<LedgerHealthResponse>`. `LedgerHealthResponse(DateTimeOffset CheckedAt, bool Healthy, IReadOnlyList<InvariantResult> Invariants)`, `InvariantResult(string Id, string Name, bool Passed, int FailureCount, …)`. The route is Admin-only (`GetLedgerHealth/Endpoint.cs:9`). The reader **doesn't** enforce roles: the caller does (plan D11). Say so in the XML doc.
- `ValidationProblem` from a handler: throw `ArgumentException(message, paramName)`, where `paramName` is the first error key and `message` is its first message (`ValidationProblem.ProblemDetails.Errors`). Throw `ArgumentOutOfRangeException` up front when `max` is outside 1..100.

### The contract (copy these names)
`IInventoryReader.cs`, namespace `Inventory.Contracts`, every method documented ("tenant-scoped to the current request; a foreign id reads as missing"):
```csharp
public interface IInventoryReader
{
    Task<ItemList> SearchItemsAsync(string? search, bool includeInactive, int max, CancellationToken cancellationToken);
    Task<ItemSnapshot?> GetItemAsync(Guid itemId, CancellationToken cancellationToken);
    Task<ItemSnapshot?> GetItemBySkuAsync(string sku, CancellationToken cancellationToken);
    Task<IReadOnlyList<LocationSummary>> ListLocationsAsync(CancellationToken cancellationToken);
    Task<StockSnapshot> GetStockAsync(Guid? itemId, Guid? locationId, int max, CancellationToken cancellationToken);
    Task<MovementList?> GetItemMovementsAsync(Guid itemId, int max, CancellationToken cancellationToken);
    Task<DocumentList> ListDocumentsAsync(string? type, DateTimeOffset? postedFrom, DateTimeOffset? postedTo, int max, CancellationToken cancellationToken);
    Task<DocumentSnapshot?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken);
    Task<LedgerHealthSnapshot> GetLedgerHealthAsync(CancellationToken cancellationToken);
}
```
`InventoryReadModels.cs`: `public sealed record`s:
- `ItemList(IReadOnlyList<ItemSummary> Items, int Total)`
- `ItemSummary(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive)`
- `ItemSnapshot(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive, IReadOnlyList<ItemBalanceSnapshot> Balances, IReadOnlyList<ItemUnitSnapshot> Units)`
- `ItemBalanceSnapshot(Guid LocationId, string LocationCode, string LocationName, decimal OnHand, decimal StockValue, decimal? AverageCost, decimal? LastUnitCost)`
- `ItemUnitSnapshot(string Code, decimal Factor, bool IsActive)`
- `LocationSummary(Guid Id, string Code, string Name, string Kind, bool IsPhysical, bool AllowNegativeStock, bool IsActive)`, where `IsPhysical = !IsSystem`
- `StockSnapshot(IReadOnlyList<StockRowSnapshot> Rows, int Total, decimal TotalStockValue)`
- `StockRowSnapshot(Guid ItemId, string Sku, string ItemName, string BaseUnit, Guid LocationId, string LocationCode, string LocationName, decimal OnHand, decimal StockValue, decimal? AverageCost, decimal? LastUnitCost)`
- `MovementList(IReadOnlyList<MovementSnapshot> Movements, int Total)`
- `MovementSnapshot(long MovementId, Guid DocumentId, string DocumentType, DateTimeOffset PostedAt, string Direction, string LocationCode, string LocationName, string CounterpartLocationCode, string CounterpartLocationName, decimal QuantityChange, decimal ValueChange, decimal RunningOnHand, decimal RunningValue)`
- `DocumentList(IReadOnlyList<DocumentSummarySnapshot> Documents, int Total)`
- `DocumentSummarySnapshot(Guid Id, string Type, string? ReasonCode, string? Reference, DateTimeOffset PostedAt, int LineCount, Guid? ReversesDocumentId, Guid? ReversedByDocumentId)`
- `DocumentSnapshot(Guid Id, string Type, string? ReasonCode, string? Reference, string? Notes, DateTimeOffset PostedAt, Guid? ReversesDocumentId, Guid? ReversedByDocumentId, IReadOnlyList<DocumentLineSnapshot> Lines)`
- `DocumentLineSnapshot(int LineNo, Guid ItemId, string Sku, string ItemName, string BaseUnit, string FromLocationCode, string FromLocationName, string ToLocationCode, string ToLocationName, decimal Quantity, decimal? UnitCost, decimal TotalCost)`
- `LedgerHealthSnapshot(DateTimeOffset CheckedAt, bool Healthy, IReadOnlyList<InvariantSnapshot> Invariants)`
- `InvariantSnapshot(string Id, string Name, bool Passed, int FailureCount)`

### The implementation
- `src/Modules/Inventory/Inventory/InventoryReader.cs`: `internal sealed class InventoryReader(...) : IInventoryReader`. Inject the handlers plus `InventoryDbContext` (for `GetItemBySkuAsync` only).
- Pass `Page: 1, PageSize: max` to the paged handlers. `includeInactive: false` maps to `Active: true`; `true` maps to `Active: null`.
- `GetItemBySkuAsync`: `db.Items.AsNoTracking().Where(i => i.Sku == sku).Select(i => (Guid?)i.Id).SingleOrDefaultAsync(ct)` (the tenant filter applies; SKUs are unique per tenant; the column collation is case-insensitive), then `GetItemAsync`. A blank `sku` returns null.
- Copy the class style of `InventoryModule.cs` (primary constructor, XML summary).
- Register it in `AddInventoryModule`: `builder.Services.AddScoped<IInventoryReader, InventoryReader>();` next to `IInventoryModule`.

## Do not
- Touch files outside the `files` list, or change any handler, response record or route. If you must, stop and report why.
- Expose `PostedBy`, `CreatedBy`, `Version`, emails or user ids in any contract type.
- Enforce roles in the reader, or add caching, abstractions or config.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Architecture.Tests
dotnet test
```
- `grep -n 'PostedBy\|CreatedBy\|Email\|Version' src/Modules/Inventory/Inventory.Contracts/InventoryReadModels.cs` prints nothing.
