---
type: prompt
plan: "[[2026-11-15-s8-item-history]]"
id: 01
unit: IMS-12
status: done
model: sonnet
effort: medium
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Items/ItemDetailResponse.cs
    - tests/Inventory.IntegrationTests/ItemHistoryTests.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Items/GetItem/Handler.cs
  delete: []
---
# 01 · Balances in `GET /items/{id}`

## Task
Make `GET /api/items/{id}` return the item's existing fields plus `balances`: its balance at each **physical** location where it has a `StockBalances` row (zero rows included), ordered by location code. Create `ItemHistoryTests` with the facts below and a V-set helper that prompt 02 reuses.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-12`. .NET 10, EF Core 10, SQL Server 2025, xUnit v3 + Shouldly. `Nullable` and `TreatWarningsAsErrors` are on. Module internals are `internal`.
- Money and quantities are `decimal`; rounding is `CostingPolicy.Round4` (`src/Modules/Inventory/Inventory/Domain/CostingPolicy.cs:7`, AwayFromZero, 4 dp). Never divide in SQL.
- **Create `Features/Items/ItemDetailResponse.cs`** (namespace `Inventory.Features.Items`):
  ```csharp
  /// <summary>GET /items/{id}: the item plus its balance at each physical location (spec §9).</summary>
  internal sealed record ItemDetailResponse(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive, byte[] Version,
      IReadOnlyList<ItemBalanceResponse> Balances)
  {
      public static ItemDetailResponse From(Item item, IReadOnlyList<ItemBalanceResponse> balances) =>
          new(item.Id, item.Sku, item.Name, item.BaseUnit, item.IsActive, item.RowVersion, balances);
  }

  /// <summary>One physical balance; average cost is derived, never stored (spec §5).</summary>
  internal sealed record ItemBalanceResponse(Guid LocationId, string LocationCode, string LocationName,
      decimal OnHand, decimal StockValue, decimal? AverageCost, decimal? LastUnitCost);
  ```
  `Item` is `Inventory.Persistence.Item`. Do **not** change `Features/Items/ItemResponse.cs`: list, create, update and deactivate keep returning it.
- **Modify `Features/Items/GetItem/Handler.cs`** (currently `:10-21`, returns `Results<Ok<ItemResponse>, ProblemHttpResult>`):
  - Return type becomes `Results<Ok<ItemDetailResponse>, ProblemHttpResult>`. Keep the `404` path (`Problems.NotFound()`) exactly as is. Replace the class summary with "The item with its balance at each physical location (spec §9)."
  - After the item is found, read the balances, copying the join from `Features/Stock/ListStock/Handler.cs:24,35-51`:
    ```csharp
    var rows = await (
        from b in db.StockBalances.AsNoTracking()
        where b.ItemId == id && !b.IsVirtual
        join l in db.Locations on b.LocationId equals l.Id
        orderby l.Code
        select new { b.LocationId, l.Code, l.Name, b.OnHand, b.StockValue, b.LastUnitCost }).ToListAsync(cancellationToken);
    ```
    Map each to `ItemBalanceResponse`, with `AverageCost = r.OnHand > 0 ? CostingPolicy.Round4(r.StockValue / r.OnHand) : null` computed in C# (`using Inventory.Domain;`), as `ListStock/Handler.cs:65` does.
  - Never call `IgnoreQueryFilters`. The named tenant filter and RLS scope all three tables, so joining on `Id` alone is safe (the comment at `ListStock/Handler.cs:23`).
  - `GetItem/Endpoint.cs` needs no change: its lambda returns the handler's result.
