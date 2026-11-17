---
type: prompt
plan: "[[2026-11-05-s4-master-data]]"
id: 05
unit: IMS-5
status: done
model: sonnet
effort: high
depends_on: [04]
parallel_group: E
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Locations/LocationResponse.cs
    - src/Modules/Inventory/Inventory/Features/Locations/LocationRules.cs
    - src/Modules/Inventory/Inventory/Features/Locations/LocationEndpoints.cs
    - src/Modules/Inventory/Inventory/Features/Locations/ListLocations/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Locations/ListLocations/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Locations/ListLocations/Request.cs
    - src/Modules/Inventory/Inventory/Features/Locations/CreateLocation/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Locations/CreateLocation/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Locations/CreateLocation/Request.cs
    - src/Modules/Inventory/Inventory/Features/Locations/UpdateLocation/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Locations/UpdateLocation/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Locations/UpdateLocation/Request.cs
    - src/Modules/Inventory/Inventory/Features/Locations/DeactivateLocation/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Locations/DeactivateLocation/Handler.cs
    - tests/Inventory.IntegrationTests/LocationsTests.cs
  modify:
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 05 · Locations API

## Task
Add `GET /api/locations` (any role) and the Admin-only `POST /api/locations`, `PUT /api/locations/{id}` and `POST /api/locations/{id}/deactivate`, physical locations only. Copy the Items pattern from prompts 03–04 exactly. Add T5 probes and `LocationsTests`.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-5`; don't switch it. **Read and mirror these:** `Features/Items/**` (every endpoint folder, `ItemRules.cs`, `ItemResponse.cs`, `ItemEndpoints.cs`), `Features/{Problems,Paging,InventoryPolicies}.cs`, and `tests/Inventory.IntegrationTests/ItemsTests.cs`. The entity is `src/Modules/Inventory/Inventory/Persistence/Location.cs`: `Code` (≤ 32), `Name` (≤ 200), `Kind` (`LocationKind` enum stored as a string: `Physical`, `Supplier`, `Customer`, `Adjustment`), `AllowNegativeStock`, `IsActive`, audit columns, `RowVersion`. The unique index is `(TenantId, Code)`. Every tenant already has the virtual `SUP`, `CUS` and `ADJ` (`InventoryModule.cs:10-15`).

Same rules: everything `internal`; never touch `TenantId`, `IgnoreQueryFilters` or claims; time from `TimeProvider`; user from `ICurrentUser`.

**Decisions to implement** (plan `2026-11-05-s4-master-data`, Decisions):
- `LocationResponse(Guid Id, string Code, string Name, string Kind, bool IsSystem, bool AllowNegativeStock, bool IsActive, byte[] Version)`, where `Kind` is the enum name and `IsSystem = Kind != Physical`. Give it a `From(Location)` like `ItemResponse`.
- `GET /locations`: `ListLocationsRequest(int? Page, int? PageSize)` via `[AsParameters]`, using `Paging.Resolve`/`ToPageAsync`. Order with `OrderBy(l => l.Kind == LocationKind.Physical ? 0 : 1).ThenBy(l => l.Code)`, so physical comes first. It returns inactive rows too (no `active` filter in S4).
- `LocationRules.Normalize(code, name, errors)`: both trimmed and required; `code` ≤ 32, `name` ≤ 200. The error keys are `code` and `name`.
- `POST /locations`: `CreateLocationRequest(string? Code, string? Name)`. It creates `Kind = Physical`, `AllowNegativeStock = false`, `IsActive = true` (explicitly), `CreatedAt`, `CreatedBy`. A duplicate code (pre-check plus `IsUniqueViolation`) → `errors["code"] = ["A location with this code already exists."]`. The same index already holds the reserved `SUP`/`CUS`/`ADJ`, so they're refused too. Returns `201` with `Location: /api/locations/{id}`.
- `PUT /locations/{id}`: `UpdateLocationRequest(string? Code, string? Name, byte[]? Version)`. **No `AllowNegativeStock` property**: the toggle is S10's, because it must follow §6's lock order and update `StockBalances.AllowNegative`. JSON ignores the unknown property. The order is: validate (with `version` required) → load (`null` → 404) → `Kind != Physical` → `Problems.Validation` with `errors["location"] = ["System locations can't be changed."]` → duplicate code excluding self → set the original `RowVersion` → save (concurrency → 409, unique → the code error) → `200`.
- `POST /locations/{id}/deactivate`: load (404) → system → the same `errors["location"]` 400 → idempotent deactivate → `200`. Comment: `// Spec §9 allows deactivation only with zero stock; that check (with UPDLOCK, HOLDLOCK on the balance rows) lands with the ledger tables (S5a).`
- Write routes use `.RequireAuthorization(InventoryPolicies.Admin)`. `LocationEndpoints.MapLocationEndpoints` maps `/locations`, like `ItemEndpoints`. In `InventoryModuleExtensions`, register the 4 handlers scoped and call `endpoints.MapLocationEndpoints();` after `MapItemEndpoints()`.

**`tests/Inventory.IntegrationTests/LocationsTests.cs`** (new; Admin unless stated):
- `List_shows_physical_first_then_the_three_system_locations`: create `ZZ` and `AA`. As **Staff**, the list codes are `[AA, ZZ, ADJ, CUS, SUP]`. The system rows have `isSystem` true and kinds `Adjustment`/`Customer`/`Supplier`; the physical ones have `kind` `Physical` and `isSystem` false.
- `Create_returns_201_with_location_and_trimmed_fields`: `{ code: " BR-1 ", name: " Branch " }` → `201`, `Location` header, `code` `BR-1`, `allowNegativeStock` false, `isActive` true.
- `[Theory] Create_validates_fields`: `(null, "N", "code")`, `(new string('C', 33), "N", "code")`, `("C", "", "name")`, `("C", new string('N', 201), "name")`.
- `[Theory] Create_rejects_a_reserved_or_duplicate_code` with `"SUP"`, `"sup"`, plus a code created earlier in the test → `400 errors.code`.
- `Update_changes_code_and_name_and_ignores_allowNegativeStock`: PUT `{ code, name, version, allowNegativeStock: true }` → `200` and `allowNegativeStock` false. In a tenant scope, the row's `AllowNegativeStock` is false.
- `Update_with_a_stale_version_is_409`, `Update_without_version_is_400`, `Update_and_deactivate_unknown_id_are_404`.
- `System_locations_cannot_be_updated_or_deactivated`: PUT and deactivate on the tenant's `SUP` id → `400 /problems/validation` with `errors.location`. The row is unchanged in a tenant scope.
- `Deactivate_is_idempotent`: twice → `200`, `isActive` false, and the list still returns it with `isActive` false.
- `Staff_gets_403_on_every_admin_route`: as Staff, POST, PUT on a physical id and deactivate → `403`, `Location` header null, and in a tenant scope the location count and the row are unchanged.

**`TenantIsolationTests.cs`.** Add four probes; the guard fails without them.
- `[ProbesRoute("GET /api/locations")] Each_tenant_lists_only_its_own_locations`: A and B each create a physical location with the same code. Each list returns only its own ids: its 3 system locations plus its own physical one.
- `[ProbesRoute("POST /api/locations")] Location_codes_are_unique_per_tenant`: the same code POSTed by A and B → both `201`.
- `[ProbesRoute("PUT /api/locations/{id:guid}")]` and `[ProbesRoute("POST /api/locations/{id:guid}/deactivate")]`: A targets B's physical location (with B's real `version` for the PUT) → `404 /problems/not-found`, and B's row is unchanged.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Accept or change `AllowNegativeStock` or `Kind` through any route, or let any route create a non-physical location.
- Change anything under `Features/Items/` except reading it.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
```
- `LocationsTests` and the 4 probes pass. `Every_api_route_is_probed_or_exempt` passes with 9 Inventory routes. The T12 tests pass.
- `grep -rn 'AllowNegativeStock' src/Modules/Inventory/Inventory/Features` shows only `LocationResponse` and the create handler's `= false`.
