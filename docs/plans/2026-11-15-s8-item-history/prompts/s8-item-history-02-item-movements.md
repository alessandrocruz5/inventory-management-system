---
type: prompt
plan: "[[2026-11-15-s8-item-history]]"
id: 02
unit: IMS-12
status: todo
model: sonnet
effort: high
depends_on: [01]
parallel_group: B
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Items/ItemMovementResponse.cs
    - src/Modules/Inventory/Inventory/Features/Items/ListItemMovements/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Items/ListItemMovements/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Items/ListItemMovements/Request.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Items/ItemEndpoints.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/ItemHistoryTests.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 02 · `GET /items/{id}/movements` with running balance

## Task
Add `GET /api/items/{id}/movements`: the item's ledger history as a **stock card**, newest first and paged. Each row is one movement at one **physical** location it touches, with signed quantity and value changes and the running on-hand and running value at that location, computed by a window `SUM() OVER` across the whole history (spec §9). An unknown or foreign item id is `404 not-found`. Add the T5 probe and extend `ItemHistoryTests`.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-12`. Prompt 01 created `Features/Items/ItemDetailResponse.cs` and `tests/Inventory.IntegrationTests/ItemHistoryTests.cs` with `PostVAsync(client, upTo, ct)` (V1–V5 or V1–V6 at `LOC-A`/`LOC-B`, item `V-ITEM`; returns `(A, B, Item, Documents[])`) and `GetJsonAsync`.
- Conventions: `decimal` only; module types `internal`; endpoints follow `Features/<Area>/<Endpoint>/{Endpoint,Handler,Request}.cs` with plain scoped handlers. Raw SQL is allowed for reports, **always through EF Core** (`db.Database.SqlQuery`), never a connection opened another way (it would have no session context). `IgnoreQueryFilters()` is banned.
- **Shape (plan D1/D2).** A receipt at A gives one `In` row at A (counterpart `SUP`); an issue one `Out` row (counterpart `CUS`); a transfer A→B gives `Out` at A and `In` at B (one movement, two rows); an adjustment one row against `ADJ`; a revaluation (quantity 0, `LineNo` 1000 + n, S10) a row with `quantityChange` 0. Virtual locations (`Kind <> 'Physical'`) never get rows. `quantityChange`/`valueChange` are `+Quantity`/`+TotalCost` on the `In` side and negated on the `Out` side. Running sums partition by location, ordered by movement id.
- **Create `Features/Items/ItemMovementResponse.cs`** (namespace `Inventory.Features.Items`):
  ```csharp
  /// <summary>One movement at one physical location, with the running balance there after it (spec §9 stock card).</summary>
  internal sealed record ItemMovementResponse(long MovementId, Guid DocumentId, string DocumentType, int LineNo,
      DateTimeOffset PostedAt, Guid PostedBy, string Direction,
      Guid LocationId, string LocationCode, string LocationName,
      Guid CounterpartLocationId, string CounterpartLocationCode, string CounterpartLocationName,
      decimal QuantityChange, decimal ValueChange, decimal RunningOnHand, decimal RunningValue);
  ```
- **Create `ListItemMovements/Request.cs`**: `internal sealed record ListItemMovementsRequest(int? Page, int? PageSize);` (no date property: T10 scans every `*Request`/`*Line` type under `Inventory.Features`).
- **Create `ListItemMovements/Endpoint.cs`**, copying `Features/Documents/ListDocuments/Endpoint.cs:7-12` and `Features/Items/GetItem/Endpoint.cs:9-12`:
  `public static RouteHandlerBuilder MapListItemMovements(this RouteGroupBuilder items) => items.MapGet("/{id:guid}/movements", (Guid id, [AsParameters] ListItemMovementsRequest request, ListItemMovementsHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(id, request, cancellationToken));`. Any signed-in role.
