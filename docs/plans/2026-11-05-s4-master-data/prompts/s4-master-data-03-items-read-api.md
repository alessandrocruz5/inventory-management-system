---
type: prompt
plan: "[[2026-11-05-s4-master-data]]"
id: 03
unit: IMS-5
status: done
model: sonnet
effort: high
depends_on: [01, 02]
parallel_group: C
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Paging.cs
    - src/Modules/Inventory/Inventory/Features/Problems.cs
    - src/Modules/Inventory/Inventory/Features/Items/ItemResponse.cs
    - src/Modules/Inventory/Inventory/Features/Items/ItemEndpoints.cs
    - src/Modules/Inventory/Inventory/Features/Items/ListItems/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Items/ListItems/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Items/ListItems/Request.cs
    - src/Modules/Inventory/Inventory/Features/Items/GetItem/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Items/GetItem/Handler.cs
    - tests/Inventory.IntegrationTests/ItemsTests.cs
  modify:
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/DatabaseFixture.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 03 · Items read API and HTTP plumbing

## Task
Add `GET /api/items` and `GET /api/items/{id}`, any role. This also creates the Inventory endpoint pattern every later slice copies: shared paging and problem helpers, one folder per endpoint, and plain scoped handlers. Add T5 probes for both routes and the `ItemsTests` read tests.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-5`; don't switch it. Prompt 01 added `Inventory.Persistence.Item` (`Id`, `Sku`, `Name`, `BaseUnit`, `IsActive`, `RowVersion`, …) and `InventoryDbContext.Items`. Prompt 02 added `Kernel.Problems.ProblemTypes.NotFound` (`"not-found"`) and `ConcurrencyConflict`.

**Rules that apply**
- Every type is `internal` (`internal sealed class` / `internal sealed record` / `internal static class`). T12 `Module_implementations_expose_only_their_registration_class` fails on any new public type.
- Never read or set `TenantId`: the `"Tenant"` query filter scopes every query, and a foreign id simply isn't found → `404`. Never call `IgnoreQueryFilters`. Never read claims (T12): use `Kernel.Tenancy.ICurrentUser` if you need the user.
- Problems are RFC 9457 with `type` = `ProblemTypes.Uri(slug)`. Copy `src/Modules/Platform/Platform/Identity/AuthEndpoints.cs:51` for validation and `:92-96` for a problem.
- JSON is camelCase (ASP.NET default). `byte[]` serializes as base64; that is the `version` field.
- Layout (user decision): `Features/<Area>/<Endpoint>/{Endpoint,Handler,Request,Response}.cs`, with only the files an endpoint needs. Namespace = folder (`Inventory.Features.Items.ListItems`). Classes are prefixed with the endpoint name (`ListItemsEndpoint`, `ListItemsHandler`, `ListItemsRequest`), because `Endpoint` alone collides with `Microsoft.AspNetCore.Http.Endpoint`.

**1. `Features/Paging.cs`** (namespace `Inventory.Features`):
```csharp
/// <summary>One page of a list (spec §9: page and pageSize, default 25, max 100).</summary>
internal sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

internal static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>Resolves the defaults, adding an error for a page below 1 or a pageSize outside 1..100.</summary>
    public static (int Page, int PageSize) Resolve(int? page, int? pageSize, Dictionary<string, string[]> errors) { … }

    /// <summary>Counts, then reads one page. The query must already be ordered.</summary>
    public static async Task<PagedResponse<T>> ToPageAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken) { … }
}
```
The error keys are `"page"` and `"pageSize"`. The messages are `"Page must be 1 or more."` and `"Page size must be between 1 and 100."`.

**2. `Features/Problems.cs`** (namespace `Inventory.Features`):
```csharp
/// <summary>The ProblemDetails results Inventory endpoints return (spec §9).</summary>
internal static class Problems
{
    public static ValidationProblem Validation(Dictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, type: ProblemTypes.Uri(ProblemTypes.Validation));

