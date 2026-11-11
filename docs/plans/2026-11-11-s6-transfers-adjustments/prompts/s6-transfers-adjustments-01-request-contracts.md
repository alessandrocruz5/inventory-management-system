---
type: prompt
plan: "[[2026-11-11-s6-transfers-adjustments]]"
id: 01
unit: IMS-10
status: todo
model: opus
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - src/Modules/Inventory/Inventory/Features/Posting/PostTransfer/Request.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostAdjustment/Request.cs
  modify:
    - src/Modules/Inventory/Inventory/Features/Posting/PostingValidation.cs
    - src/Modules/Inventory/Inventory/Features/Posting/RequestHash.cs
    - tests/Inventory.UnitTests/PostingValidationTests.cs
    - tests/Inventory.UnitTests/RequestHashTests.cs
  delete: []
---
# 01 · Transfer and adjustment request contracts: step 1 and canonical hash

## Task
Add the request records, the pure step-1 validation and the canonical `RequestHash` for `POST /api/documents/transfers` and `POST /api/documents/adjustments`. Don't add routes, handlers or poster changes; prompts 02 and 03 wire them. The receipt and issue behaviour, their error keys and messages, and their canonical JSON must stay **byte-for-byte unchanged**.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-10`. Don't switch it. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any `dotnet` command.
- **Conventions:** .NET 10, `Nullable` on, `TreatWarningsAsErrors`. Everything `internal`. Quantities and costs are `decimal`, never `double`/`float`. Rounding is only `Inventory.Domain.CostingPolicy.Round4` (4 dp, `MidpointRounding.AwayFromZero`). No new packages.
- **Spec bodies (§9):**
  ```json
  POST /api/documents/transfers
  { "fromLocationId": "…", "toLocationId": "…", "notes": null,
    "lines": [ { "itemId": "…", "quantity": 4 } ] }

  POST /api/documents/adjustments
  { "locationId": "…", "reasonCode": "Damaged", "notes": "water damage",
    "lines": [ { "itemId": "…", "quantityDelta": -2 },
               { "itemId": "…", "quantityDelta": 5, "unitCost": 42.50 } ] }
  ```
  Plan decision: both also accept an optional `reference`, as receipts and issues do.

### 1. Request records (create)
Copy the shape of `Features/Posting/PostIssue/Request.cs`: a nullable positional record per body and per line, in the folder's namespace.
- `Features/Posting/PostTransfer/Request.cs`, namespace `Inventory.Features.Posting.PostTransfer`:
  `internal sealed record PostTransferRequest(Guid? FromLocationId, Guid? ToLocationId, string? Reference, string? Notes, IReadOnlyList<PostTransferLine?>? Lines);`
  `internal sealed record PostTransferLine(Guid? ItemId, decimal? Quantity);`
- `Features/Posting/PostAdjustment/Request.cs`, namespace `Inventory.Features.Posting.PostAdjustment`:
  `internal sealed record PostAdjustmentRequest(Guid? LocationId, string? ReasonCode, string? Reference, string? Notes, IReadOnlyList<PostAdjustmentLine?>? Lines);`
  `internal sealed record PostAdjustmentLine(Guid? ItemId, decimal? QuantityDelta, decimal? UnitCost);`
  `ReasonCode` is `string?`, not the enum, so a bad value is a `400` with `errors.reasonCode` and never a JSON binding failure.

### 2. `PostingValidation.cs` (modify)
Today (read it first):
- `:12` `internal sealed record ValidatedLine(int LineNo, Guid ItemId, decimal Quantity, decimal? UnitCost);`
- `:15` `internal sealed record ValidatedPosting(Guid LocationId, string? Reference, string? Notes, IReadOnlyList<ValidatedLine> Lines);`
- `:38` `Receipt(...)`, `:49` `Issue(...)`, `:65` `IdempotencyKey(...)`, `:91-166` the private generic `Validate<TLine>`, `:169` `Text`, `:186` `Amount`.
- Constants: `MaxAmount = 999_999_999.9999m`, `MaxColumn = 999_999_999_999_999.9999m`, `MaxLines = 100`.

Changes:
- **`ValidatedPosting` (plan D2):** keep the positional signature exactly (tests and the seeder construct it positionally), and add two init-only properties with XML docs:
  - `public Guid? ToLocationId { get; init; }`: transfers only, the destination. `LocationId` is then the source.
  - `public AdjustmentReason? ReasonCode { get; init; }`: adjustments only. `Inventory.Persistence.AdjustmentReason` is `{ Damaged, Lost, Found, CountCorrection, Other }`.
