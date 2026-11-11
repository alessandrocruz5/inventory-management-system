---
type: prompt
plan: "[[2026-11-11-s5d-opening-stock]]"
id: 01
unit: IMS-9
status: done
model: sonnet
effort: high
depends_on: []
parallel_group: A
files:
  create: []
  modify:
    - src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
    - tests/Platform.IntegrationTests/SetupTests.cs
  delete: []
---
# 01 · Opening stock in the seeder, seed-twice snapshot

## Task
Extend `InventorySeeder` so that, after it seeds branches and items, it posts the demo tenant's opening stock. Post it as one-line `Receipt` documents through `PostingEngine`, each under a deterministic `Idempotency-Key`, so a second run is a replay. Then extend `Seed_twice_changes_nothing` so its snapshot covers the ledger.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-9`. Don't switch it. Run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` before any `dotnet` command.
- **Spec:**
  - §12 S5d: *"Seed posts opening stock as receipts through `PostingEngine` with deterministic idempotency keys, never by writing balances."*
  - §13: the key is `seed:<tenant>:<sku>:<location>`, and running the seed twice changes nothing.
- **Today's seeder:** `src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs`. Its constructor is `(ITenantScopeFactory scopes, TimeProvider clock)`. It opens `scopes.BeginScope(SeedTenants.Demo)` and gets `InventoryDbContext` from `scope.Services`. It inserts the missing branches (`Branches`: QC, MKT) and items (`Items`: 20 SKUs), matching natural keys case-insensitively, then calls `SaveChangesAsync` once. Keep all of that unchanged, and add the opening-stock pass after the `SaveChangesAsync`.
- **Pattern to mirror:** `src/Modules/Inventory/Inventory/Features/Posting/DocumentPoster.cs:42-109`. It checks the key, runs step 1, hashes, calls `FindExistingAsync`, builds the `PostingCommand` and calls `PostAsync`. Don't call `DocumentPoster` (it needs `HttpContext` and `ICurrentUser`), and don't edit it.
- **Signatures, all `internal` in the same assembly:**
  - `Inventory.Persistence.PostingEngine` (scoped; already registered at `InventoryModuleExtensions.cs:42`):
    - `Task<PostingOutcome?> FindExistingAsync(string key, byte[] hash, CancellationToken ct)` returns null when no document holds the key, a `PostedOutcome { Replayed: true }` for the same hash, or a `KeyReusedOutcome` for a different one.
    - `Task<PostingOutcome> PostAsync(PostingCommand command, CancellationToken ct)`.
    - **It calls `db.ChangeTracker.Clear()` at the start of every attempt** (`PostingEngine.cs:95`), so read every id into locals before the first post.
  - `Inventory.Persistence.PostingCommand(PostingKind Kind, StockDocumentType DocumentType, IReadOnlyList<PostingLine> Lines, string IdempotencyKey, byte[] RequestHash, string? Reference, string? Notes, Guid PostedBy)`.
  - The outcome records are in the same file (`PostingCommand.cs:20-50`): `PostedOutcome(Document, Movements, Balances, bool Replayed, BalancesAsOf)`, `KeyReusedOutcome(Guid OriginalDocumentId)`, `InactiveItemOutcome(Guid ItemId)`, `InactiveLocationOutcome(Guid LocationId)`, `RejectedOutcome(PostingRejectedException Rejection)`, `BalanceOutOfRangeOutcome(Guid ItemId, Guid LocationId)` and `RetriesExhaustedOutcome`.
  - `Inventory.Domain.PostingKind.Receipt`, `Inventory.Persistence.StockDocumentType.Receipt`.
  - `Inventory.Domain.PostingLine(int LineNo, Guid ItemId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, decimal? UnitCost)`.
  - `Inventory.Features.Posting.PostReceipt.PostReceiptRequest(Guid? LocationId, string? Reference, string? Notes, IReadOnlyList<PostReceiptLine?>? Lines)` and `PostReceiptLine(Guid? ItemId, decimal? Quantity, decimal? UnitCost)`.
  - `Inventory.Features.Posting.PostingValidation`:
    - `static ValidatedPosting? Receipt(PostReceiptRequest request, Dictionary<string, string[]> errors)`.
    - `static string? IdempotencyKey(StringValues header, Dictionary<string, string[]> errors, out bool missing)`. `StringValues` is from `Microsoft.Extensions.Primitives`.
  - `ValidatedPosting(Guid LocationId, string? Reference, string? Notes, IReadOnlyList<ValidatedLine> Lines)` and `ValidatedLine(int LineNo, Guid ItemId, decimal Quantity, decimal? UnitCost)`.
  - `Inventory.Features.Posting.RequestHash.Receipt(ValidatedPosting posting)` returns `byte[]`. It's the receipt route's canonical form (Hidden coupling), so a client POST with the same key and body replays.
  - `Inventory.Persistence.LocationKind.{Physical, Supplier}`, and the `Location` and `Item` entities with `Id`, `Code`/`Sku`, `Kind` and `IsActive`.

### Decisions to implement (plan D1–D7)
- **Members to add to `InventorySeeder`** (all `internal static`, because prompt 02's test reads them):
  ```csharp
  /// <summary>Who posts the opening stock: no user, the system (plan D2). S7 shows it as "System".</summary>
  internal static readonly Guid SystemPrincipal = Guid.Empty;

  internal const string OpeningStockReference = "Opening stock";

  /// <summary>The demo tenant's opening stock: one receipt per row, in this order (plan D3). CANDLE-WHT has none on purpose.</summary>
  internal static readonly (string Code, string Sku, decimal Quantity, decimal UnitCost)[] OpeningStock =
  [
      ("QC", "RICE-WHT", 120m, 48.50m),
      ("QC", "SUGAR-BRN", 60m, 72.25m),
      ("QC", "OIL-PALM-1L", 48m, 89.75m),
      ("QC", "EGG-MED", 360m, 7.80m),
      ("QC", "NOODLE-CHK", 240m, 9.50m),
      ("QC", "SARDINE-155", 144m, 22.75m),
      ("QC", "CORNBEEF-150", 96m, 41.50m),
      ("QC", "COFFEE-3IN1", 500m, 6.85m),
      ("QC", "MILK-EVAP-370", 72m, 38.25m),
      ("QC", "COLA-1.5L", 48m, 62.00m),
      ("QC", "WATER-500", 120m, 11.50m),
      ("QC", "WATER-500-24", 10m, 235.00m),
      ("QC", "SOAP-BAR-90", 72m, 28.50m),
      ("QC", "DETERGENT-1KG", 36m, 79.95m),
      ("QC", "SHAMPOO-12", 300m, 6.25m),
      ("QC", "TOOTHPASTE-100", 48m, 68.50m),
      ("QC", "VINEGAR-REFILL", 25.5m, 34.80m),
      ("QC", "SOY-1L", 36m, 52.75m),
      ("QC", "PANDESAL-10", 20m, 45.00m),
      ("MKT", "RICE-WHT", 80m, 49.25m),
      ("MKT", "SUGAR-BRN", 40m, 72.25m),
      ("MKT", "OIL-PALM-1L", 24m, 89.75m),
      ("MKT", "EGG-MED", 180m, 7.80m),
      ("MKT", "NOODLE-CHK", 120m, 9.50m),
      ("MKT", "SARDINE-155", 72m, 22.75m),
      ("MKT", "COFFEE-3IN1", 250m, 6.85m),
      ("MKT", "COLA-1.5L", 24m, 62.00m),
      ("MKT", "WATER-500", 96m, 11.50m),
      ("MKT", "SOAP-BAR-90", 36m, 28.50m),
      ("MKT", "SHAMPOO-12", 150m, 6.25m),
      ("MKT", "VINEGAR-REFILL", 12.25m, 34.80m),
  ];

  /// <summary>The deterministic Idempotency-Key of one opening-stock receipt (spec §13): seed:&lt;tenant&gt;:&lt;sku&gt;:&lt;location&gt;.</summary>
  internal static string OpeningStockKey(string sku, string code) => $"seed:{SeedTenants.Demo:D}:{sku}:{code}";
  ```
  That's 31 rows over 19 distinct items. Update the class `<summary>` so it mentions opening stock. Delete the phrase "S5d adds opening stock here".
- **Constructor:** add `ILogger<InventorySeeder> logger` (`Microsoft.Extensions.Logging`, from the framework reference, so no package). DI supplies it, and `InventoryModuleExtensions.cs` needs no change. Use message templates with named placeholders, never string interpolation in log calls.
- **The pass.** After the existing `SaveChangesAsync`, call a private `PostOpeningStockAsync(scope.Services, db, cancellationToken)`. It does this:
  1. `var engine = services.GetRequiredService<PostingEngine>();`. It's the same scope, so it shares the scoped `InventoryDbContext`.
  2. Read ids first (D7), all `AsNoTracking()`:
     - The physical locations as a `Dictionary<string, (Guid Id, bool IsActive)>` keyed by `Code` with `StringComparer.OrdinalIgnoreCase`.
     - The items the same way, keyed by `Sku`.
     - `supplierId = (await db.Locations.AsNoTracking().SingleAsync(l => l.Kind == LocationKind.Supplier, ct)).Id`. A missing Supplier is a provisioning bug: let it throw, as `DocumentPoster.cs:101` does.
  3. For each row of `OpeningStock`, in order:
     - If the code or the SKU isn't in its dictionary, log a warning (`"Opening stock {Key} skipped: its item or location is missing."`) and continue.
     - `key = OpeningStockKey(sku, code)`. Run `PostingValidation.IdempotencyKey(new StringValues(key), errors, out _)`. If it returns null, throw `InvalidOperationException` naming the key: it's a seed-data bug.
     - `validated = PostingValidation.Receipt(new PostReceiptRequest(locationId, OpeningStockReference, null, [new PostReceiptLine(itemId, quantity, unitCost)]), errors)`. If it returns null, throw `InvalidOperationException` with the key and `string.Join(", ", errors.Keys)`.
     - `hash = RequestHash.Receipt(validated)`.
     - **D1:** if `await engine.FindExistingAsync(key, hash, ct)` is non-null, the pair is already seeded: count it as skipped and continue. When it's a `KeyReusedOutcome`, also log at information level (`"Opening stock {Key} skipped: the key holds document {DocumentId} from a different request (renamed item or branch)."`).
     - Don't pre-check `IsActive`. The engine re-checks it under lock at step 7. Build the line from `validated.Lines.Single()`: `new PostingLine(line.LineNo, line.ItemId, supplierId, validated.LocationId, line.Quantity, line.UnitCost)`. Build the command: `new PostingCommand(PostingKind.Receipt, StockDocumentType.Receipt, [postingLine], key, hash, validated.Reference, validated.Notes, SystemPrincipal)`.
     - Then `switch` on `await engine.PostAsync(command, ct)` (D6):
       - `PostedOutcome { Replayed: false }`: count it as posted.
       - `PostedOutcome` (a replay) or `KeyReusedOutcome`: a parallel seed won the key. Count it as skipped.
       - `InactiveItemOutcome` or `InactiveLocationOutcome`: log a warning (`"Opening stock {Key} skipped: its item or location is inactive."`) and count it as skipped.
       - Anything else (`RejectedOutcome`, `BalanceOutOfRangeOutcome`, `RetriesExhaustedOutcome`): throw `InvalidOperationException($"Opening stock {key} was not posted: {outcome}.")`. The seed must fail loudly, and a rerun is safe.
  4. At the end, log at information level: `"Opening stock: {Posted} posted, {Skipped} already seeded or skipped."`.
- **The seeder never assigns `TenantId`.** `grep -n 'TenantId' …/InventorySeeder.cs` must print nothing: `SeedTenants.Demo` and the scope supply the tenant. It never touches `StockBalances`, `StockDocuments` or `StockMovements`, and runs no raw SQL.

### Test to extend: `tests/Platform.IntegrationTests/SetupTests.cs:164-250`
- **Snapshot.** Extend the `Snapshot` tuple to `(int Tenants, int Users, int Locations, int Items, int Documents, int Movements, int BalanceRows, string Balances)`.
  - Inside the existing per-tenant `BeginScope` loop, count `inventory.StockDocuments` and `inventory.StockMovements` with the same `x.TenantId == tenantId` predicate the loop already uses.
  - Declare one `var balanceRows = new List<string>();` before the loop. In the loop, read `inventory.StockBalances.Where(b => b.TenantId == tenantId)`, ordered by `ItemId` then `LocationId`. Materialize it, then add each row to `balanceRows` as `string.Create(CultureInfo.InvariantCulture, $"{b.ItemId}|{b.LocationId}|{b.OnHand:F4}|{b.StockValue:F4}|{b.LastUnitCost:F4}|{b.LastMovementId}")`. EF can't translate the formatting, so format in memory.
  - After the loop, set `BalanceRows = balanceRows.Count` and `Balances = string.Join("\n", balanceRows)`. Join once, over the single list: never join per-tenant strings, because the isolation tenant has no rows and would add an empty segment.
  - The existing `after.ShouldBe(before)` then covers the ledger.
- **New assertions,** after the existing count assertions:
  - `after.Documents.ShouldBe(31)` and `after.Movements.ShouldBe(31)`.
  - 50 balance rows (31 branch balances and 19 Supplier balances): `after.BalanceRows.ShouldBe(50)`. Don't split `Balances` to count it.
  - In the existing demo branch of the per-tenant loop, every document is a `Receipt` with `Reference == InventorySeeder.OpeningStockReference` and `PostedBy == InventorySeeder.SystemPrincipal`.
  - The keys match the table. Read the documents' `IdempotencyKey`s with no `OrderBy`. Then assert `keys.Order(StringComparer.Ordinal).ToList().ShouldBe(InventorySeeder.OpeningStock.Select(r => InventorySeeder.OpeningStockKey(r.Sku, r.Code)).Order(StringComparer.Ordinal).ToList())`. Sort only in C#, with `StringComparer.Ordinal`: `IdempotencyKey` is `varchar` under the server's default collation, and the table isn't in alphabetical order.
  - In the isolation branch, the documents are empty.
- The Platform test project already sees `Inventory` internals (`Inventory.csproj:24` `InternalsVisibleTo`). Add `using Inventory.Seeding;` and `using System.Globalization;` as needed.

### Conventions and review constraints
- Quantities and money are `decimal`, never `double` or `float`. The engine rounds (AwayFromZero, 4 dp).
- Time comes only from `TimeProvider`: the engine stamps `PostedAt`. No `DateTime.Now` or `DateTimeOffset.UtcNow`.
- No UPDATE or DELETE on the ledger. No `IgnoreQueryFilters()`.
- Module internals stay `internal`. Add no public type.
- `TreatWarningsAsErrors` is on, with nullable enabled. Match the file's existing style: primary constructor, collection expressions, `await using`.

## Do not
- Touch files outside the `files` list. Specifically, don't edit `PostingEngine`, `DocumentPoster`, `PostingValidation`, `RequestHash`, `Domain/*`, `InventoryModuleExtensions.cs`, migrations or the web app. If you must, stop and report why.
- Change the existing branch and item seeding, or its natural-key matching.
- Seed opening stock for the isolation tenant.
- Add a marker column, a new document type, a package, an abstraction or a config key.
- Create or switch branches, commit or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Platform.IntegrationTests
dotnet test --project tests/Inventory.IntegrationTests
dotnet test --project tests/Architecture.Tests
! grep -nE 'StockBalances|StockMovements|StockDocuments|ExecuteSql|SqlQuery|FromSql' src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
! grep -n 'TenantId' src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
! grep -nE '\b(double|float)\b|DateTime(Offset)?\.(Utc)?Now|IgnoreQueryFilters' src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
git diff --stat -- src/Modules/Inventory/Inventory/Persistence src/Modules/Inventory/Inventory/Features src/Modules/Inventory/Inventory/Domain   # prints nothing
```
- `Seed_twice_changes_nothing` passes with 31 documents, 31 movements and 50 balance rows (`BalanceRows`), and its before and after snapshots are equal.
- If Testcontainers crashes at start-up, rerun with `dotnet test --max-parallel-test-modules 1` and say so in the report.
