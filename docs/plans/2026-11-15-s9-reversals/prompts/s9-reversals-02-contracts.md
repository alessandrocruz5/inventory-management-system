---
type: prompt
plan: "[[2026-11-15-s9-reversals]]"
id: 02
unit: IMS-13
status: todo
model: opus
effort: high
depends_on: []
parallel_group: B
files:
  create: []
  modify:
    - src/Shared/Kernel/Problems/ProblemTypes.cs
    - src/Modules/Inventory/Inventory/Features/Problems.cs
    - src/Modules/Inventory/Inventory/Features/Posting/RequestHash.cs
    - src/Modules/Inventory/Inventory/Features/Posting/DocumentResponse.cs
    - tests/Inventory.UnitTests/RequestHashTests.cs
    - "docs/Inventory System — Sprint 1 Spec.md"
  delete: []
---
# 02 · Reversal contracts: slugs, problems, hash, response

## Task
Add the three reversal problem types (Kernel constants, Inventory `Problems` results, spec §9 row for the new one), the reversal `RequestHash` form, and `reversesDocumentId` on the posting response. All additive; nothing calls them until prompt 03.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-13`. `Kernel` never references EF Core, ASP.NET Core or SqlClient (TASKS IMS-2 Downstream): its problem types are string constants.
- Plan decisions: **D2** reversing a reversal is `422 cannot-reverse-reversal` with `reversesDocumentId`. **D4** the reversal hash is SHA-256 over UTF-8 `"POST /api/documents/{id}/reversal\n{}"` with `id` lowercase `"D"`. **D10** `DocumentResponse` gains `ReversesDocumentId`.
- Hidden coupling (CLAUDE.md): the `RequestHash` canonical form must match what the client sends, and changing an existing form breaks every retry of it. **Do not touch the four existing forms** (`Receipt`, `Issue`, `Transfer`, `Adjustment`, `CanonicalJson`, `TransferCanonicalJson`, `AdjustmentCanonicalJson`).
- **`src/Shared/Kernel/Problems/ProblemTypes.cs`** — after `InactiveLocation` (`:37`), add, in the file's style:
  ```csharp
  /// <summary>A later posting touched a balance of the document being reversed (spec §6): post a correcting document instead.</summary>
  public const string ReversalNotLatest = "reversal-not-latest";

  /// <summary>The document already has a reversal (spec §6; <c>UX_StockDocuments_Reverses</c>).</summary>
  public const string AlreadyReversed = "already-reversed";

  /// <summary>The document is itself a reversal, which can't be reversed (spec §6).</summary>
  public const string CannotReverseReversal = "cannot-reverse-reversal";
  ```
- **`src/Modules/Inventory/Inventory/Features/Problems.cs`** — after `InactiveLocation` (`:62-66`), copying `IdempotencyKeyReused` (`:38-42`) and `InsufficientStock` (`:44-54`):
  - `ReversalNotLatest(Guid itemId, Guid locationId, Guid blockingDocumentId)` → `409`, title `"A later posting changed this stock"`, extensions `itemId`, `locationId`, `blockingDocumentId`.
  - `AlreadyReversed(Guid reversalDocumentId)` → `409`, title `"Document already reversed"`, extension `reversalDocumentId`.
  - `CannotReverseReversal(Guid reversesDocumentId)` → `422`, title `"A reversal can't be reversed"`, extension `reversesDocumentId`.
- **`src/Modules/Inventory/Inventory/Features/Posting/RequestHash.cs`**:
  - Extend the class summary by one sentence: a reversal hashes `"POST /api/documents/{id}/reversal\n{}"`: its body is empty, so its canonical form is `{}` and the route carries the document id.
  - Add `public const string ReversalBody = "{}";` and
    ```csharp
    /// <summary>The route a reversal of <paramref name="documentId"/> is posted to, as the hash names it (lowercase "D").</summary>
    public static string ReversalRoute(Guid documentId) => $"/api/documents/{documentId:D}/reversal";

    /// <summary>The 32-byte hash of a reversal of <paramref name="documentId"/> (plan D4).</summary>
    public static byte[] Reversal(Guid documentId) => Hash(ReversalRoute(documentId), ReversalBody);
    ```
    `Guid.ToString("D")` is lowercase.
- **`src/Modules/Inventory/Inventory/Features/Posting/DocumentResponse.cs`** — add `Guid? ReversesDocumentId` to `DocumentResponse` right after `string Type`, filled from `outcome.Document.ReversesDocumentId`. Update the summary: "every posting route, reversals included". `MovementResponse` already carries `ReversesMovementId`.
- **`tests/Inventory.UnitTests/RequestHashTests.cs`** — copy `Receipt_hash_is_sha256_of_method_route_and_canonical_json` (`:95-102`). Add:
  - `Reversal_hash_is_sha256_of_method_route_and_empty_body`: for `Loc` (`0F8FAD5B-…`), `RequestHash.Reversal(Loc)` equals `SHA256.HashData(Encoding.UTF8.GetBytes("POST /api/documents/0f8fad5b-d9cb-469f-a165-70867728950e/reversal\n{}"))` (write the literal; it pins lowercase and the `{}` body).
  - `Reversals_of_different_documents_hash_differently`: `Reversal(Loc)` ≠ `Reversal(OtherLoc)`.
  - `A_reversal_never_hashes_like_a_receipt`: `Reversal(Loc)` ≠ `ReceiptHash(Loc, null, new PostReceiptLine(ItemA, 1m, 1m))`.
- **`docs/Inventory System — Sprint 1 Spec.md`** §9 problem-types table (`:578-590`): add one row after `already-reversed` (`:585`): `| \`cannot-reverse-reversal\` | 422 | \`reversesDocumentId\` |`. Change nothing else in the spec (not §6).

## Do not
- Touch files outside the `files` list, or any existing hash form, problem method or constant.
- Add `PROBLEM_MESSAGES` entries here (prompt 06 does, in the same PR).
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests --filter-class '*RequestHashTests'
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingTests' --filter-class '*PostingConcurrencyTests' --filter-class '*SeedTests'
dotnet test --project tests/Architecture.Tests
git diff --stat        # only the six declared files
```
- The existing `RequestHashTests` pass unchanged (the four forms are untouched).
