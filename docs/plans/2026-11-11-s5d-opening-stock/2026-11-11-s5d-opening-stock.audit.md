# Audit: [[2026-11-11-s5d-opening-stock]]

## Audit 2026-11-11: PASS-WITH-CHANGES
Graph built: none (no `graphify-out/`; grep per workflow §4) · Symbols traversed: 14 · Missed blast-radius items: 1

Recomputed independently before reading the planner's Blast radius. Verified true:
- The engine works inside `BeginScope`, because `TenantScopeFactory.cs:13` sets `TenantContext`, which `PostingEngine` reads.
- `PostingEngine` and `InventorySeeder` are already scoped (`InventoryModuleExtensions.cs:42-43`).
- `StockDocuments.PostedBy` has no CHECK, so `Guid.Empty` inserts (`20261105155422_Ledger.cs:68-75`).
- The longest key is 60 characters, under `varchar(64)` (`PostingValidation.cs:33`).
- The seed body and the client body hash the same. `RequestHash.FormatDecimal` strips trailing zeros (`RequestHash.cs:65`), so `48.50m` matches `48.5`.
- The table has 31 rows over 19 items, giving 50 balance rows. The spot values check out: 120 × 48.5 = 5820 and 12.25 × 34.8 = 426.3.
- `inventory_app` has UPDATE on `Locations` (`DatabasePrincipals.cs:29-32`).
- The `PostingTests` helpers and the `Receipts` constant are `internal` (`PostingTests.cs:20,556,579,634`). `CostingPolicy.Round4` is internal static (`CostingPolicy.cs:7`).
- Both test projects reference `Api` and see its internals (`Api.csproj:23-24`, `Inventory.csproj:24-25`).
- No test outside `SetupTests` seeds or uses the demo tenant (grep `SeedCommand|demo\.test|SeedTenants\.Demo` over `tests/`).
- The sa connections in the Inventory tests are still RLS-filtered (`LedgerImmutabilityTests.cs:41`). `LedgerSchemaTests.cs:252` doesn't assert adjacent sequence ids.
- No T12 rule constrains `Seeding` (`ModuleBoundaryTests.cs`).
- No `AnalysisMode` is set (`Directory.Build.props`), so CA1848 isn't an error.
- `--filter-class` and `--filter-method` work under MTP. Run here: `Architecture.Tests` gave 5 passed, then 1 passed.

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|
| FIX | Usability | **The key-list comparison doesn't say what it orders by.** `OpeningStock` is in table order (QC then MKT, not alphabetical). A natural `ORDER BY IdempotencyKey` sorts under the server's default collation, because `IdempotencyKey` is `varchar(64)` with no explicit collation. A C# `Order()` is culture-sensitive. If the executor pairs SQL order with table order, or SQL collation order with culture order, the test goes red on a correct seeder. | Prompt 01 `:125` ("Compare ordered lists, case-sensitive"); prompt 02 `:49`; `20261105155422_Ledger.cs:65` (`unicode: false`, no collation); no `UseCollation` in `InventoryDbContext.cs` | In both prompts: read the keys unordered, then compare `actual.Order(StringComparer.Ordinal)` with `OpeningStock.Select(r => OpeningStockKey(r.Sku, r.Code)).Order(StringComparer.Ordinal)`. In 02, to also prove posting order, read the keys `ORDER BY` each document's movement `Id` (taken from the sequence in posting order), and compare that list with table order. |
| FIX | Usability | **The 50-row balance check depends on how the snapshot string is joined.** "`string.Join("\n", …)` across both tenants" has two readings. Joining each tenant's string, with an empty isolation tenant, leaves a trailing `\n`, so `Split('\n').Length` is 51 and a correct seeder fails. That would cost a rerun of prompt 01, the plan's priciest prompt. | Prompt 01 `:120,124`; the isolation tenant has 0 balances (R5, prompt 02 `:71`) | Collect every tenant's formatted rows into one `List<string>`, join that once, and add an `int BalanceRows` field to the `Snapshot` tuple. Assert `after.BalanceRows.ShouldBe(50)` instead of splitting the string. |
| FIX | Breakage (downstream) | **Missed item: the demo tenant in the Inventory test container now has one owner.** `SeedTests` (c) renames QC to `QC-OLD` for good, and classes run in parallel on the assembly fixture. A later test there that seeds or reads the demo tenant would race with that rename or see `QC-OLD`. The plan's own Proposed Downstream invites one: S11's "free check" on the seeded demo tenant. | Plan D8 `:76`, Proposed Downstream `:212` (S11); `DatabaseFixture.cs:13` (assembly fixture); prompt 02 `:65` | D8: say that `SeedTests` owns the demo tenant in `Inventory.IntegrationTests`. Proposed Downstream: any other demo-tenant assertion, including S11's health check, goes inside `SeedTests`' single `[Fact]` or into `Platform.IntegrationTests`, never into a new Inventory test class. |
| NOTE | Notes & constraints | **`SeedTests` departs from §11.** §11 says "Every integration test creates its own tenant". `SeedTests` (and `SetupTests` today) must use `SeedTenants.Demo`, because the seed targets only that tenant. The plan cites §11 as the reason this is safe, but never calls it an exception. | Spec `:629`; plan `:46`, D8 | Add one line to D8 naming the exception and the reason. |
| NOTE | Bloat | Step 2 of prompt 01 builds `(Guid Id, bool IsActive)` dictionaries, but step 3 says not to pre-check `IsActive`. Nothing reads `IsActive`. | Prompt 01 `:99-100,108` | Key the dictionaries to `Guid` only. |
| NOTE | Bloat | Prompt 01 adds content assertions to `SetupTests` (Receipt type, Reference, PostedBy, the key list) that prompt 02 asserts again, more thoroughly. | Prompt 01 `:125`; prompt 02 `:48-49` | Optional: keep only the snapshot and the 31, 31 and 50 counts in `SetupTests`, and leave content to `SeedTests`. That's about 15 LOC less. |
| NOTE | Usability | **The smoke script has two gaps.** (1) The comment says S5c smoke data can turn the replay into a 409. It can't: S5c used random keys, and the seeded hash only depends on the QC and RICE-WHT ids. Only a rename, or a client posting under a seed key, does that. (2) `kill %1; wait` doesn't wait for DCP to release port 4200. Run 2's readiness poll could then hit run 1's dying instance. | Plan `:198-200`; `DocumentPoster.cs:61` (hash from ids) | Fix the comment. After `wait`, poll until `curl` to `:4200` fails before starting run 2. |
| NOTE | Model fit | `sonnet` fits both prompts. 01 adds no SQL, lock or transaction, and the prompt carries the full table and outcome map. The ticket step must rewrite TASKS.md's provisional `model: opus` for IMS-9 to `sonnet`. | TASKS.md `:19`; plan `:156-159` | Covered by the ticket step (legacy re-plan, in-place rewrite). |

**Bloat estimate:** planned ~3 files / ~350 LOC (seeder +110, `SetupTests` +40, `SeedTests` ~200); minimal viable ~3 / ~330. That's in proportion.
**Model cost:** 01 → sonnet/high (keep). 02 → sonnet/medium (keep). No opus needed.