- **Create `ListItemMovements/Handler.cs`**: `internal sealed class ListItemMovementsHandler(InventoryDbContext db, ITenantContext tenant)` (`Kernel.Tenancy.ITenantContext`, injected as `PostingEngine` does at `Persistence/PostingEngine.cs:19`). `HandleAsync(Guid id, ListItemMovementsRequest request, CancellationToken cancellationToken)` returns `Task<Results<Ok<PagedResponse<ItemMovementResponse>>, ValidationProblem, ProblemHttpResult>>`.
  1. `Paging.Resolve(request.Page, request.PageSize, errors)`; on errors return `Problems.Validation(errors)` (`Features/Paging.cs:14`, `Features/Problems.cs:15`).
  2. `if (!await db.Items.AnyAsync(i => i.Id == id, cancellationToken)) return Problems.NotFound();` — through EF, so the named tenant filter applies; a foreign id is invisible, hence `404` (spec §8).
  3. A private row type for materialization. Its name must **not** end in `Line` or `Request` (T10):
     ```csharp
     private sealed class ItemMovementRow
     {
         public long MovementId { get; init; }  public Guid DocumentId { get; init; }  public string DocumentType { get; init; } = "";
         public int LineNo { get; init; }  public DateTimeOffset PostedAt { get; init; }  public Guid PostedBy { get; init; }
         public string Direction { get; init; } = "";  public Guid LocationId { get; init; }  public string LocationCode { get; init; } = "";
         public string LocationName { get; init; } = "";  public Guid CounterpartLocationId { get; init; }
         public string CounterpartLocationCode { get; init; } = "";  public string CounterpartLocationName { get; init; } = "";
         public decimal QuantityChange { get; init; }  public decimal ValueChange { get; init; }
         public decimal RunningOnHand { get; init; }  public decimal RunningValue { get; init; }
     }
     ```
     (One property per line in the real file; `dotnet format` decides the layout.) If EF can't materialize a `private` nested type, make it `internal sealed` at namespace level in `Handler.cs`.
  4. The windowed query, as an interpolated `FormattableString` so `{tenantId}` and `{id}` become parameters (`var tenantId = tenant.TenantId;`):
     ```csharp
     var rows = db.Database.SqlQuery<ItemMovementRow>($"""
         SELECT e.MovementId, e.DocumentId, d.[Type] AS DocumentType, e.[LineNo], e.PostedAt, e.PostedBy, e.Direction,
                e.LocationId, l.Code AS LocationCode, l.Name AS LocationName,
                e.CounterpartLocationId, c.Code AS CounterpartLocationCode, c.Name AS CounterpartLocationName,
                e.QuantityChange, e.ValueChange,
                SUM(e.QuantityChange) OVER (PARTITION BY e.LocationId ORDER BY e.MovementId ROWS UNBOUNDED PRECEDING) AS RunningOnHand,
                SUM(e.ValueChange) OVER (PARTITION BY e.LocationId ORDER BY e.MovementId ROWS UNBOUNDED PRECEDING) AS RunningValue
         FROM (
             SELECT m.Id AS MovementId, m.DocumentId, m.[LineNo], m.PostedAt, m.PostedBy, 'In' AS Direction,
                    m.ToLocationId AS LocationId, m.FromLocationId AS CounterpartLocationId,
                    m.Quantity AS QuantityChange, m.TotalCost AS ValueChange
             FROM [inventory].[StockMovements] AS m
             WHERE m.TenantId = {tenantId} AND m.ItemId = {id}
             UNION ALL
             SELECT m.Id, m.DocumentId, m.[LineNo], m.PostedAt, m.PostedBy, 'Out',
                    m.FromLocationId, m.ToLocationId, -m.Quantity, -m.TotalCost
             FROM [inventory].[StockMovements] AS m
             WHERE m.TenantId = {tenantId} AND m.ItemId = {id}
         ) AS e
         JOIN [inventory].[Locations] AS l ON l.TenantId = {tenantId} AND l.Id = e.LocationId AND l.Kind = 'Physical'
         JOIN [inventory].[Locations] AS c ON c.TenantId = {tenantId} AND c.Id = e.CounterpartLocationId
         JOIN [inventory].[StockDocuments] AS d ON d.TenantId = {tenantId} AND d.Id = e.DocumentId
         """);
     ```
     Rules, with reasons (put a short comment above the SQL):
     - **No `ORDER BY`, no CTE (`WITH`), no trailing `;`.** EF wraps `SqlQuery` in a derived table to compose the LINQ below; any of those three fails there (IMS-6 note on derived tables).
     - **`TenantId = {tenantId}` on every table.** EF's named `Tenant` filter does not apply to `SqlQuery` on an unmapped type. The explicit predicate is the second isolation layer (RLS through the session-context interceptor is underneath), and it lets the optimizer seek `IX_Movements_Item (TenantId, ItemId, Id)`. Add no index or hint.
     - `[LineNo]` and `[Type]` are bracketed (reserved words). `Kind` and `Type` are stored as their enum names (`InventoryDbContext.cs:52,95`), so `'Physical'` and the returned `DocumentType` are strings.
     - The window runs inside the derived table, before paging, so a later page's running values are cumulative over the whole history. A movement never has `From = To` (CHECK), so `MovementId` is unique per partition.
  5. Compose and page: `var result = await rows.OrderByDescending(r => r.MovementId).ThenBy(r => r.Direction).ToPageAsync(page, pageSize, cancellationToken);` (`Features/Paging.cs:32`: count, then one page). `ThenBy(Direction)` puts a transfer's `In` row above its `Out` row and makes paging deterministic.
  6. Map each row to `ItemMovementResponse` and return `TypedResults.Ok(new PagedResponse<ItemMovementResponse>([.. …], result.Page, result.PageSize, result.Total))`, as `Features/Documents/ListDocuments/Handler.cs:81-87` does.
