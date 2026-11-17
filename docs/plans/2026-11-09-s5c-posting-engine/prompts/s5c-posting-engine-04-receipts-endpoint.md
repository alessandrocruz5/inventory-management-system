---
type: prompt
plan: "[[2026-11-09-s5c-posting-engine]]"
id: 04
unit: IMS-8
status: done
model: opus
effort: high
depends_on: [01, 03]
parallel_group: C
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentResponse.cs
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentPoster.cs
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentEndpoints.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostReceipt/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostReceipt/Handler.cs
    - tests/Inventory.IntegrationTests/PostingTests.cs
  modify:
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 04 · Receipts endpoint and shared poster

## Task
Add `POST /api/documents/receipts`, the first posting route, on top of a shared `DocumentPoster`. Prompt 05's issues route reuses the poster; so will S6 later. Give the route its T5 probe, and integration tests for its behaviour.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-8`. Don't switch it.
- **Prerequisite:** run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` first.
- **Layout** (TASKS.md IMS-5 Downstream): `Features/<Area>/<Endpoint>/{Endpoint,Handler,Request}.cs`, with plain scoped handlers sharing `Features/Problems.cs`. Every type is `internal` (T12). Copy these:
  - `Features/Items/CreateItem/Endpoint.cs`: the `MapPost` on a group, taking the handler and the `CancellationToken`.
  - `Features/Items/CreateItem/Handler.cs`: typed `Results<…>`, `TypedResults.Created`.
  - `Features/Items/ItemEndpoints.cs:15-24`: the group method.
- **What prompts 01 and 03 created:**
  - `Features/Posting/PostReceipt/Request.cs`: `PostReceiptRequest`.
  - `Features/Posting/PostingValidation.cs`: `Receipt(request, errors)` → `ValidatedPosting?`, and `IdempotencyKey(header, errors, out missing)`.
  - `Features/Posting/RequestHash.cs`: `Receipt(validated)` → 32 bytes, and `ReceiptsRoute`.
  - `Persistence/PostingCommand.cs`: `PostingCommand` and the outcomes `PostedOutcome`, `KeyReusedOutcome`, `InactiveItemOutcome`, `InactiveLocationOutcome`, `RejectedOutcome`, `BalanceOutOfRangeOutcome`, `RetriesExhaustedOutcome`.
  - `Persistence/PostingEngine.cs`: `FindExistingAsync(key, hash, ct)` and `PostAsync(command, ct)`.
  - `Features/Problems.cs`: `IdempotencyKeyMissing()`, `IdempotencyKeyReused(id)`, `InsufficientStock(item, loc, available, requested)`, `InactiveItem(id)`, `InactiveLocation(id)`, plus the existing `Validation(errors)`, `ConcurrencyConflict()`.
  - Domain: `Inventory.Domain.PostingKind.Receipt`, `PostingLine(LineNo, ItemId, FromLocationId, ToLocationId, Quantity, UnitCost)`, `InsufficientStockException`.

### `Features/Posting/DocumentResponse.cs` (plan D3)
```csharp
internal sealed record DocumentResponse(Guid Id, string Type, string? Reference, string? Notes, DateTimeOffset PostedAt, Guid PostedBy,
    IReadOnlyList<MovementResponse> Movements, IReadOnlyList<BalanceResponse> Balances, DateTimeOffset BalancesAsOf)
{
    public static DocumentResponse From(PostedOutcome outcome);   // Type = Document.Type.ToString(); movements by LineNo
}
internal sealed record MovementResponse(long Id, int LineNo, Guid ItemId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, decimal TotalCost, long? ReversesMovementId);
internal sealed record BalanceResponse(Guid ItemId, Guid LocationId, decimal OnHand, decimal StockValue, decimal? LastUnitCost);
```

### `Features/Posting/DocumentPoster.cs`
`internal sealed class DocumentPoster(InventoryDbContext db, PostingEngine engine, ICurrentUser user)`, registered as scoped. It has one method, generic enough for prompt 05 and S6:

```csharp
public async Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> PostAsync(
    HttpContext http, PostingKind kind, StockDocumentType type, LocationKind counterpart, bool counterpartIsSource,
    Func<Dictionary<string, string[]>, ValidatedPosting?> validate, Func<ValidatedPosting, byte[]> hash, CancellationToken ct)
