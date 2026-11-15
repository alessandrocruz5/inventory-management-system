---
type: prompt
plan: "[[2026-11-15-s9-reversals]]"
id: 03
unit: IMS-13
status: todo
model: opus
effort: high
depends_on: [01, 02]
parallel_group: C
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Reversals/ReverseDocument/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Reversals/ReverseDocument/Handler.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentEndpoints.cs
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 03 · Reversal route

## Task
Add `POST /api/documents/{id}/reversal` (Admin only, `Idempotency-Key` required, empty body). The handler runs the plan's D3 order, builds a `ReversalCommand` and maps every engine outcome to the spec §9 response. Register and map it, and add its T5 probe (the route guard fails without one).

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-13`. Prompt 01 added `PostingEngine.ReverseAsync(ReversalCommand, CancellationToken)` and, in `Persistence/PostingCommand.cs`, `ReversalCommand(Guid OriginalDocumentId, IReadOnlyList<LedgerMovement> Original, string IdempotencyKey, byte[] RequestHash, Guid PostedBy)`, `AlreadyReversedOutcome(Guid ReversalDocumentId)` and `ReversalNotLatestOutcome(Guid ItemId, Guid LocationId, Guid BlockingDocumentId)`. Prompt 02 added `RequestHash.Reversal(Guid)`, `Problems.ReversalNotLatest/AlreadyReversed/CannotReverseReversal`, and `DocumentResponse.ReversesDocumentId`.
- Layout (TASKS IMS-5 Downstream): `Features/<Area>/<Endpoint>/{Endpoint,Handler}.cs`, plain scoped handlers, `internal`. Namespace `Inventory.Features.Reversals.ReverseDocument`. There is no `Request.cs`: the body is empty (spec §9), and T10 scans `*Request` types for dates.
- **`Endpoint.cs`**, copying `Features/Posting/PostAdjustment/Endpoint.cs:7-15`:
  ```csharp
  internal static class ReverseDocumentEndpoint
  {
      /// <summary>Admin only (spec §9); Staff gets 403. The body is empty and never bound.</summary>
      public static RouteHandlerBuilder MapReverseDocument(this RouteGroupBuilder documents) => documents.MapPost(
          "/{id:guid}/reversal",
          (Guid id, ReverseDocumentHandler handler, HttpContext http, CancellationToken cancellationToken) =>
              handler.HandleAsync(id, http, cancellationToken))
          .RequireAuthorization(InventoryPolicies.Admin);
  }
  ```
- **`Handler.cs`**: `internal sealed class ReverseDocumentHandler(InventoryDbContext db, PostingEngine engine, ICurrentUser user)` (`Kernel.Tenancy.ICurrentUser`, as `Features/Posting/DocumentPoster.cs:19`). Summary: the reversal route (spec §6 reversal rules, plan D3), with the order fixed so a retry of a successful reversal replays even after the state changed.
  `public async Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(Guid id, HttpContext http, CancellationToken ct)`:
  1. Key, exactly as `DocumentPoster.cs:36-46`: `var errors = new Dictionary<string, string[]>(); var key = PostingValidation.IdempotencyKey(http.Request.Headers["Idempotency-Key"], errors, out var missing); if (missing) return Problems.IdempotencyKeyMissing(); if (key is null) return Problems.Validation(errors);`
  2. `var hash = RequestHash.Reversal(id);` then `if (await engine.FindExistingAsync(key, hash, ct) is { } existing) return ToResult(http, existing);`
  3. `var original = await db.StockDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct); if (original is null) return Problems.NotFound();` — through EF, so the named tenant filter and RLS make another tenant's id invisible: `404` (spec §8).
  4. `if (original.Type == StockDocumentType.Reversal) return Problems.CannotReverseReversal(original.ReversesDocumentId ?? throw new InvalidOperationException("A reversal document has ReversesDocumentId (CK_StockDocuments_Reversal)."));`
  5. `var reversal = await db.StockDocuments.AsNoTracking().Where(d => d.ReversesDocumentId == id).Select(d => (Guid?)d.Id).SingleOrDefaultAsync(ct); if (reversal is { } reversalId) return Problems.AlreadyReversed(reversalId);` (comment: a racing reversal is caught at step 4 by `UX_StockDocuments_Reverses`, plan D5).
  6. `var movements = await db.StockMovements.AsNoTracking().Where(m => m.DocumentId == id).OrderBy(m => m.Id).Select(m => new LedgerMovement(m.Id, m.DocumentId, m.LineNo, m.ItemId, m.FromLocationId, m.ToLocationId, m.Quantity, m.TotalCost, m.ReversesMovementId)).ToListAsync(ct);`
  7. `return ToResult(http, await engine.ReverseAsync(new ReversalCommand(id, movements, key, hash, user.UserId), ct));`
  `private static … ToResult(HttpContext http, PostingOutcome outcome)`, a `switch` modelled on `DocumentPoster.cs:151-183`:
  - `PostedOutcome { Replayed: false } posted` → `TypedResults.Created($"/api/documents/{posted.Document.Id}", DocumentResponse.From(posted))`.
  - `PostedOutcome replayed` → set `Idempotent-Replayed: true`, `TypedResults.Ok(DocumentResponse.From(replayed))`.
  - `KeyReusedOutcome r` → `Problems.IdempotencyKeyReused(r.OriginalDocumentId)`.
  - `AlreadyReversedOutcome a` → `Problems.AlreadyReversed(a.ReversalDocumentId)`.
  - `ReversalNotLatestOutcome n` → `Problems.ReversalNotLatest(n.ItemId, n.LocationId, n.BlockingDocumentId)`.
  - `InactiveItemOutcome i` → `Problems.InactiveItem(i.ItemId)`; `InactiveLocationOutcome l` → `Problems.InactiveLocation(l.LocationId)`.
  - `RejectedOutcome { Rejection: InsufficientStockException e }` → `Problems.InsufficientStock(e.ItemId, e.LocationId, e.Available, e.Requested)` (reachable once S10's toggle exists).
  - `RetriesExhaustedOutcome` → `Problems.ConcurrencyConflict()`.
  - `default` → `throw new InvalidOperationException($"Unexpected reversal outcome: {outcome}.")` (a reversal never yields `BalanceOutOfRangeOutcome` or `UnitCostRequired`).
  Usings: `Inventory.Domain`, `Inventory.Features.Posting`, `Inventory.Persistence`, `Kernel.Tenancy`, `Microsoft.AspNetCore.Http`, `Microsoft.AspNetCore.Http.HttpResults`, `Microsoft.EntityFrameworkCore`.
- **`Features/Posting/DocumentEndpoints.cs`** — add `using Inventory.Features.Reversals.ReverseDocument;` and `documents.MapReverseDocument();` after `MapPostAdjustment()`. Route raw text becomes `/api/documents/{id:guid}/reversal`.
- **`InventoryModuleExtensions.cs`** — `builder.Services.AddScoped<ReverseDocumentHandler>();` after `AddScoped<PostAdjustmentHandler>()` (`:66`), with the using.
- **`tests/Inventory.IntegrationTests/TenantIsolationTests.cs`** — add a probe after the adjustment probe (`:596-…`), copying `Another_tenants_item_cannot_be_deactivated` (`:343-361`):
  ```csharp
  [Fact]
  [ProbesRoute("POST /api/documents/{id:guid}/reversal")]
  public async Task Another_tenants_document_cannot_be_reversed()
  ```
  Tenant B (Admin) creates a location and item (`LocationsTests.CreateAsync`, `ItemsTests.CreateAsync`) and posts a receipt of 5 @ 10 (`PostingTests.PostDocumentAsync(clientB, PostingTests.Receipts, PostingTests.NewKey(), PostingTests.Receipt(locationB, PostingTests.Line(itemB, 5m, 10m)), ct)`, `201`; read its `id`). Tenant A (Admin) sends `POST /api/documents/{idB}/reversal` with a fresh `Idempotency-Key` and no body (`new HttpRequestMessage(HttpMethod.Post, $"/api/documents/{idB}/reversal")`, add the header, `SendAsync`). Assert `404` with type `/problems/not-found`; then on `LedgerRows.OpenAsync(db, b, ct)`: B's balance is still `(5m, 50m)` (`PostingTests.BalanceAsync`) and `SELECT COUNT(*) FROM inventory.StockDocuments WHERE Type = 'Reversal'` is 0.

## Do not
- Touch files outside the `files` list (no engine, no `DocumentPoster`, no web). If you must, stop and report why.
- Read a tenant id from the request, add a request body type, or add a date anywhere.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests' --filter-class '*PostingTests'
dotnet test --project tests/Architecture.Tests
dotnet test            # full suite
git diff --stat        # only the five declared files
```
- The T5 route guard passes with the new route probed.