    public static ProblemHttpResult NotFound() => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound, title: "Not found", type: ProblemTypes.Uri(ProblemTypes.NotFound));
}
```
Prompt 04 adds more to this file. Don't add anything else now.

**3. `Features/Items/ItemResponse.cs`** (namespace `Inventory.Features.Items`):
```csharp
/// <summary>An item as every Items endpoint returns it; <c>Version</c> is the rowversion an edit sends back.</summary>
internal sealed record ItemResponse(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive, byte[] Version)
{
    public static ItemResponse From(Item item) => new(item.Id, item.Sku, item.Name, item.BaseUnit, item.IsActive, item.RowVersion);
}
```

**4. `ListItems/`.**
- `Request.cs`: `internal sealed record ListItemsRequest(string? Search, bool? Active, int? Page, int? PageSize);`, bound with `[AsParameters]` from the query string (`?search=&active=&page=&pageSize=`).
- `Handler.cs`: `internal sealed class ListItemsHandler(InventoryDbContext db)` with `Task<Results<Ok<PagedResponse<ItemResponse>>, ValidationProblem>> HandleAsync(ListItemsRequest request, CancellationToken cancellationToken)`:
  - Resolve paging. A `search` longer than 200 characters after trimming → `errors["search"] = ["Search must be at most 200 characters."]`. Any error → `Problems.Validation(errors)`.
  - Use `db.Items.AsNoTracking()`. When `search` is non-blank, filter `i.Sku.Contains(s) || i.Name.Contains(s)` on the trimmed value; it's case-insensitive through the database collation. When `active` has a value, filter `i.IsActive == active`.
  - Order by `Sku`, project with `new ItemResponse(i.Id, i.Sku, i.Name, i.BaseUnit, i.IsActive, i.RowVersion)`, then `ToPageAsync`.
- `Endpoint.cs`: `internal static class ListItemsEndpoint` with `public static RouteHandlerBuilder MapListItems(this RouteGroupBuilder items) => items.MapGet("", ([AsParameters] ListItemsRequest request, ListItemsHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(request, cancellationToken));`

**5. `GetItem/`.**
- `Handler.cs`: `internal sealed class GetItemHandler(InventoryDbContext db)` with `Task<Results<Ok<ItemResponse>, ProblemHttpResult>> HandleAsync(Guid id, CancellationToken cancellationToken)`. Use `AsNoTracking().SingleOrDefaultAsync(i => i.Id == id)`. `null` → `Problems.NotFound()`.
- `Endpoint.cs`: `MapGetItem(this RouteGroupBuilder items) => items.MapGet("/{id:guid}", …)`.
- Add a `<summary>` on the handler: `The item only; S8 adds its balance per physical location (spec audit fix (b)).`

**6. `Features/Items/ItemEndpoints.cs`:**
```csharp
internal static class ItemEndpoints
{
    /// <summary>Maps <c>/items</c> under the caller's group, which already requires a signed-in user (Api Program.cs).</summary>
    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder api)
    {
        var items = api.MapGroup("/items");
        items.MapListItems();
        items.MapGetItem();
        return api;
    }
}
```

**7. `InventoryModuleExtensions.cs`.** In `AddInventoryModule` (lines 16-27), after `IInventoryModule`, register `builder.Services.AddScoped<ListItemsHandler>();` and `AddScoped<GetItemHandler>();`. Change `MapInventoryEndpoints` (line 29) to a block body that calls `endpoints.MapItemEndpoints();` and returns `endpoints`. Keep it the class's only other public method (T12 checks the method names).

**8. `tests/Inventory.IntegrationTests/DatabaseFixture.cs`.** Add a sign-in helper beside `CreateTenantWithUserAsync` (line 62):
```csharp
/// <summary>A client signed in as the user, with the auth cookie in its container.</summary>
internal static async Task<HttpClient> SignInAsync(ApiFactory app, string email)
{
    var client = app.NewClient();
    var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = UserPassword }, TestContext.Current.CancellationToken);
    response.EnsureSuccessStatusCode();
    return client;
}
```
Add `using System.Net.Http.Json;`.

**9. `tests/Inventory.IntegrationTests/ItemsTests.cs`** (new; `public sealed class ItemsTests(DatabaseFixture db)`, with the xUnit v3 and Shouldly style of `TenantIsolationTests.cs`). Put a private helper `AddItemsAsync(ApiFactory app, Guid tenant, params (string Sku, string Name, bool Active)[] items)` that inserts through `ITenantScopeFactory.BeginScope(tenant)` → `InventoryDbContext.Items.Add(new Item { Sku, Name, BaseUnit = "pc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow })`, setting `IsActive = false` afterwards and saving again for inactive rows. Never set `TenantId`; the interceptor stamps it. It returns the ids. Read JSON with `ReadFromJsonAsync<JsonNode>`. Sign in as **Staff** for every test here: that is R5's read half.
- `List_pages_and_orders_by_sku`: add `C-1`, `A-1`, `B-1`. `?pageSize=2` → `items` SKUs `[A-1, B-1]`, `page` 1, `pageSize` 2, `total` 3. `?page=2&pageSize=2` → `[C-1]`.
- `List_defaults_to_page_1_of_25`: no query → `page` 1, `pageSize` 25.
- `List_searches_sku_and_name`: add `RICE-1 "White rice"` and `OIL-1 "Palm oil"`. `?search=rice` → `[RICE-1]` (case-insensitive SKU match). `?search=palm` → `[OIL-1]` (a name match).
- `List_filters_by_active`: one active and one inactive. `?active=true` → only the active one, `?active=false` → only the inactive one, no filter → both.
- `[Theory]` `List_rejects_out_of_range_paging` with `("page=0", "page")`, `("pageSize=0", "pageSize")`, `("pageSize=101", "pageSize")` → `400`, `type` `/problems/validation`, and `errors` has the key.
- `Get_returns_the_item_with_its_version`: `200`, every field, and a non-empty base64 `version`.
- `Get_unknown_id_is_404_not_found`: `Guid.NewGuid()` → `404`, `type` `/problems/not-found`.

**10. `tests/Inventory.IntegrationTests/TenantIsolationTests.cs`.** Add two probes; the route guard at line 124 fails until both exist. Insert data through `BeginScope`, as above (copy the helper privately, or call a `static` one on `ItemsTests`).
- `[Fact] [ProbesRoute("GET /api/items")] Each_tenant_lists_only_its_own_items`: tenants A and B each get an item with the **same** SKU, which proves per-tenant uniqueness at the database. A's list returns exactly A's id, and B's exactly B's.
- `[Fact] [ProbesRoute("GET /api/items/{id:guid}")] Another_tenants_item_id_is_404`: A requests B's item id → `404 /problems/not-found`.

The probe string must equal the endpoint's `RoutePattern.RawText`. If the guard still lists them as unlisted, copy the exact strings from its failure message.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add write routes, an Admin policy or `ItemRules` (prompt 04 does that).
- Use MediatR, AutoMapper, FluentValidation or .NET's `AddValidation()`; add no package.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
```
- `ItemsTests` (8 cases) and the two new T5 probes pass, and `Every_api_route_is_probed_or_exempt` passes.
- `grep -rn 'TenantId\|IgnoreQueryFilters\|Claims' src/Modules/Inventory/Inventory/Features` prints nothing.