```
Shape the signature however reads best; the **order** is fixed (plan D2):
1. Read the key with `PostingValidation.IdempotencyKey(http.Request.Headers["Idempotency-Key"], errors, out var missing)`. If `missing` → `Problems.IdempotencyKeyMissing()`; if there are errors → `Problems.Validation(errors)`.
2. `validate(errors)`. If the result is null → `Problems.Validation(errors)`.
3. Compute the hash with `hash(validated)`.
4. Step 2: `engine.FindExistingAsync(key, hash, ct)`. A non-null result maps through `ToResult` (below). A replay must **not** run the database checks (D2).
5. Step 1's database part (spec §6):
   - The location: `db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == validated.LocationId)`. If it's null or `Kind != LocationKind.Physical`, set `errors["locationId"] = ["Choose an existing physical location."]`. If it's inactive → `Problems.InactiveLocation(id)`.
   - The items: `db.Items.AsNoTracking().Where(i => ids.Contains(i.Id))`. For each line in `LineNo` order, an id not found gives `errors[$"lines[{lineNo - 1}].itemId"] = ["Choose an existing item."]`. The first inactive one → `Problems.InactiveItem(id)`.
   - Return `400` if there are any errors, before any `422`.
   - Tenant B's ids are invisible through the tenant filter and RLS, so they behave like unknown ids: `400`, never `404` or `403` (spec §8, T5).
   - The counterpart: `db.Locations.AsNoTracking().SingleAsync(l => l.Kind == counterpart)`, which is Supplier for receipts and Customer for issues. A missing one is a provisioning bug, so let it throw.
6. Build the `PostingLine`s:
   - For a receipt, `From = counterpart` and `To = location`, with `UnitCost` set.
   - For an issue, `From = location` and `To = counterpart`, with `UnitCost = null`.
   - Then `new PostingCommand(kind, type, lines, key, hash, validated.Reference, validated.Notes, user.UserId)`, and `engine.PostAsync(command, ct)`.
7. `ToResult(outcome)`:
   - `PostedOutcome { Replayed: false }` → `TypedResults.Created($"/api/documents/{id}", DocumentResponse.From(o))`.
   - `PostedOutcome { Replayed: true }` → set `http.Response.Headers["Idempotent-Replayed"] = "true"`, then `TypedResults.Ok(DocumentResponse.From(o))`.
   - `KeyReusedOutcome` → `Problems.IdempotencyKeyReused(o.OriginalDocumentId)`.
   - `InactiveItemOutcome` / `InactiveLocationOutcome` → the matching 422.
   - `RejectedOutcome { Rejection: InsufficientStockException e }` → `Problems.InsufficientStock(e.ItemId, e.LocationId, e.Available, e.Requested)`.
   - Any other `RejectedOutcome` → `throw new InvalidOperationException(...)`. `UnitCostRequired` and `ReversalNotLatest` are unreachable here; S6 and S9 map them.
   - `BalanceOutOfRangeOutcome` → `Problems.Validation({ [$"lines[{i}].quantity"] = ["This would exceed the largest stock quantity or value the system can hold."] })`, where `i` is the zero-based index of the first line with that `ItemId`.
   - `RetriesExhaustedOutcome` → `Problems.ConcurrencyConflict()`.

### `Features/Posting/PostReceipt/{Endpoint,Handler}.cs`
- `PostReceiptHandler(DocumentPoster poster)` exposes `HandleAsync(PostReceiptRequest request, HttpContext http, CancellationToken ct)`. It calls the poster with `PostingKind.Receipt`, `StockDocumentType.Receipt`, `LocationKind.Supplier`, `errors => PostingValidation.Receipt(request, errors)` and `RequestHash.Receipt`.
- `PostReceiptEndpoint.MapPostReceipt(this RouteGroupBuilder documents)` maps `documents.MapPost("/receipts", …)`.
- No role policy: Staff and Admin may both post receipts (spec §9), and the `/api` group already requires sign-in.

### `Features/Posting/DocumentEndpoints.cs`
`internal static class DocumentEndpoints { public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder api) { var documents = api.MapGroup("/documents"); documents.MapPostReceipt(); return api; } }`

### `InventoryModuleExtensions.cs`
Register `DocumentPoster` and `PostReceiptHandler` as scoped. Call `endpoints.MapDocumentEndpoints();` after `MapLocationEndpoints()`.

### `tests/Inventory.IntegrationTests/TenantIsolationTests.cs`
Add a `[Fact]` with `[ProbesRoute("POST /api/documents/receipts")]` (T5, spec §11; IMS-4 Downstream: a body id from tenant B gives `400 validation`).
- Create tenants A and B, each with an Admin.
- As B, create a physical location and an item through the API.
- As A, receipt into B's `locationId` with A's own item → `400` with `errors.locationId`.
- As A, receipt into A's location with B's `itemId` → `400` with `errors["lines[0].itemId"]`.
- Then, as B, confirm that B's item has no movements. Count `inventory.StockMovements` for B's item via `LedgerRows.OpenAsync(db, tenantB, ct)` and a `SELECT COUNT(*)`.

The route guard at `:325` compares raw route text. If it reports a different spelling (for example a trailing slash), use the exact text it prints.

### `tests/Inventory.IntegrationTests/PostingTests.cs`
Create `public sealed class PostingTests(DatabaseFixture db)`.
- Copy the setup style of `ItemsTests.cs:23-35`: `db.CreateApp()`, `DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin|Roles.Staff)`, `DatabaseFixture.SignInAsync`.
- Create the location and item as an Admin with the existing shared helpers: `LocationsTests.CreateAsync(client, code, name, ct)` (`LocationsTests.cs:409`) and `ItemsTests.CreateAsync(client, sku, name, baseUnit, ct)` (`ItemsTests.cs:668`). Both are `internal static`, and T5 already uses them. Don't write copies.
- Send `Idempotency-Key` with `HttpRequestMessage`. Put that in one `internal static Task<HttpResponseMessage> PostDocumentAsync(HttpClient client, string route, string? key, object body, CancellationToken ct)` on `PostingTests`, so that prompts 05 and 06 and the T5 probes call it instead of copying it.
- Use `JsonNode` for responses, as the existing tests do.

Tests:
- **`Receipt_returns_201_with_document_movements_and_balance`.** Staff receives 10 @ 100.00.
  - `201` and `Location: /api/documents/{id}`.
  - `type` is `"Receipt"`, and `postedBy` is the user.
  - One movement: from the tenant's SUP id to the location, `quantity` 10, `totalCost` 1000.0000, `lineNo` 1.
  - `balances` holds only the physical balance: 10 / 1000.0000 / `lastUnitCost` 100.0000.
  - `balancesAsOf == postedAt`.
  - In the database, as the tenant: the document row exists, and the SUP balance has `OnHand` −10. `LastMovementId` on both balances equals the movement id.
- **`Receipt_movement_ids_follow_line_order`.** Three lines → three movement ids that **strictly ascend** with `lineNo`. Don't assert that they're consecutive: `MovementIds` is one sequence for the whole database, other test classes post in parallel, and the engine may use the per-movement `NEXT VALUE FOR` fallback. Assert contiguity only if `grep -q sp_sequence_get_range src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs` succeeds.
- **`Receipt_without_key_is_400_idempotency_key_missing`**, and a 65-character key → `400 validation` with `errors["Idempotency-Key"]`.
- **`Receipt_validates_fields`** (`[Theory]`), one case each for:
  - `lines` empty;
  - `quantity` 0;
  - `quantity` with 5 dp;
  - a negative `unitCost`;
  - a missing `unitCost`;
  - a duplicate item;
  - D1: `quantity` 999999999.9999 with `unitCost` 999999999.9999 → `lines[0].unitCost`;
  - `reference` of 101 characters.

  Each asserts `400`, `type` `/problems/validation` and the exact key.
- **`Receipt_into_a_virtual_location_is_400`**, using the tenant's SUP id as `locationId` → `errors.locationId`.
- **`Receipt_of_an_inactive_item_is_422`** (deactivate it first through the API, while it has no stock) → `/problems/inactive-item` with `itemId`. Do the same for an inactive location → `/problems/inactive-location`.
- **`Same_key_and_body_replays_with_200`.** The second call returns `200`, `Idempotent-Replayed: true`, the same `id` and `movements`, and `balancesAsOf` at or after the first. There's still exactly one document for the key.
- **`Same_key_different_body_is_409`.** It returns `/problems/idempotency-key-reused` with `originalDocumentId`.
- The D2 test (a replay after a deactivation) lands in prompt 05. It needs an issue to bring the stock to zero before the item can be deactivated.
- **`Staff_and_admin_may_post_receipts`.** Both get `201`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add a role policy, a package, or a new ProblemDetails slug.
- Change `PostingEngine` or the Domain. If the engine's behaviour blocks a test, stop and report it.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
```
- `PostingTests` and the new T5 probe pass, and `Every_api_route_is_probed_or_exempt` is green.
