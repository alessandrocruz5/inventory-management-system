---
type: prompt
plan: "[[2026-11-09-s5c-posting-engine]]"
id: 01
unit: IMS-8
status: done
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Posting/PostReceipt/Request.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostIssue/Request.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostingValidation.cs
    - src/Modules/Inventory/Inventory/Features/Posting/RequestHash.cs
    - tests/Inventory.UnitTests/PostingValidationTests.cs
    - tests/Inventory.UnitTests/RequestHashTests.cs
  modify: []
  delete: []
---
# 01 · Request contract, step-1 validation, RequestHash

## Task
Create the receipt and issue request DTOs, along with two pure helpers and their unit tests:
- `PostingValidation`, which runs the pure step-1 checks of spec §6, normalizes the request and checks the `Idempotency-Key` header.
- `RequestHash`, which writes the canonical JSON and returns its SHA-256.

Nothing calls these helpers yet; prompt 04 wires them in. Don't touch the database or any endpoint.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-8`. Don't switch it.
- **Prerequisite:** run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any `dotnet` command, because restore needs it on this machine.
- **Conventions:**
  - .NET 10, `Nullable` on, `TreatWarningsAsErrors`.
  - Everything here is `internal sealed` (T12: a module exposes only `InventoryModuleExtensions` as public).
  - Money and quantities are `decimal`, never `double` or `float`.
  - Rounding is `Inventory.Domain.CostingPolicy.Round4` (`decimal.Round(v, 4, MidpointRounding.AwayFromZero)`). Reuse it; don't write a new one.
  - Match the existing style in `src/Modules/Inventory/Inventory/Features/Items/CreateItem/Request.cs` (records with nullable members) and `Features/Items/ItemRules.cs` (pure normalization that fills an `errors` dictionary).
- **Unit tests:** `tests/Inventory.UnitTests` uses xUnit v3 (`[Fact]`, `[Theory]`, `[InlineData]`) and Shouldly, and already has `InternalsVisibleTo`. Copy the file style of `tests/Inventory.UnitTests/CostingPolicyTests.cs`.

### DTOs
Bodies from spec §9. Nullable members, so a missing field is a `400 validation`, not a binding failure.

```csharp
// Features/Posting/PostReceipt/Request.cs
namespace Inventory.Features.Posting.PostReceipt;
internal sealed record PostReceiptRequest(Guid? LocationId, string? Reference, string? Notes, IReadOnlyList<PostReceiptLine?>? Lines);
internal sealed record PostReceiptLine(Guid? ItemId, decimal? Quantity, decimal? UnitCost);

// Features/Posting/PostIssue/Request.cs
namespace Inventory.Features.Posting.PostIssue;
internal sealed record PostIssueRequest(Guid? LocationId, string? Reference, string? Notes, IReadOnlyList<PostIssueLine?>? Lines);
internal sealed record PostIssueLine(Guid? ItemId, decimal? Quantity);
```
No property is a date or time (spec §6: no request carries a date; T10 checks this by reflection in prompt 06).

### `Features/Posting/PostingValidation.cs` (namespace `Inventory.Features.Posting`)
```csharp
internal sealed record ValidatedLine(int LineNo, Guid ItemId, decimal Quantity, decimal? UnitCost);   // LineNo = index + 1
internal sealed record ValidatedPosting(Guid LocationId, string? Reference, string? Notes, IReadOnlyList<ValidatedLine> Lines);

internal static class PostingValidation
{
    public const decimal MaxAmount = 999_999_999.9999m;               // per quantity and per unit cost (spec §6 step 1)
    public const decimal MaxColumn = 999_999_999_999_999.9999m;       // decimal(19,4) max: line totals and balances (plan D1)
    public const int MaxLines = 100;

    // Returns null and fills errors when invalid.
    public static ValidatedPosting? Receipt(PostReceiptRequest request, Dictionary<string, string[]> errors);
    public static ValidatedPosting? Issue(PostIssueRequest request, Dictionary<string, string[]> errors);

    // Header check. Returns the key, or null with missing = true (→ idempotency-key-missing) or an errors["Idempotency-Key"] entry (→ validation).
    public static string? IdempotencyKey(string? header, Dictionary<string, string[]> errors, out bool missing);
}
```