- **`ValidatedLine`:** keep its shape. Update its XML doc: `Quantity` is > 0, except on an adjustment, where it is the request's signed `quantityDelta` (never 0). `DocumentPoster` turns it into a direction (prompt 02).
- **`public static ValidatedPosting? Transfer(PostTransferRequest request, Dictionary<string, string[]> errors)`:**
  - `fromLocationId` null or `Guid.Empty` → `errors["fromLocationId"] = ["Choose the location to move stock from."]`.
  - `toLocationId` null or `Guid.Empty` → `errors["toLocationId"] = ["Choose the location to move stock to."]`.
  - Both valid and equal → `errors["toLocationId"] = ["Choose a different location to move stock to."]`.
  - `reference`, `notes` and `lines`: exactly as `Issue` does today (same keys, messages, 100-line cap, duplicate-item rule, quantity rules, no unit cost).
  - Result: `new ValidatedPosting(from, reference, notes, lines) { ToLocationId = to }`.
- **`public static ValidatedPosting? Adjustment(PostAdjustmentRequest request, Dictionary<string, string[]> errors)`:**
  - `locationId`: as `Receipt` (`errors["locationId"] = ["Location is required."]`).
  - `reasonCode`: null, empty or whitespace → `errors["reasonCode"] = ["Choose a reason."]`. Otherwise it must equal one of `Enum.GetNames<AdjustmentReason>()` **exactly** (ordinal, case-sensitive, untrimmed), or `errors["reasonCode"] = ["Choose one of: Damaged, Lost, Found, CountCorrection, Other."]` (build the list from the enum names). **Don't use `Enum.TryParse`**: it accepts `"1"` and `"damaged"` with `ignoreCase`. Parse the matched name with `Enum.Parse<AdjustmentReason>`.
  - `reference`, `notes`, the line count, null lines and `itemId`: as receipts.
  - `lines[i].quantityDelta`: null → `"Quantity change is required."`; `0` → `"Quantity change must not be zero."`; `|v| > MaxAmount` → `"Quantity change must be between -999,999,999.9999 and 999,999,999.9999."` (format with `MaxAmount.ToString("N4", CultureInfo.InvariantCulture)` like `MaxAmountText`); more than 4 dp → `"Quantity change can have at most 4 decimal places."`.
  - `lines[i].unitCost`, when present: the receipt's `Amount(..., "Unit cost", allowZero: true, ...)` rules. A valid **negative** delta with a non-null unit cost → `errors["lines[i].unitCost"] = ["Leave the unit cost empty on a line that removes stock."]`. A valid positive delta with a valid cost whose `CostingPolicy.Round4(delta * cost) > MaxColumn` → `errors["lines[i].unitCost"] = ["The line total is too large."]` (plan D1). An absent cost on a positive line is valid: the engine uses the average or rejects at step 8.
  - Result: `new ValidatedPosting(location, reference, notes, lines) { ReasonCode = parsed }`, with each `ValidatedLine(i + 1, item, delta /* signed */, unitCost)`.
- **Implementation latitude:** you may generalize the private `Validate<TLine>` (for example a location-key parameter, or a per-line callback) or add a sibling private method. Either way the existing `Receipt`/`Issue` keys, messages and order of checks must not change. The existing `PostingValidationTests` are the guard, and you must not edit their existing cases.

### 3. `RequestHash.cs` (modify, plan D3; hidden coupling)
Today: `ReceiptsRoute`, `IssuesRoute`, `Receipt`, `Issue`, `internal static string CanonicalJson(ValidatedPosting posting, bool withUnitCost)`, `FormatDecimal`, private `Hash`, `WriteText`, `WriteDecimal`. **Leave `CanonicalJson`, both route constants and `Receipt`/`Issue` untouched.** A change there turns every stored key into a false `409 idempotency-key-reused`.