- **`ItemEndpoints.cs:17-22`**: add `items.MapListItemMovements();` after `items.MapGetItem();` and `using Inventory.Features.Items.ListItemMovements;`.
- **`InventoryModuleExtensions.cs:52`**: add `builder.Services.AddScoped<ListItemMovementsHandler>();` after `GetItemHandler`, plus its `using`.
- **T5.** In `TenantIsolationTests.cs`, add next to `Another_tenants_item_id_is_404` (`:222-239`; copy it):
  ```csharp
  [Fact]
  [ProbesRoute("GET /api/items/{id:guid}/movements")]
  public async Task Another_tenants_item_history_is_404()
  ```
  Two Admin tenants, each signed in. Tenant B creates a location and an item (`PostingTests.NewLocationAsync`, `NewItemAsync`) and receives 1 @ 10 (`PostingTests.ReceiveAsync`). Tenant A's `GET /api/items/<B's item>/movements` is `404` with `type` `/problems/not-found`. Tenant A then creates its own item with the **same SKU**, receives 2 @ 10 at its own location, and its history has `total` 1 with that row's `locationId` equal to A's location (no B rows leak).
- **`ItemHistoryTests.cs`**: add these facts. Use a private `MovementsAsync(client, item, query, ct)` that GETs `/api/items/{item}/movements{query}`, asserts `200` and returns the `JsonNode`. Compare decimals with `GetValue<decimal>()`.
  1. `Running_balance_matches_V1_to_V6`: Admin, `PostVAsync(client, 6, ct)`, then `?pageSize=100`. `total` 7 and exactly these rows in order — `(locationCode, direction, quantityChange, valueChange, runningOnHand, runningValue, counterpartLocationCode, documentId)`:
     `LOC-B In 6 750 10 1230 SUP V6` · `LOC-B In 4 480 4 480 LOC-A V5` · `LOC-A Out −4 −480 6 720 LOC-B V5` · `LOC-A In 5 650 10 1200 SUP V4` · `LOC-A Out −15 −1650 5 550 CUS V3` · `LOC-A In 10 1200 20 2200 SUP V2` · `LOC-A In 10 1000 10 1000 SUP V1`. `documentType` is `Receipt`/`Transfer`/`Issue` accordingly. These are spec §7's V1–V6 balances.
  2. `Newest_row_per_location_equals_the_balance`: after `PostVAsync(client, 6, ct)`, the first row for each `locationId` has `runningOnHand`/`runningValue` equal to that location's `onHand`/`stockValue` in `GET /api/items/{item}` `balances` (prompt 01).
  3. `Running_values_span_the_whole_history_across_pages`: after V1–V6, `?page=2&pageSize=2` has `total` 7, `page` 2 and two rows equal to rows 3 and 4 of fact 1 (`LOC-A Out … 6 720` and `LOC-A In … 10 1200`).
  4. `Revaluation_row_moves_value_only`: a new item `REVAL-1` at location `LOC-R`; receive 7 @ 56 through the API. Then insert a revaluation as `inventory_app` with `LedgerRows` (`tests/Inventory.IntegrationTests/LedgerRows.cs:20,32,64,81`): `await using var c = await LedgerRows.OpenAsync(db, tenant, ct);` `var adj = await LedgerRows.LocationIdAsync(c, "ADJ", ct);` `var doc = await LedgerRows.AddDocumentAsync(c, ct);` `await LedgerRows.AddMovementAsync(c, doc, 1001, item, locR, adj, 0m, 18m, ct);`. The history's first row is `LOC-R Out`, `quantityChange` 0, `valueChange` −18, `runningOnHand` 7, `runningValue` 374, counterpart `ADJ`; `total` 2. (This tenant's balances are now out of step with the ledger; the tenant is the test's own, spec §11 test isolation.)
  5. `Item_without_movements_has_an_empty_history`: a new item → `total` 0, empty `items`.
  6. `Unknown_item_history_is_404`: `GET /api/items/<Guid.NewGuid()>/movements` → `404` with `type` `/problems/not-found`.
  7. `History_page_size_over_100_is_400`: `?pageSize=101` on an existing item → `400` with `errors.pageSize` (`PostingTests.AssertValidationAsync(response, "pageSize", ct)`, `PostingTests.cs:660`).
  8. `Staff_can_read_item_history`: a Staff user of the same tenant (`PostingTests.AddUserAsync(app, tenant, Roles.Staff)`) gets `200`.

## Do not
- Touch `Features/Posting/**`, `Persistence/**`, `Domain/**`, or any migration. Add no index, hint, column or package.
- Use `IgnoreQueryFilters`, `FromSql` on a `DbSet`, or a `SqlConnection` outside the tests.
- Compute averages or divide in SQL.
- Touch files outside the `files` list. If you must, stop and report why.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*ItemHistoryTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests' --filter-class '*ItemsTests'
dotnet test            # if the Testcontainers start-up flake shows: dotnet test -- --max-parallel-test-modules 1
```
- The 8 new `ItemHistoryTests` facts, the new T5 probe and the T5 route guard pass; T10 (`PostingClockTests`) passes unmodified; every pre-existing test passes unmodified.