- **Create `tests/Inventory.IntegrationTests/ItemHistoryTests.cs`**: `public sealed class ItemHistoryTests(DatabaseFixture db)` in namespace `Inventory.IntegrationTests` (the assembly fixture is wired in `DatabaseFixture.cs:13`). Copy the shape and usings of `DocumentQueryTests.cs:1-36` (`System.Net`, `System.Net.Http.Json`, `System.Text.Json.Nodes`, `Platform.Identity` for `Roles`, `Shouldly`). Use `db.CreateApp()`, `DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin|Roles.Staff)` (returns `(Guid Tenant, string Email)`), `DatabaseFixture.SignInAsync(app, email)` and `TestContext.Current.CancellationToken`. Compare decimals with `GetValue<decimal>()`, never as text (IMS-8: a 201 and a replay format scale differently).
  - Add this helper; prompt 02 reuses it:
    ```csharp
    /// <summary>Spec §7 vector set V through the API, steps V1..upTo (5 or 6). Returns the locations, the item and each step's document id.</summary>
    internal static async Task<(Guid A, Guid B, Guid Item, Guid[] Documents)> PostVAsync(HttpClient client, int upTo, CancellationToken ct)
    ```
    Locations via `PostingTests.NewLocationAsync(client, ct, "LOC-A")` and `"LOC-B"`; item via `PostingTests.NewItemAsync(client, "V-ITEM", ct)`. Steps, each posted with `PostingTests.PostDocumentAsync(client, route, PostingTests.NewKey(), body, ct)`, asserting `201` and reading `id` from the body:
    V1 `Receipts`, `Receipt(A, Line(item, 10m, 100m))`; V2 `Receipt(A, Line(item, 10m, 120m))`; V3 `Issues`, `Issue(A, IssueLine(item, 15m))`; V4 `Receipt(A, Line(item, 5m, 130m))`; V5 `TransferTests.Transfers`, `TransferTests.Transfer(A, B, TransferTests.TransferLine(item, 4m))`; V6 (only when `upTo == 6`) `Receipt(B, Line(item, 6m, 125m))`. The helpers are `PostingTests.Receipts`, `.Issues`, `.Receipt`, `.Line`, `.Issue`, `.IssueLine` (`PostingTests.cs:20-21,556-576`) and `TransferTests.cs:17,342-345`.
  - Add a private `GetJsonAsync(HttpClient client, string path, CancellationToken ct)` that asserts `200` and returns the `JsonNode`.
  - Facts:
    1. `Item_lists_its_balance_at_each_physical_location`: Admin, `PostVAsync(client, 5, ct)`. `GET /api/items/{item}`: `sku` `"V-ITEM"`, `version` non-empty base64, and `balances` has exactly two entries in this order: `LOC-A` with `onHand` 6, `stockValue` 720.0000, `averageCost` 120.0000, `lastUnitCost` 120.0000; `LOC-B` with 4, 480.0000, 120.0000, 120.0000. No entry has code `SUP`, `CUS` or `ADJ`. (LUC after an inflow is `Round4(StockValue / OnHand)`, spec §7.)
    2. `Item_with_no_stock_has_no_balances`: a new item, never posted → `balances` is an empty array.
    3. `Item_issued_to_zero_shows_a_zero_balance`: receive 2 @ 10 at a location, issue 2 → one balance with `onHand` 0, `stockValue` 0 and `averageCost` null (`balances[0]["averageCost"]` is `null`).
    4. `Staff_can_read_an_item_with_balances`: a Staff user of the same tenant (`PostingTests.AddUserAsync(app, tenant, Roles.Staff)`, `PostingTests.cs:602`) gets `200` with the same `balances` count as the Admin.

## Do not
- Change `ItemResponse.cs`, `GetItem/Endpoint.cs`, `ItemsTests.cs`, or anything under `Features/Posting/**`, `Persistence/**` or `Domain/**`.
- Add an index, a migration, a package or a ProblemDetails slug.
- Touch files outside the `files` list. If you must, stop and report why.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*ItemHistoryTests' --filter-class '*ItemsTests' --filter-class '*TenantIsolationTests'
dotnet test            # if the Testcontainers start-up flake shows: dotnet test -- --max-parallel-test-modules 1
```
- The 4 new facts pass; `ItemsTests.Get_returns_the_item_with_its_version` and every other pre-existing test pass unmodified.
