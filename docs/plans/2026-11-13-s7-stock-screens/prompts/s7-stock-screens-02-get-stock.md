---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 02
unit: IMS-11
status: todo
model: sonnet
effort: medium
depends_on: [01]
parallel_group: B
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Stock/StockEndpoints.cs
    - src/Modules/Inventory/Inventory/Features/Stock/StockResponse.cs
    - src/Modules/Inventory/Inventory/Features/Stock/ListStock/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Stock/ListStock/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Stock/ListStock/Request.cs
    - tests/Inventory.IntegrationTests/StockTests.cs
  modify:
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 02 · `GET /stock` with totals

## Task
Add `GET /api/stock`. It returns the balances by item × **physical** location, with average cost and value, filtered by `itemId` and/or `locationId`, paged, plus `totals.stockValue` for the whole filter (spec §9). Register it, add its T5 probe, and add `StockTests`.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11`. .NET 10 minimal APIs. Module internals are `internal`. `TreatWarningsAsErrors` is on. Quantities and money are `decimal`; rounding is `MidpointRounding.AwayFromZero` to 4 dp.
- Copy the list pattern exactly from `src/Modules/Inventory/Inventory/Features/Items/ListItems/`:
  - `Endpoint.cs:7-13`: `items.MapGet("", ([AsParameters] ListItemsRequest request, ListItemsHandler handler, CancellationToken ct) => handler.HandleAsync(request, ct))`.
  - `Handler.cs:9-46`: `Paging.Resolve(request.Page, request.PageSize, errors)`, then `Problems.Validation(errors)`, then the `AsNoTracking()` query. Return type `Results<Ok<…>, ValidationProblem>`.
  - `Request.cs`: a positional `internal sealed record`.
  - `Features/Items/ItemEndpoints.cs:15-24` for a group class with a `Map…Endpoints(this IEndpointRouteBuilder api)` extension and `api.MapGroup("/items")`.
  - `Features/Paging.cs`: `PagedResponse<T>(Items, Page, PageSize, Total)`, `Paging.Resolve`, `ToPageAsync` (count, then `Skip/Take`; the query must be ordered).
- Files (namespace `Inventory.Features.Stock` / `Inventory.Features.Stock.ListStock`):
  - `ListStock/Request.cs`: `internal sealed record ListStockRequest(Guid? ItemId, Guid? LocationId, int? Page, int? PageSize);`. No date property (T10 scans every `*Request` type).
  - `StockResponse.cs`:
    ```csharp
    internal sealed record StockRowResponse(Guid ItemId, string Sku, string ItemName, string BaseUnit, Guid LocationId, string LocationCode,
        string LocationName, decimal OnHand, decimal StockValue, decimal? AverageCost, decimal? LastUnitCost);
    internal sealed record StockTotals(decimal StockValue);
    internal sealed record StockPageResponse(IReadOnlyList<StockRowResponse> Items, int Page, int PageSize, int Total, StockTotals Totals);
    ```
    Give each a one-line `<summary>`, like `DocumentResponse.cs`.
  - `ListStock/Handler.cs` (`ListStockHandler(InventoryDbContext db)`):
    - Query `db.StockBalances.AsNoTracking().Where(b => !b.IsVirtual)`, joined to `db.Items` on `ItemId == Id` and to `db.Locations` on `LocationId == Id`. The named tenant filter and RLS scope all three tables, so joining on `Id` alone is correct. Never call `IgnoreQueryFilters`.
    - Optional filters: `ItemId`, `LocationId`. An unknown or another tenant's id simply matches nothing, which gives a `200` with an empty page and totals 0 (plan D6). Don't validate them against the database.
    - Order by `Item.Sku`, then `Location.Code`.
    - Project to an anonymous or intermediate shape with the raw columns, then `ToPageAsync(page, pageSize, ct)`. After the page is read, map each row in C#. `AverageCost = OnHand > 0 ? CostingPolicy.Round4(StockValue / OnHand) : null`. `CostingPolicy` is `Inventory.Domain.CostingPolicy`, `public static decimal Round4(decimal)` at `Domain/CostingPolicy.cs:7`. Never divide in SQL.
    - Totals: a second statement over the same filtered query, `await filtered.SumAsync(r => r.StockValue, ct)` (EF emits `COALESCE(SUM(…), 0)`, so an empty filter gives 0). Don't total quantity: units differ across items (plan D6).
    - Return `TypedResults.Ok(new StockPageResponse(rows, page.Page, page.PageSize, page.Total, new StockTotals(total)))`.
  - `ListStock/Endpoint.cs`: `MapListStock(this RouteGroupBuilder stock)` → `stock.MapGet("", …)`. No role policy: any signed-in user (§9). The `/api` group already requires sign-in.
  - `StockEndpoints.cs`: `internal static class StockEndpoints { public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder api) { var stock = api.MapGroup("/stock"); stock.MapListStock(); return api; } }`.
- `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs`: add `builder.Services.AddScoped<ListStockHandler>();` after the last `AddScoped` (`:59`), and `endpoints.MapStockEndpoints();` after `MapDocumentEndpoints()` (`:67`). Add the `using`s.
- **T5 (route guard).** `tests/Inventory.IntegrationTests/TenantIsolationTests.cs:521-545` fails unless every `/api` route has a `[ProbesRoute("<METHOD> <RawText>")]` test. A `""` route's raw text has a trailing slash, so the key is `GET /api/stock/`. Add, next to `Each_tenant_lists_only_its_own_items` (`:127-150`, copy its shape):
  ```
  [Fact]
  [ProbesRoute("GET /api/stock/")]
  public async Task Each_tenant_lists_only_its_own_stock()
  ```
  Create two tenants with Admin users (`DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin)` + `DatabaseFixture.SignInAsync`). Each creates a location and item (`PostingTests.NewLocationAsync`, `PostingTests.NewItemAsync`) and receives stock (`PostingTests.ReceiveAsync(client, location, item, 5m, 10m, ct)`). Each tenant's `GET /api/stock` returns exactly its own row (`itemId`), and `total` is 1. Then tenant A's `GET /api/stock?locationId=<B's location>` and `?itemId=<B's item>` each return `items: []`, `total` 0 and `totals.stockValue` 0.
- **`tests/Inventory.IntegrationTests/StockTests.cs`** (new; `public sealed class StockTests(DatabaseFixture db)`, xUnit v3 + Shouldly, `TestContext.Current.CancellationToken`, `await using var app = db.CreateApp();`, read JSON as `JsonNode` as `PostingTests` does). Every test creates its own tenant (no reset; the ledger refuses `DELETE`). Use these helpers: `PostingTests.PostDocumentAsync`, `PostingTests.Receipts`, `PostingTests.Issues`, `PostingTests.Receipt`, `PostingTests.Line`, `PostingTests.Issue`, `PostingTests.IssueLine`, `PostingTests.NewKey`, `PostingTests.ReceiveAsync`, `PostingTests.NewLocationAsync(client, ct, code)`, `PostingTests.NewItemAsync(client, sku, ct)`, `PostingTests.AddUserAsync(app, tenant, role)` (returns `(Guid Id, string Email)`), `PostingTests.AssertValidationAsync(response, key, ct)`. Compare decimals as numbers (`GetValue<decimal>()`), never as JSON text (IMS-8: `1` vs `1.0000`). Facts:
  1. `Average_cost_is_value_over_on_hand_rounded_to_4dp`: at A, receive 10 @ 100.0000, then 3 @ 33.3333. The row has `onHand` 13, `stockValue` 1099.9999, `averageCost` 84.6154 and `lastUnitCost` non-null, plus `sku`, `itemName`, `baseUnit`, `locationCode` and `locationName`.
  2. `Zero_balance_row_has_no_average_cost`: receive 2 @ 5, issue 2. The row shows `onHand` 0, `stockValue` 0 and `averageCost` null.
  3. `Virtual_locations_never_appear`: after a receipt and an issue, every row's `locationCode` is the physical one (never `SUP`/`CUS`/`ADJ`), and `total` equals the number of physical rows.
  4. `Totals_cover_the_whole_filter_not_the_page`: 3 items at A with distinct values. `?pageSize=1` gives one item, `total` 3, and `totals.stockValue` = the sum of all three. `?page=2&pageSize=1` gives the same totals.
  5. `Filters_narrow_rows_and_totals`: 2 items × 2 locations. `?locationId=A` gives only A's rows, and totals = A's sum. `?itemId=I` gives only I's rows. Both filters together give one row.
  6. `Rows_are_ordered_by_sku_then_location_code`.
  7. `Paging_is_validated`: `?pageSize=101` → `400` on `pageSize`; `?page=0` → `400` on `page`.
  8. `Staff_can_read_stock`: a Staff user (`PostingTests.AddUserAsync(app, tenant, Roles.Staff)` then sign in) gets `200`.
- Known flake: if Testcontainers crashes on start, rerun with `-- --max-parallel-test-modules 1`.

## Do not
- Touch `Features/Posting/**`, `Persistence/**` or `Domain/**` (high-stakes). You only *call* `CostingPolicy.Round4`.
- Return virtual balances, compute averages in SQL, or use `double`/`float`.
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*StockTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests'
dotnet test
```
- The T5 route guard (`TenantIsolationTests`) is green with the new probe; the 8 `StockTests` facts pass.
