---
type: prompt
plan: "[[2026-11-05-s4-master-data]]"
id: 04
unit: IMS-5
status: done
model: sonnet
effort: high
depends_on: [03]
parallel_group: D
files:
  create:
    - src/Modules/Inventory/Inventory/Features/InventoryPolicies.cs
    - src/Modules/Inventory/Inventory/Features/Items/ItemRules.cs
    - src/Modules/Inventory/Inventory/Features/Items/CreateItem/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Items/CreateItem/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Items/CreateItem/Request.cs
    - src/Modules/Inventory/Inventory/Features/Items/UpdateItem/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Items/UpdateItem/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Items/UpdateItem/Request.cs
    - src/Modules/Inventory/Inventory/Features/Items/DeactivateItem/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Items/DeactivateItem/Handler.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Problems.cs
    - src/Modules/Inventory/Inventory/Features/Items/ItemEndpoints.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
    - tests/Inventory.IntegrationTests/ItemsTests.cs
  delete: []
---
# 04 · Items write API and Admin policy

## Task
Add the three Admin-only Items routes: `POST /api/items`, `PUT /api/items/{id}` and `POST /api/items/{id}/deactivate`. Add their validation, the per-tenant SKU uniqueness, optimistic concurrency and T5 probes, plus tests, including Staff → `403`.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-5`; don't switch it. Prompt 03 created the pattern. **Read these first and copy them exactly:**
- `src/Modules/Inventory/Inventory/Features/Items/ListItems/{Endpoint,Handler,Request}.cs`
- `Features/Items/GetItem/*`, `Features/Items/ItemEndpoints.cs`, `Features/Items/ItemResponse.cs`
- `Features/Problems.cs`, `Features/Paging.cs`
- `tests/Inventory.IntegrationTests/ItemsTests.cs`, and `DatabaseFixture.SignInAsync` / `CreateTenantWithUserAsync`

Same rules as prompt 03: everything `internal`; never touch `TenantId` or `IgnoreQueryFilters`; never read claims (T12 `Modules_do_not_read_claims`). The user comes from `Kernel.Tenancy.ICurrentUser.UserId` and the time from the injected `TimeProvider`. Kernel names: `Kernel.Tenancy.RoleNames.Admin`, and `ProblemTypes.ConcurrencyConflict` (`"concurrency-conflict"`).

**1. `Features/InventoryPolicies.cs`:**
```csharp
/// <summary>Role policies for Inventory routes. The /api group already requires a signed-in user (Api Program.cs).</summary>
internal static class InventoryPolicies
{
    public static readonly AuthorizationPolicy Admin = new AuthorizationPolicyBuilder().RequireRole(RoleNames.Admin).Build();
}
```
A Staff user then gets a bare `403` from the cookie handler's `OnRedirectToAccessDenied` (`Platform/Identity/IdentityRegistration.cs:43-47`). Authorization runs before the body is bound, so nothing is written.

**2. `Features/Problems.cs`.** Add:
```csharp
public static ProblemHttpResult ConcurrencyConflict() => TypedResults.Problem(
    statusCode: StatusCodes.Status409Conflict, title: "Changed by someone else", type: ProblemTypes.Uri(ProblemTypes.ConcurrencyConflict));

/// <summary>SQL Server 2601 (unique index) or 2627 (unique constraint): a racing insert took the key.</summary>
public static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is SqlException { Number: 2601 or 2627 };
```
`Microsoft.Data.SqlClient` is already referenced through `Microsoft.EntityFrameworkCore.SqlServer`.

**3. `Features/Items/ItemRules.cs`:**
```csharp
/// <summary>Normalizes and validates an item's editable fields (spec §5).</summary>
internal static class ItemRules
{
    /// <summary>Trims all three and lowercases the unit; adds an error per invalid field.</summary>
    public static (string Sku, string Name, string BaseUnit) Normalize(string? sku, string? name, string? baseUnit, Dictionary<string, string[]> errors);
}
```
- `sku`: trimmed, required (`"SKU is required."`), ≤ 64 (`"SKU must be at most 64 characters."`).
- `name`: trimmed, required, ≤ 200.
- `baseUnit`: trimmed and `ToLowerInvariant()`, required, ≤ 16.
- Error keys are `sku`, `name`, `baseUnit`.

**4. `CreateItem/`.**
- `Request.cs`: `internal sealed record CreateItemRequest(string? Sku, string? Name, string? BaseUnit);`
- `Handler.cs`: `internal sealed class CreateItemHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)` with `Task<Results<Created<ItemResponse>, ValidationProblem>> HandleAsync(CreateItemRequest request, CancellationToken cancellationToken)`:
  1. Normalize. If there are errors, return `Problems.Validation`.
  2. If `await db.Items.AnyAsync(i => i.Sku == sku)`, set `errors["sku"] = ["An item with this SKU already exists."]`. The comparison is case-insensitive through the collation.
  3. Add `new Item { Sku, Name, BaseUnit, IsActive = true, CreatedAt = clock.GetUtcNow(), CreatedBy = user.UserId }`. Always set `IsActive = true` explicitly: `HasDefaultValue(true)` makes `false` EF's sentinel.
  4. Call `SaveChangesAsync` inside `try`. On `catch (DbUpdateException ex) when (Problems.IsUniqueViolation(ex))`, return the same SKU error.
  5. `TypedResults.Created($"/api/items/{item.Id}", ItemResponse.From(item))`.
- `Endpoint.cs`: `MapCreateItem(this RouteGroupBuilder items) => items.MapPost("", …).RequireAuthorization(InventoryPolicies.Admin);`

**5. `UpdateItem/`.**
- `Request.cs`: `internal sealed record UpdateItemRequest(string? Sku, string? Name, string? BaseUnit, byte[]? Version);`
- `Handler.cs`: `UpdateItemHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)`, `HandleAsync(Guid id, UpdateItemRequest request, …)` returning `Results<Ok<ItemResponse>, ValidationProblem, ProblemHttpResult>`:
  1. Normalize. A missing or empty `version` gives `errors["version"] = ["Version is required."]`. If there are errors, return them.
  2. `var item = await db.Items.SingleOrDefaultAsync(i => i.Id == id)`. `null` → `Problems.NotFound()`; a foreign id lands here too.
  3. If `await db.Items.AnyAsync(i => i.Sku == sku && i.Id != id)`, return the SKU error.
  4. `db.Entry(item).Property(i => i.RowVersion).OriginalValue = request.Version;`. Then set `Sku`, `Name`, `BaseUnit`, `UpdatedAt = clock.GetUtcNow()` and `UpdatedBy = user.UserId`.
  5. Save. `DbUpdateConcurrencyException` → `Problems.ConcurrencyConflict()`. A unique violation → the SKU error.
  6. `Ok(ItemResponse.From(item))`. EF reads the new rowversion back on save.
  - Put this comment above step 3: `// Spec §9 allows a SKU change only before the first movement; that check lands with the ledger tables (S5a).`
- `Endpoint.cs`: `items.MapPut("/{id:guid}", …).RequireAuthorization(InventoryPolicies.Admin)`.

**6. `DeactivateItem/`** (no request body; Endpoint and Handler only).
- `DeactivateItemHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)`, `HandleAsync(Guid id, …)` returning `Results<Ok<ItemResponse>, ProblemHttpResult>`:
  1. Load. `null` → 404.
  2. If `IsActive`, set it `false` with `UpdatedAt`/`UpdatedBy` and save. `DbUpdateConcurrencyException` → 409.
  3. Already inactive → `200` unchanged (idempotent).
  4. Return `Ok(From(item))`.
  - Put this comment above step 2: `// Spec §9 allows deactivation only with zero physical stock; that check lands with the ledger tables (S5a).`
- `items.MapPost("/{id:guid}/deactivate", …).RequireAuthorization(InventoryPolicies.Admin)`.

**7. `ItemEndpoints.cs`.** Call `MapCreateItem`, `MapUpdateItem` and `MapDeactivateItem` after the two read maps. In **`InventoryModuleExtensions.cs`**, register `CreateItemHandler`, `UpdateItemHandler` and `DeactivateItemHandler` as scoped, next to the read handlers.

**8. `ItemsTests.cs`** (add; sign in as **Admin** unless the test says Staff):
- `Create_returns_201_with_location_and_normalized_fields`: body `{ sku: "  AB-1 ", name: " Rice ", baseUnit: " KG " }` → `201`. The `Location` header is `/api/items/{id}`. The body has `sku` `"AB-1"`, `name` `"Rice"`, `baseUnit` `"kg"`, `isActive` true and a non-empty `version`. `GET` on the Location → `200`, with the same id.
- `Create_records_the_user`: in a `BeginScope(tenant)`, the row's `CreatedBy` equals the `id` from `GET /api/auth/me`, and `CreatedAt` isn't default.
- `[Theory] Create_validates_fields` with `(sku, name, baseUnit, expectedKey)`: `(null, "N", "pc", "sku")`, `(new string('S', 65), "N", "pc", "sku")`, `("S", "", "pc", "name")`, `("S", new string('N', 201), "pc", "name")`, `("S", "N", " ", "baseUnit")`, `("S", "N", new string('u', 17), "baseUnit")`. Each → `400 /problems/validation` with the key in `errors`.
- `Create_rejects_a_duplicate_sku_ignoring_case`: `AB-1`, then `ab-1` → `400`, `errors.sku`.
- `Staff_gets_403_on_every_admin_route`: as Staff, POST `/api/items`, PUT a seeded item and POST its deactivate. Each → `403` with `response.Headers.Location` null. Afterwards, in a tenant scope, the item count is unchanged, and the seeded item is still active with its original name and rowversion. (The IMS-3 Downstream asks for this.)
- `Update_changes_fields_and_returns_a_new_version`: PUT a new SKU, name and unit with the GET's `version` → `200`, the new values, and a `version` different from the old one. A SKU change is allowed in S4.
- `Update_with_a_stale_version_is_409`: PUT once with version v1 → `200`. PUT again with v1 → `409 /problems/concurrency-conflict`.
- `Update_without_version_is_400`: `errors.version`.
- `Update_to_another_items_sku_is_400`: `errors.sku`.
- `Update_and_deactivate_unknown_id_are_404`.
- `Deactivate_is_idempotent_and_hides_the_item_from_active_lists`: deactivate twice → `200` both times, `isActive` false. `?active=true` excludes it, and `?active=false` includes it.

**9. `TenantIsolationTests.cs`.** Add three probes; the guard fails without them. There are no body ids in S4, so there is no `400` body-id probe.
- `[ProbesRoute("POST /api/items")] Skus_are_unique_per_tenant`: A and B each POST SKU `SAME-1` → both `201`. A's list doesn't contain B's id.
- `[ProbesRoute("PUT /api/items/{id:guid}")] Another_tenants_item_cannot_be_updated`: A (Admin) PUTs B's item id with B's real `version` and a valid body → `404 /problems/not-found`. B's GET shows it unchanged.
- `[ProbesRoute("POST /api/items/{id:guid}/deactivate")] Another_tenants_item_cannot_be_deactivated`: → `404`, and B's item is still active.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add `stock-not-zero`, a movement check, a reactivate route, or a `TenantId`/date in any request.
- Use `ClaimsPrincipal`, `ClaimTypes` or `HttpContext.User` in Inventory.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
```
- All `ItemsTests` and the 3 new probes pass. `Every_api_route_is_probed_or_exempt` passes with 5 `/api/items*` routes. The T12 architecture tests pass.
- `grep -rn 'TenantId\|IgnoreQueryFilters\|Claims' src/Modules/Inventory/Inventory/Features` prints nothing.
