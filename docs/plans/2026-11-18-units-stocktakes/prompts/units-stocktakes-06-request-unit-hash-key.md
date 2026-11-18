---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 06
unit: IMS-29
status: todo
model: opus
effort: high
depends_on: [05]
parallel_group: F
files:
  create: []
  modify:
    - src/Modules/Inventory/Inventory/Features/Posting/PostReceipt/Request.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostIssue/Request.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostTransfer/Request.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostAdjustment/Request.cs
    - src/Modules/Inventory/Inventory/Features/Posting/PostingValidation.cs
    - src/Modules/Inventory/Inventory/Features/Posting/RequestHash.cs
    - tests/Inventory.UnitTests/RequestHashTests.cs
    - tests/Inventory.UnitTests/PostingValidationTests.cs
  delete: []
---
# 06 · `unit` on request lines, byte-stable RequestHash, reserved `stocktake:` keys

## Task
Accept an optional `unit` on every posting line, normalize it in the pure step-1 checks, and add it to the canonical RequestHash **only when present**, so every existing hash stays byte-identical. Reserve the `stocktake:` key prefix for the server. This prompt doesn't resolve or convert units; prompt 07 does.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. `Features/Posting/**` is a High-stakes path.
- Plan D5: `unit` is optional. It's trimmed and lowercased with `ToLowerInvariant()`, empty or whitespace becomes null, and more than 16 characters is `400` on `lines[i].unit` "Unit must be at most 16 characters." Whether the unit exists is a database check (prompt 07).
- Plan D6, hidden coupling (`RequestHash.cs` doc and CLAUDE.md): "changing a form changes every hash of it, so a retry of an earlier post would get a false `409 idempotency-key-reused`". So the line object writes `"unit":"<code>"` **after its last existing property** and only when `Unit` isn't null. The golden tests `RequestHashTests.cs:60` (`Receipt_canonical_json_is_exact`), `:73` and the hash tests at `:96`/`:124` must pass **unmodified**.
- Plan D10: a client `Idempotency-Key` starting with `stocktake:` (ordinal, case-sensitive) gets `400` on `Idempotency-Key`: "This key prefix is reserved." The server posts stocktakes under `stocktake:<id>` (prompt 12). UUID keys from the web client can't collide.
- Read first:
  - `Features/Posting/PostingValidation.cs:1-60`: `ValidatedLine`, `ValidatedPosting`, `MaxAmount`, `MaxLines`.
  - `:135-159` `IdempotencyKey`.
  - `:161-260` `Validate<TLine>` and `Body<TLine>`. The `read` projector returns `(ItemId, Quantity, UnitCost)`, and `Body` builds `new ValidatedLine(i + 1, item, validQuantity, cleanCost)` at `:254`.
  - `:263-385`: the amounts and text helpers.
  - The four `Request.cs` files, e.g. `internal sealed record PostReceiptLine(Guid? ItemId, decimal? Quantity, decimal? UnitCost);`.
  - All of `RequestHash.cs`.

### 1. Requests
Add `string? Unit` as the **last** parameter of `PostReceiptLine`, `PostIssueLine`, `PostTransferLine` and `PostAdjustmentLine`. JSON binding is by name, so old bodies still bind.

### 2. `PostingValidation`
- `internal sealed record ValidatedLine(int LineNo, Guid ItemId, decimal Quantity, decimal? UnitCost, string? Unit = null);`. Its 14 call sites keep compiling. Doc: "`Unit`: normalized code, or null for the base unit (plan D5); `Quantity` is in that unit until `DocumentPoster` converts it."
- Extend the `read` projector tuple to `(Guid? ItemId, decimal? Quantity, decimal? UnitCost, string? Unit)` in `Validate`, `Body` and the four public entry points: `line => (line.ItemId, line.Quantity, line.UnitCost, line.Unit)`, and `null` for unit cost where there is none today.
- In `Body`, normalize the unit with a small private `Unit(string? value, string path, Dictionary<string,string[]> errors)` in the style of `Text(...)`, and pass it to `ValidatedLine`. A unit error adds to `errors` and keeps the line out, like the other per-line errors.
- `IdempotencyKey`: after the length and ASCII check, `value.StartsWith("stocktake:", StringComparison.Ordinal)` → the 400 above. Expose `internal const string StocktakeKeyPrefix = "stocktake:";` on `PostingValidation`; prompt 12 builds keys from it.

### 3. `RequestHash`
- In `CanonicalJson`, `TransferCanonicalJson` and `AdjustmentCanonicalJson`, after each line's last property: `if (line.Unit is { } unit) json.WriteString("unit", unit);`.
- Update the class doc's form list: each line form gains "and `unit`, written only when present (plan D6)".

### 4. Tests
- `RequestHashTests.cs`: keep every existing test unchanged. Add:
  - `Absent_unit_keeps_the_pre_sprint_3_bytes`: for each of the four forms, a posting whose lines have `Unit: null` gives exactly the existing golden JSON and hash. Reuse the existing fixtures and expected strings.
  - `Unit_is_the_last_line_property_when_present`: a receipt line with `Unit: "case"` gives `…,"unitCost":500,"unit":"case"}…`, and an adjustment gives `…"unitCost":null,"unit":"sack"}`.
  - `Unit_changes_the_hash`.
- `PostingValidationTests.cs`, in its existing style:
  - `" Case "` normalizes to `case`.
  - `"  "` becomes null.
  - A 17-character unit is 400 on `lines[0].unit`.
  - An adjustment line keeps its unit.
  - An `Idempotency-Key` of `stocktake:abc` is 400 on `Idempotency-Key`, while `Stocktake:abc` and `x-stocktake:` are accepted.

## Do not
- Touch files outside `files`. If you must, stop and report why. `DocumentPoster` (resolving and converting units) is prompt 07. Until 07 lands, a request with `unit` posts as if the unit were absent. That's acceptable mid-unit, because 06 and 07 ship in the same PR.
- Edit any existing golden string or expected hash.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.UnitTests
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingTests' --filter-class '*PostingClockTests' --filter-class '*AdjustmentTests'
git diff -U0 tests/Inventory.UnitTests/RequestHashTests.cs | grep '^-[^-]' ; test $? -eq 1   # no existing line removed or changed
```