Add:
- `public const string TransfersRoute = "/api/documents/transfers";` and `public const string AdjustmentsRoute = "/api/documents/adjustments";`
- `public static byte[] Transfer(ValidatedPosting posting) => Hash(TransfersRoute, TransferCanonicalJson(posting));`
- `public static byte[] Adjustment(ValidatedPosting posting) => Hash(AdjustmentsRoute, AdjustmentCanonicalJson(posting));`
- `internal static string TransferCanonicalJson(ValidatedPosting posting)` writes, in this order: `fromLocationId` (`LocationId`), `toLocationId` (`ToLocationId`; throw `InvalidOperationException` if it is null, since that's a caller bug), `reference`, `notes`, `lines` [`itemId`, `quantity`].
- `internal static string AdjustmentCanonicalJson(ValidatedPosting posting)` writes, in this order: `locationId`, `reasonCode` (the enum name; throw if null), `reference`, `notes`, `lines` [`itemId`, `quantityDelta` (the signed `Quantity`), `unitCost` (null when absent)].
- Same rules as `CanonicalJson`: `Utf8JsonWriter`, not indented, every property always written, null for absent, Guids lowercase `"D"`, decimals through `WriteDecimal`/`FormatDecimal` (so `-2.0000m` → `-2`), lines `OrderBy(l => l.LineNo)`. The new methods may reuse the existing private `Hash`, `WriteText` and `WriteDecimal`. **Don't touch `CanonicalJson`'s body**, not even to extract a shared helper: the Done-when diff check fails on any removed line.
- Update the class XML doc to list the four forms.

### 4. Unit tests (modify)
- `tests/Inventory.UnitTests/RequestHashTests.cs`: **add** tests; don't edit or delete the existing ones.
  - `Transfer_canonical_json_is_exact`, with two lines, a trimmed reference and null notes. Assert the exact string, for example `{"fromLocationId":"0f8fad5b-…","toLocationId":"3b241101-…","reference":"TR-1","notes":null,"lines":[{"itemId":"7c9e6679-…","quantity":4},{"itemId":"d3c8a2f0-…","quantity":0.5}]}` with the file's fixed `Loc`/`OtherLoc`/`ItemA`/`ItemB` Guids written out in full.
  - `Adjustment_canonical_json_is_exact`: lines `-2` (no cost) and `5.0000` @ `42.50`. Expect `"quantityDelta":-2,"unitCost":null` and `"quantityDelta":5,"unitCost":42.5`, with `"reasonCode":"Damaged"`.
  - `Transfer_hash_is_sha256_of_method_route_and_canonical_json` and `Adjustment_hash_…`: copy `:74-90`.
  - `Routes_separate_hashes`: one body validated as a receipt, as an issue and as a transfer gives three different hashes, and an adjustment differs from a receipt with the same location and lines.
  - `A_changed_reason_changes_the_hash`, `A_flipped_sign_changes_the_hash` and `Swapped_from_and_to_change_the_hash`.
- `tests/Inventory.UnitTests/PostingValidationTests.cs`: **add** cases; keep every existing one unchanged.
  - Transfer: a valid one (normalized, `ToLocationId` set, lines numbered from 1); missing from or to (each key alone); `from == to` → only `toLocationId`; the invalid quantities theory reused through `MemberData(nameof(InvalidQuantities))` on `lines[0].quantity`.
  - Adjustment: a valid mixed-sign one (signed quantities kept, `ReasonCode` parsed, the cost on the positive line only); the reason theory `null`, `""`, `"  "`, `"damaged"`, `"DAMAGED"`, `" Damaged"`, `"1"`, `"Bogus"` → keys exactly `["reasonCode"]`; each of the five names passes; a `quantityDelta` theory `null`, `0m`, `MaxAmount + 0.0001m`, `-(MaxAmount + 0.0001m)`, `0.00001m`, `-1.23456m` → `["lines[0].quantityDelta"]`; valid deltas `-MaxAmount`, `-0.0001m`, `0.0001m`, `MaxAmount`; a negative line with `unitCost: 0m` → `["lines[0].unitCost"]`; a positive line without a cost passes with `UnitCost` null; the line-total guard (`MaxAmount` × `MaxAmount`) → `["lines[0].unitCost"]`.

### Review constraints that apply
- Decimals only. Round only through `CostingPolicy.Round4`.
- No request carries a tenant id or a date.
- Don't change spec §6 (the ledger contract). These checks are its step 1.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Change `CanonicalJson`, `ReceiptsRoute`, `IssuesRoute`, `Receipt`, `Issue`, or any existing message or key in `PostingValidation`.
- Edit or delete existing test cases.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests
! git diff -U0 develop -- tests/Inventory.UnitTests/RequestHashTests.cs tests/Inventory.UnitTests/PostingValidationTests.cs | grep -E '^-[^-]'
git diff -U0 develop -- src/Modules/Inventory/Inventory/Features/Posting/RequestHash.cs | grep -E '^-[^-]'   # only class XML-doc lines (///) may appear
```
- The existing receipt and issue exact-JSON tests (`RequestHashTests.cs:38-62`) pass unmodified.
- `dotnet test` for the whole solution is still green: nothing calls the new code yet.