**Rules.** Error keys are camelCase JSON paths with zero-based indexes, like S4's `errors["sku"]`. Each key carries one short, plain-English message.
- `locationId`: required, and not `Guid.Empty`.
- `reference`: trimmed, and empty becomes null; at most 100 characters. `notes`: the same, at most 500 (spec §5 column sizes).
- `lines`: required, with 1–100 entries; otherwise `errors["lines"]`. A null entry gives `errors["lines[i]"]`.
- `lines[i].itemId`: required, and not `Guid.Empty`. An item already on an earlier line gives `lines[i].itemId` "This item is already on another line." Each item appears at most once per document.
- `lines[i].quantity`: required, `> 0`, `<= MaxAmount`, and at most 4 dp, tested by value: `CostingPolicy.Round4(q) == q`, so `10.00000` passes.
- `lines[i].unitCost` (receipts only): required, `>= 0`, `<= MaxAmount`, at most 4 dp. **D1:** if `CostingPolicy.Round4(q * c) > MaxColumn`, then `lines[i].unitCost` gets "The line total is too large." Only check this when the quantity and cost are each valid.
- Collect every error; don't stop at the first. Return null if there are any.
- Header: null, empty or whitespace sets `missing = true` and returns null. Otherwise the length must be ≤ 64 and every char in `0x21..0x7E`, or `errors["Idempotency-Key"]` gets "Use 1–64 printable ASCII characters without spaces." Use the key as sent: don't trim or case-fold it. The database compares keys case-insensitively (the column's default collation), so write no test that expects `abc` and `ABC` to be different keys.

### `Features/Posting/RequestHash.cs` (namespace `Inventory.Features.Posting`)
```csharp
internal static class RequestHash
{
    public const string ReceiptsRoute = "/api/documents/receipts";
    public const string IssuesRoute = "/api/documents/issues";

    public static byte[] Receipt(ValidatedPosting posting);   // pre-image route = ReceiptsRoute, lines with unitCost
    public static byte[] Issue(ValidatedPosting posting);     // pre-image route = IssuesRoute, lines without unitCost

    internal static string CanonicalJson(ValidatedPosting posting, bool withUnitCost);  // exposed for tests
    internal static string FormatDecimal(decimal value);                                // exposed for tests
}
```
The canonical form is Hidden coupling (CLAUDE.md) and the plan's "Canonical form" decision:
- The pre-image is `Encoding.UTF8.GetBytes($"POST {route}\n{json}")`, and the hash is `SHA256.HashData(...)`, 32 bytes, which fits `RequestHash binary(32)`.
- Write `json` with `Utf8JsonWriter` (`Indented = false`). Write every property every time, in this order, with `WriteNull` for null:
  - Receipt: `{"locationId","reference","notes","lines":[{"itemId","quantity","unitCost"}]}`
  - Issue: `{"locationId","reference","notes","lines":[{"itemId","quantity"}]}`
- Guids: `ToString("D")`, which is lowercase.
- Strings are the normalized values from `ValidatedPosting`.
- Decimals go through `WriteRawValue(FormatDecimal(v))`. `FormatDecimal` strips trailing zeros and uses the invariant culture with no exponent: `10.0000m` → `10`, `0.50m` → `0.5`, `0m` → `0`, `100m` → `100`, `999999999.9999m` → `999999999.9999`. One way to do it: `(value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture)`, then confirm `100m` stays `100`. Test it.
- Lines keep their `LineNo` order.

### Tests
- **`PostingValidationTests`:**
  - One test per rule above, asserting the exact error key.
  - A valid receipt and issue normalize `reference: "  DR-1 "` → `"DR-1"` and `notes: "   "` → null.
  - `LineNo` is 1-based.
  - 100 lines pass and 101 fail.
  - `quantity` `0.00001` fails and `1.50000` passes.
  - `unitCost` `0` passes.
  - D1: `quantity 999999999.9999`, `unitCost 999999999.9999` → `lines[0].unitCost`.
  - Every header case: missing, whitespace, 64 chars passes, 65 fails, a space inside fails, `é` fails.
- **`RequestHashTests`:**
  - Exact `CanonicalJson` strings for one receipt and one issue (assert the literal).
  - Equal hashes for `10` vs `10.0000` and for `" DR-1"` vs `"DR-1"` (after validation).
  - Different hashes for a changed quantity, a reordered line pair, a different `locationId`, and receipt vs issue on the same lines without cost (different route).
  - The hash length is 32.
  - The `FormatDecimal` table above.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Reference `Inventory.Persistence` or EF from these files.
- Add a package, a Kernel helper, or a second rounding function.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests
dotnet test --project tests/Architecture.Tests
```
- Every new test passes, and the existing 63 unit tests still pass.
