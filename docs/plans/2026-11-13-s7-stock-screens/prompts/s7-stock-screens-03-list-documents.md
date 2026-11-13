---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 03
unit: IMS-11
status: todo
model: sonnet
effort: high
depends_on: [02]
parallel_group: C
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Documents/DocumentQueryEndpoints.cs
    - src/Modules/Inventory/Inventory/Features/Documents/DocumentSummaryResponse.cs
    - src/Modules/Inventory/Inventory/Features/Documents/ListDocuments/Endpoint.cs
    - src/Modules/Inventory/Inventory/Features/Documents/ListDocuments/Handler.cs
    - src/Modules/Inventory/Inventory/Features/Documents/ListDocuments/Request.cs
    - tests/Inventory.IntegrationTests/DocumentQueryTests.cs
  modify:
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - CLAUDE.md
    - tests/Inventory.IntegrationTests/PostingClockTests.cs
    - tests/Inventory.IntegrationTests/TenantIsolationTests.cs
  delete: []
---
# 03 · `GET /documents` and the T10 exemption

## Task
Add `GET /api/documents`: the tenant's documents, newest first, paged, filtered by `type` and by a half-open posted-at range. Give T10 a named exemption for the two filter properties, add the T5 probe, and create `DocumentQueryTests`.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11`. .NET 10 minimal APIs, everything `internal`, `TreatWarningsAsErrors` on.
- Copy the list pattern from `Features/Items/ListItems/{Endpoint,Handler,Request}.cs` and `Features/Items/ItemEndpoints.cs:15-24`, and use `Features/Paging.cs` (`Paging.Resolve`, `ToPageAsync`, `PagedResponse<T>`) and `Features/Problems.cs:15` (`Problems.Validation`).
- **Plan D5:** don't touch `Features/Posting/DocumentEndpoints.cs` (high-stakes glob). Create `Features/Documents/DocumentQueryEndpoints.cs`:
  ```csharp
  internal static class DocumentQueryEndpoints
  {
      /// <summary>Maps the read side of <c>/documents</c>; the posting routes share the prefix from Features/Posting/DocumentEndpoints.</summary>
      public static IEndpointRouteBuilder MapDocumentQueryEndpoints(this IEndpointRouteBuilder api)
      {
          var documents = api.MapGroup("/documents");
          documents.MapListDocuments();
          return api;
      }
  }
  ```
  Prompt 04 adds `MapGetDocument()` here.
- `ListDocuments/Request.cs`: `internal sealed record ListDocumentsRequest(string? Type, DateTimeOffset? PostedFrom, DateTimeOffset? PostedTo, int? Page, int? PageSize);`. `Type` is a string, so an unknown value is a `400` on `type`, not a binding failure.
- `DocumentSummaryResponse.cs`:
  ```csharp
  internal sealed record DocumentSummaryResponse(Guid Id, string Type, string? ReasonCode, string? Reference, DateTimeOffset PostedAt,
      Guid PostedBy, int LineCount, Guid? ReversesDocumentId, Guid? ReversedByDocumentId);
  ```
- `ListDocuments/Handler.cs` (`ListDocumentsHandler(InventoryDbContext db)`, returns `Results<Ok<PagedResponse<DocumentSummaryResponse>>, ValidationProblem>`):
  - `Paging.Resolve`.
  - `type`: when present, it must equal one of `Enum.GetNames<StockDocumentType>()` **exactly** (ordinal, untrimmed), else `errors["type"] = [$"Choose one of: {string.Join(", ", names)}."]`. Copy `Features/Posting/PostingValidation.cs:305-330`: not `Enum.TryParse`, which accepts `"1"` and, ignoring case, `"receipt"`. `StockDocumentType` is in `Inventory.Persistence` (Receipt, Issue, Transfer, Adjustment, Reversal).
  - When both dates are present and `PostedTo <= PostedFrom`: `errors["postedTo"] = ["The end must be after the start."]`.
  - Query `db.StockDocuments.AsNoTracking()`. Filter `Type == parsed`, `PostedAt >= PostedFrom`, `PostedAt < PostedTo` (half-open, plan D6). Order `PostedAt` descending, then `Id` descending; `IX_StockDocuments_PostedAt (TenantId, PostedAt)` from prompt 01 serves this.
  - Project in SQL: `LineCount = db.StockMovements.Count(m => m.DocumentId == d.Id && m.LineNo < 1000)` (a revaluation movement is `LineNo` 1000 + n, spec §5). `ReversedByDocumentId = db.StockDocuments.Where(r => r.ReversesDocumentId == d.Id).Select(r => (Guid?)r.Id).FirstOrDefault()` (unique by `UX_StockDocuments_Reverses`). `Type = d.Type.ToString()` and `ReasonCode = d.ReasonCode.ToString()` can't translate to SQL reliably, so project the enums and convert after `ToPageAsync`, or select the raw values and map in C#. Either way, `ToPageAsync` must run on the ordered query.
  - Never call `IgnoreQueryFilters`. Tenant scoping comes from the named filter and RLS.
