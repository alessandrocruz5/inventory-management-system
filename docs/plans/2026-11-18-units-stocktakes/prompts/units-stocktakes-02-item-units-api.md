---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 02
unit: IMS-28
status: done
model: sonnet
effort: high
depends_on: [01]
parallel_group: B
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Items/Units/CreateItemUnit/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Items/Units/CreateItemUnit/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Items/Units/CreateItemUnit/Request.cs
    - src/Modules/Inventory/Inventory/Features/Items/Units/DeactivateItemUnit/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Items/Units/DeactivateItemUnit/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Items/ItemUnitResponse.cs
    - tests/Inventory.IntegrationTests/ItemUnitsTests.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Items/ItemEndpoints.cs
    - src/Modules/Inventory/Inventory/Features/Items/ItemDetailResponse.cs
    - src/Modules/Inventory/Inventory/Features/Items/GetItem/Handler.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 02 · Item units API: create, deactivate, and `units` on GET /items/{id}

## Task
Add `POST /api/items/{id}/units` and `POST /api/items/{id}/units/{code}/deactivate` (both Admin), and return `units` on `GET /api/items/{id}`. Add T5 probes and the tests.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. Prompt 01 added `Persistence/ItemUnit.cs` and `db.ItemUnits` (PK `(TenantId, ItemId, Code)`, `CK_ItemUnits_Factor`).
- Plan D3: a code is trimmed, lowercased and at most 16 characters, and is never equal to the item's `BaseUnit`. It's unique per item, inactive codes included, because inactive codes stay reserved. `factor` is > 0, ≤ `PostingValidation.MaxAmount` (999,999,999.9999) and has at most 4 decimal places. A factor is immutable. There is no update route and no reactivation.
- Conventions: endpoints follow `Features/<Area>/<Endpoint>/{Endpoint,Handler,Request}.cs` with plain scoped handlers registered in `InventoryModuleExtensions.cs:56-76`. Errors go through `Features/Problems.cs` (`Validation`, `NotFound`, `IsUniqueViolation`). `ICurrentUser user` and `TimeProvider clock` are injected. Types are `internal`. T10 (`PostingClockTests.cs:89`) scans `Inventory.Features` types ending in `Request` or `Line` for dates, so request types carry none.
- Patterns: `Features/Items/CreateItem/{Endpoint,Handler,Request}.cs` (`.RequireAuthorization(InventoryPolicies.Admin)`, 201 with a `Location` header, unique violation → 400) and `Features/Items/DeactivateItem/Handler.cs` (idempotent deactivate in a transaction inside `db.Database.CreateExecutionStrategy().ExecuteAsync`, with `db.ChangeTracker.Clear()` per attempt). To check decimal places, mirror the private `PostingValidation.Amount` (`Features/Posting/PostingValidation.cs:371-385`): `CostingPolicy.Round4(v) != v` → "Factor can have at most 4 decimal places." Don't change `PostingValidation`; it's prompt 06's file.

### 1. Response
`Features/Items/ItemUnitResponse.cs`: `internal sealed record ItemUnitResponse(string Code, decimal Factor, bool IsActive) { public static ItemUnitResponse From(ItemUnit u) => new(u.Code, u.Factor, u.IsActive); }`.

### 2. GET /items/{id}
Add `IReadOnlyList<ItemUnitResponse> Units` as the **last** member of `ItemDetailResponse`, and extend `From(item, balances, units)`. In `GetItem/Handler.cs`, load `db.ItemUnits.AsNoTracking().Where(u => u.ItemId == id)`, ordered with `StringComparer.Ordinal` by `Code` in C#. Include inactive units, flagged by `isActive`.

### 3. POST /items/{id}/units (Admin)
- `CreateItemUnitRequest(string? Code, decimal? Factor)`.
- Pure validation first:
  - `code` is required and at most 16 characters after trim; lowercase it with `ToLowerInvariant()`.
  - `factor` is required, > 0, ≤ `MaxAmount` and has at most 4 dp.
  - Each failure is a 400 on its field.
- Then, inside the execution strategy, in a transaction, after `ChangeTracker.Clear()`:
  - Read the item with `db.Items.FromSql($"SELECT * FROM [inventory].[Items] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}").SingleOrDefaultAsync(ct)`. Missing → `Problems.NotFound()`. A tenant B id is invisible, so it's 404. Comment: plan D7, the U lock orders this against a base-unit change (prompt 03).
  - `code == item.BaseUnit` → 400 `code` "This is the item's base unit."
  - An existing `(ItemId, Code)` row, active or not → 400 `code` "This item already has this unit."
  - Insert with `IsActive = true`, `CreatedAt = clock.GetUtcNow()` and `CreatedBy = user.UserId`, then save and commit. A unique violation (a race) → the same 400 `code`.
- Return `TypedResults.Created($"/api/items/{id}/units/{code}", ItemUnitResponse.From(unit))`.

### 4. POST /items/{id}/units/{code}/deactivate (Admin)
Lowercase the route `code`. A missing item or unit → 404. Already inactive → 200 with the unit, writing nothing. Otherwise set `IsActive = false`, `UpdatedAt` and `UpdatedBy`, save, and return 200. Run it in a strategy transaction like `DeactivateItem`. The only race is two deactivations, and both answer 200.

### 5. Map and register
`ItemEndpoints.MapItemEndpoints`: add `items.MapCreateItemUnit(); items.MapDeactivateItemUnit();`. Their routes are `"/{id:guid}/units"` and `"/{id:guid}/units/{code}/deactivate"`; match the route-constraint style the existing item routes use. Register both handlers in `InventoryModuleExtensions`.

### 6. Tests
`tests/Inventory.IntegrationTests/ItemUnitsTests.cs`: copy the class shape, fixture, `[Collection]`/constructor and helpers from `ItemsTests.cs`. Use `DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin)`, `DatabaseFixture.SignInAsync`, `PostingTests.NewItemAsync` (creates a `pc` item) and `PostingTests.AddUserAsync` for Staff.
- `Create_returns_201_and_get_lists_the_unit`: `{ code: " Case ", factor: 12 }` → 201 with `code` "case", `factor` 12 and `isActive` true. `GET /api/items/{id}` has `units: [{ code: "case", factor: 12, isActive: true }]`. Compare decimals with `GetValue<decimal>()`, never as text (IMS-8 nit).
- `Create_validates` (`[Theory]`): no code; a 17-char code; no factor; factor 0; factor −1; factor 1.00001; factor 1,000,000,000 → 400 on `code` or `factor`.
- `Create_rejects_the_base_unit_and_duplicates_ignoring_case`: `pc` → 400 `code`. `case` then `CASE` → 400 `code`. After a deactivate, `case` again → still 400.
- `Deactivate_is_idempotent`: two deactivates → both 200, `isActive` false, and GET shows it inactive.
- `Unknown_item_or_unit_is_404`.
- `Staff_gets_403_on_unit_routes`: Staff on both POSTs → 403 with no `Location` header, and no row is written.
- `TenantIsolationTests.cs`: add a `[ProbesRoute("POST /api/items/{id:guid}/units")]` probe and one for the deactivate route. Use exactly the `RawText` the guard prints (`:775-797`). Tenant B's item id → 404 on both. Follow the existing item probes' style.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Change `UpdateItem`, the seed or posting. Those are prompt 03 and IMS-29.
- Add an update or reactivate route, dependencies or abstractions. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*ItemUnitsTests' --filter-class '*ItemsTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests'
dotnet test --project tests/Architecture.Tests
```
- `Every_api_route_is_probed_or_exempt` passes.