- `ListDocuments/Endpoint.cs`: `MapListDocuments(this RouteGroupBuilder documents)` → `documents.MapGet("", ([AsParameters] ListDocumentsRequest request, ListDocumentsHandler handler, CancellationToken ct) => handler.HandleAsync(request, ct))`. Any signed-in role.
- `InventoryModuleExtensions.cs`: `AddScoped<ListDocumentsHandler>()` after the last handler line, and `endpoints.MapDocumentQueryEndpoints();` after `MapStockEndpoints()` (added by prompt 02).
- **T10, plan D1 (user decision).** `tests/Inventory.IntegrationTests/PostingClockTests.cs:68-87` (`No_request_type_carries_a_date`) fails on any `*Request`/`*Line` type in `Inventory.Features*` with a date property. Change it as follows, keeping everything else:
  - Add a static map with a reason per entry, above the fact:
    ```csharp
    // Read filters bind a date to narrow a list; nothing stores it. Every other request date is banned (CLAUDE.md review constraint).
    private static readonly Dictionary<string, string> AllowedDates = new()
    {
        ["Inventory.Features.Documents.ListDocuments.ListDocumentsRequest.PostedFrom"] = "read filter; never stored",
        ["Inventory.Features.Documents.ListDocuments.ListDocumentsRequest.PostedTo"] = "read filter; never stored",
    };
    ```
  - `types.ShouldContain(typeof(ListDocumentsRequest));` next to the existing two `ShouldContain`s.
  - Split `dated` into: `dated.Where(d => !AllowedDates.ContainsKey(d)).ShouldBeEmpty();` and `AllowedDates.Keys.ShouldBeSubsetOf(dated)`, so a stale or renamed entry fails.
  - Keep `PostedAt_comes_from_the_clock` unchanged.
- **`CLAUDE.md`** (plan D1; audit FIX 3, approved by the user 2026-11-13). In `## Review constraints`, replace the line `- PostedAt comes only from TimeProvider; no request carries a date.` with `- PostedAt comes only from TimeProvider; no write request carries a date (T10 allowlists read filters, IMS-11 D1).` Change nothing else in `CLAUDE.md`, and don't edit the spec. Without this, code-guardian reads the old line literally and flags the two filter properties as a Blocker.
- **T5.** In `TenantIsolationTests.cs` (pattern `:127-150`), add:
  ```
  [Fact]
  [ProbesRoute("GET /api/documents/")]
  public async Task Each_tenant_lists_only_its_own_documents()
  ```
  Each of two tenants (Admin users) creates a location and an item, and posts one receipt (`PostingTests.ReceiveAsync`). Each tenant's `GET /api/documents` returns exactly its own single document id, with `total` 1.
- **`tests/Inventory.IntegrationTests/DocumentQueryTests.cs`** (new; `public sealed class DocumentQueryTests(DatabaseFixture db)`). Copy `PostingClockTests.cs:22-46` for a controllable clock: `var fake = new FakeTimeProvider(t1); await using var app = new ApiFactory(db.AppConnectionString) { Clock = fake };`, then `fake.SetUtcNow(t2)` between posts. Post through the API with `PostingTests` helpers (`PostDocumentAsync`, `Receipts`, `Issues`, `Receipt`, `Line`, `Issue`, `IssueLine`, `NewKey`, `ReceiveAsync`, `NewLocationAsync`, `NewItemAsync`, `AddUserAsync`, `AssertValidationAsync`), `AdjustmentTests.Adjustments`/`Adjustment(location, reasonCode, lines)`/`Delta(itemId, quantityDelta, unitCost)`, and `TransferTests.Transfers`/`Transfer(from, to, lines)`/`TransferLine`. Build query strings with `Uri.EscapeDataString(t.ToString("O"))`, because a raw `+` reads as a space. Facts:
  1. `Lists_newest_first`: three receipts at t1 < t2 < t3 list t3, t2, t1.
  2. `Posted_range_is_half_open`: `?postedFrom=t2` → t3, t2; `?postedTo=t3` → t2, t1; both → t2 only.
  3. `Type_filter_matches_exact_names`: one receipt and one issue; `?type=Issue` → the issue only. `?type=issue` and `?type=1` → `400` on `type`.
  4. `Range_end_must_follow_start`: `?postedFrom=t2&postedTo=t2` → `400` on `postedTo`.
  5. `Line_count_and_reason_code_are_listed`: a 2-line receipt shows `lineCount` 2 and `reasonCode` null. An Admin adjustment (`"Damaged"`, one `Delta(item, -1m)`) shows `reasonCode` `"Damaged"` and `type` `"Adjustment"`.
  6. `Reversal_links_point_both_ways`: post a receipt through the API, then insert a reversal row: `await using var c = await LedgerRows.OpenAsync(db, tenant, ct); var reversal = await LedgerRows.AddDocumentAsync(c, ct, type: "Reversal", reverses: receiptId);`. The list shows the receipt with `reversedByDocumentId == reversal` and the reversal with `reversesDocumentId == receiptId`. S9's endpoint doesn't exist yet; this row is valid under `CK_StockDocuments_Reversal`.
  7. `Paging_is_validated`: `?pageSize=0` → `400` on `pageSize`.
  8. `Staff_can_list_documents` → `200`.
- Known flake: if Testcontainers crashes on start, rerun with `-- --max-parallel-test-modules 1`.

## Do not
- Touch `Features/Posting/**`, `Persistence/**` or `Domain/**`.
- Weaken T10 beyond the two named properties, or rename the `ListDocumentsRequest` type so the scan misses it.
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*DocumentQueryTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests'
dotnet test
```
- `git diff CLAUDE.md` shows exactly the one reworded constraint line.
- T10 is green. It goes red if you temporarily add a `DateTimeOffset?` property to `ListStockRequest` (check, then revert).
- The 8 `DocumentQueryTests` facts and the T5 route guard pass.
