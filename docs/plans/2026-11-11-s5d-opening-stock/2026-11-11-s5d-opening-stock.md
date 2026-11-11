---
type: plan
kind: feature
status: audited
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-11
risk: medium
prompts: 2
graph_built: none
tags: [plan]
---
# S5d Opening stock (IMS-9)

## Request
> IMS-9

This plans the existing unit `IMS-9 — S5d Opening stock · Added · spec: §12 S5d · depends: IMS-8 · model: opus` (TASKS.md, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S5d: *"Seed posts opening stock as receipts through `PostingEngine` with deterministic idempotency keys, never by writing balances. Tests: Running the seed twice changes nothing."*

Spec §13 (DoD): *"The seed creates the retail demo tenant (two branches, about 20 items, opening stock) … Running it twice changes nothing: opening stock uses deterministic idempotency keys (`seed:<tenant>:<sku>:<location>`), so the second run is a replay."*

It also covers the S5d items that earlier units locked:
- IMS-3: seed extensions go in an `IDataSeeder` and run as `inventory_app`.
- IMS-5: the seeder is `Inventory.Seeding.InventorySeeder`, keyed `seed:<tenant>:<sku>:<location>`. `Sku` and `Code` are both editable, so this plan must decide how renames are handled.
- IMS-8: `PostingCommand.PostedBy` comes from the caller, so the seeder can pass its own principal.

| # | Requirement |
|---|---|
| R1 | `Api seed` posts the demo tenant's opening stock as `Receipt` documents through `PostingEngine`. Each document has one line, for one (item, branch) pair of the seed table. Nothing writes `StockBalances`, `StockDocuments` or `StockMovements` except the engine. |
| R2 | Each receipt's `Idempotency-Key` is `seed:<tenant>:<sku>:<location code>`. A second run is a replay: documents, movements and every balance row are identical before and after it. |
| R3 | A seeded receipt is exactly one the API would accept. It passes step 1 (`PostingValidation`), and its `RequestHash` uses the receipt route's canonical form. A client `POST /api/documents/receipts` with the same key and body therefore replays it (`200`, `Idempotent-Replayed: true`). |
| R4 | A pair whose key is already held is treated as already seeded and skipped. That covers a replay (same hash) and a key reused after a rename (different hash). A pair whose item or location is missing or inactive is also skipped, with a log line. Neither case fails the seed. Any other non-posted outcome is a bug and fails the seed. |
| R5 | Tests in the same PR: the seed-twice test also covers the ledger. A new test proves the content of R1, the replay of R3, the rename case of R4, and that the isolation tenant has no stock. |
| R6 | Every `## Commands` line is green, and CI `build` and `test` are green. |

**Clarify pass (2026-11-11):** three gaps, asked in one round. The user picked the recommended default each time (D1–D3).

## Context used
- Spec §12 S5d (`docs/Inventory System — Sprint 1 Spec.md:673`) and §13 DoD (`:691`): receipts through the engine, deterministic keys, a second run that replays.
- Spec §6 (`:291-343`): steps 1–10, the step-2 idempotency rules, and `PostedAt` taken from `TimeProvider` only. Spec §5 (`:200-289`): `PostedBy uniqueidentifier NOT NULL`, with no FK.
- Spec §11 (`:627`): every integration test creates its own tenant. That's why the demo tenant is safe to use in the Inventory test container.
- TASKS.md Downstream: IMS-3 (seed runs as `inventory_app`; S5d in an `IDataSeeder`), IMS-5 (S5d in `InventorySeeder`, the rename decision), IMS-6 (SKU immutable once an item has a movement; `stock-not-zero` on deactivation), IMS-8 (`DocumentPoster` is the base for posting routes; `PostingTests` helpers; the Testcontainers flake and `--max-parallel-test-modules 1`; `DOTNET_SYSTEM_NET_DISABLEIPV6=1`).
- S4 plan `2026-11-05-s4-master-data.md:92-93`: the rename problem was deferred to "S5d's plan". S4 audit `:11`: the seeder re-creates a renamed `QC`/`RICE-WHT` (accepted known risk).
- S5c plan `2026-11-09-s5c-posting-engine.md:103-135`: the engine API (`FindExistingAsync`, `PostAsync`), the canonical form, `PostedBy` from the caller.
- Code: `src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs:12-86`: today's seeder (branches QC and MKT, 20 items, natural keys case-insensitive, one `SaveChangesAsync`).
- Code: `Features/Posting/DocumentPoster.cs:42-109`: the order to mirror (key check, step 1, hash, `FindExistingAsync`, build `PostingCommand`, `PostAsync`). It's bound to `HttpContext` and `ICurrentUser`, so the seeder can't call it.
- Code: `Persistence/PostingEngine.cs:28` (`FindExistingAsync`), `:65` (`PostAsync`), `:95` (`ChangeTracker.Clear()` at the start of every attempt), `:82-86` (clears again on any non-posted outcome).
- Code: `Persistence/PostingCommand.cs:9-50` (the command and the outcome records). `Domain/PostingRules.cs:4,13` (`PostingKind`, `PostingLine`).
- Code: `Features/Posting/PostingValidation.cs:38` (`Receipt`), `:65` (`IdempotencyKey`). `RequestHash.cs:23` (`Receipt`). `PostReceipt/Request.cs:3,5` (`PostReceiptRequest`, `PostReceiptLine`).
- Code: `InventoryModuleExtensions.cs:42-43` (`PostingEngine` and `InventorySeeder` are already scoped registrations). `src/Api/Program.cs:18-19` (Platform is registered before Inventory, so `PlatformSeeder` runs first: `SeedCommand.cs:11`). `src/AppHost/AppHost.cs:25-33` (`seed` runs on every start; `api` waits for it to complete).
- Code: `UpdateLocation/Handler.cs:44-51`: a location's `Code` stays editable after it has stock. A `Sku` is not (S5a).
- Tests: `tests/Platform.IntegrationTests/SetupTests.cs:164-250` (`Seed_twice_changes_nothing` and `Snapshot`). `tests/Inventory.IntegrationTests/DatabaseFixture.cs:13,78` (assembly fixture; `SignInAsync` uses `UserPassword`). `LedgerRows.cs:20` (`OpenAsync`, a tenant connection as `inventory_app`). `PostingTests.cs:556` (`PostDocumentAsync`). `Api.csproj:23-24` (both integration test projects see `SeedCommand`).
- Grep: no Inventory integration test touches `SeedTenants.Demo`. Every ledger `COUNT(*)` in that assembly runs on a tenant-scoped connection (`LedgerRows.OpenAsync` or a signed-in client), so demo rows don't leak into them.
- Graph: none (`graphify-out/` doesn't exist). Blast radius by grep (workflow §4).

## Decisions
- **D1 · Rename handling (user, 2026-11-11): if a document holds the key, the pair is seeded.** The seeder calls `FindExistingAsync(key, hash)`. Any non-null result means "done": a `PostedOutcome` replay, or a `KeyReusedOutcome` after a rename. A renamed branch is re-created by S4's seeder with a new id, which changes the hash. That pair is skipped with an information log, and the re-created branch stays empty. No marker column, no migration. Once a pair has stock, its item's `Sku` is immutable (S5a), so only a branch rename, or a SKU edit made before the first S5d run, can trigger this. A future change to the canonical form shows up the same way, as a reused key, and is skipped.
- **D2 · `PostedBy` (user, 2026-11-11): `Guid.Empty`.** It's a named constant, `InventorySeeder.SystemPrincipal`. Platform's user ids stay out of Inventory. This mirrors S4, where seeded items have `CreatedBy = null`. S7 shows it as "System" (Downstream).
- **D3 · Seed data (user, 2026-11-11): 31 one-line receipts.** QC stocks 19 of the 20 items and MKT stocks 12. `CANDLE-WHT` has no stock anywhere, so SKU edit, deactivation and S12's T14 still work on a seeded item. Costs are realistic PHP prices with at most 2 decimals. `VINEGAR-REFILL` has fractional quantities (25.5 and 12.25 l). The table is in prompt 01, verbatim.
- **D4 · Document shape.** Each (sku, code) pair is one receipt with one line (`LineNo` 1), `Reference` `"Opening stock"` and `Notes` null. Pairs post in table order: QC first, then MKT. The key is `$"seed:{SeedTenants.Demo:D}:{sku}:{code}"`, with the seed table's `Sku` and `Code` as written and the tenant Guid in lowercase `D` form. The longest key is 60 characters (`…:VINEGAR-REFILL:MKT`), under the 64 limit. `PostingValidation.IdempotencyKey` checks every key at run time anyway.
- **D5 · Reuse step 1 and the hash; don't refactor `DocumentPoster`.** The seeder builds a `PostReceiptRequest` and runs `PostingValidation.Receipt`, then `RequestHash.Receipt`. A failure throws `InvalidOperationException`, because it's a bug in the seed data. Then it mirrors `DocumentPoster`: `FindExistingAsync`, the `PostingCommand` (Supplier → branch), and `PostAsync`. Rejected alternative: pulling an HTTP-free core out of `DocumentPoster`. That rewrites the High-stakes base that S6 and S9 build on, to save about 15 lines. The seeder skips `DocumentPoster`'s step-1 database checks: it resolves ids itself, and the engine's step 7 re-checks `IsActive` under lock.
- **D6 · Outcome mapping.**
  - `PostedOutcome { Replayed: false }` counts as posted.
  - A `PostedOutcome` replay or a `KeyReusedOutcome` from `PostAsync`'s race path means skipped, as seeded.
  - `InactiveItemOutcome` and `InactiveLocationOutcome` are skipped with a warning.
  - `RejectedOutcome`, `BalanceOutOfRangeOutcome`, `RetriesExhaustedOutcome` and any other outcome throw `InvalidOperationException`. The seed exits non-zero, the AppHost doesn't start `api`, and a rerun is safe.
  - One information log at the end gives the posted and skipped counts.
- **D7 · One scope, ids first.** Opening stock runs in the same `BeginScope(SeedTenants.Demo)` scope, after the existing `SaveChangesAsync`. It resolves `PostingEngine` from `scope.Services`, so the engine and the seeder share the scoped `InventoryDbContext`. The engine clears the change tracker on every attempt (`PostingEngine.cs:95`). So the seeder first reads the physical locations, the items and the Supplier id into plain dictionaries and locals (`AsNoTracking`, case-insensitive like the S4 part), and never holds entities across a post.
- **D8 · Test placement.**
  - `Seed_twice_changes_nothing` (Platform) keeps its claim. Its `Snapshot` gains document and movement counts and a balance fingerprint.
  - The new `tests/Inventory.IntegrationTests/SeedTests.cs` runs in its own container, where no other test uses the demo tenant. It has one sequential test: the rename step mutates the demo tenant, and the fixture is shared across the assembly, so splitting it into several `[Fact]`s would make the result depend on their order.
  - The test reads quantities and costs from `InventorySeeder.OpeningStock` (internal). Two literal spot checks act as an independent oracle.
  - **Ownership (audit 2026-11-11).** `SeedTests` owns `SeedTenants.Demo` in `Inventory.IntegrationTests`. Step (c) leaves the tenant renamed for good (`QC-OLD` plus a new `QC`), and classes run in parallel on the assembly fixture. Any later demo-tenant assertion goes inside `SeedTests`' single `[Fact]` or into `Platform.IntegrationTests`, never into another Inventory test class. This is a deliberate exception to spec §11 ("every integration test creates its own tenant"): the seed targets only the demo tenant. `SetupTests` is the precedent.
  - **Key comparisons (audit 2026-11-11).** `IdempotencyKey` is `varchar` under the server's default collation, and the table isn't alphabetical. So tests sort keys only in C#, with `StringComparer.Ordinal`, or read them in posting order (`ORDER BY` movement `Id`). They never use `ORDER BY IdempotencyKey`. The seed-twice snapshot counts balance rows in its own `int`, and joins one flat list, rather than splitting a joined string.
- **Code review.** CLAUDE.md requires code-guardian on S2–S4, S5a–S5c, S6 and S9–S11, and S5d isn't on that list. No `## High-stakes paths` glob is touched either. The review is optional (see Units).

**Assumptions:**
- `PlatformSeeder` runs before `InventorySeeder`. `GetServices<IDataSeeder>()` returns them in registration order (`Program.cs:18-19`), and S4 already relies on that. If the Supplier location is missing, `SingleAsync` throws, as it does in `DocumentPoster.cs:101`.
- Existing dev volumes get opening stock on their next AppHost start, on top of any S5c smoke receipts, so moving averages blend. That's fine for dev data, and a fresh volume is exact.
- `seed:` keys aren't reserved. A client that posts first under a seed key makes the seeder skip that pair (D1). This only matters in dev, where seed keys are visible.
- The AppHost runs `seed` to completion before `api` starts (`WaitForCompletion`), so no API posting races the seed there. If two seeds run at once, the engine's step-4 race path turns the loser into a replay or a reused key, and both are skips.
- `ILogger<InventorySeeder>` is the module's first logger. It comes from the ASP.NET Core framework reference, not a new package. Use message templates (no interpolation). The default analyzers don't make CA1848 an error.
- The isolation tenant gets no opening stock: spec §13 gives items only to the demo tenant.
- `PostedAt` is the clock at the first run, from the engine's `TimeProvider`. A replay never re-stamps it.

**Non-goals:**
- Any change to `PostingEngine`, `DocumentPoster`, `PostingValidation`, `RequestHash`, `Domain/*`, the schema, migrations, grants, routes or UI.
- A stored seed marker, or fixing S4's re-creation of a renamed branch or SKU.
- Opening stock for the isolation tenant, and a separate "Opening" document type (the spec says receipts).
- `GET /stock` and the stock screen (S7). Ledger health (S11).

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs` | M | `OpeningStock` table, `OpeningStockKey`, `SystemPrincipal`, the posting pass through `PostingEngine`, the outcome mapping, an `ILogger` | R1–R4 | 01 |
| `tests/Platform.IntegrationTests/SetupTests.cs` | M | `Snapshot` covers the documents, the movements and every balance row; new count assertions | R2, R5 | 01 |
| `tests/Inventory.IntegrationTests/SeedTests.cs` | C | Content, API replay, rename skip, no isolation stock | R1, R3, R4, R5 | 02 |

`InventoryModuleExtensions.cs` needs no change: `PostingEngine` and `InventorySeeder` are already registered, and DI supplies `ILogger<T>`.

**Review constraints, checked against the design:**
- Decimals: the table uses `decimal` literals (`m`), and the engine does the rounding (AwayFromZero, 4 dp).
- No UPDATE or DELETE on the ledger: the seeder only calls the engine, which only inserts. The one raw `UPDATE` is in a test, on `inventory.Locations`, which isn't a ledger table.
- No hard deletes.
- `TenantId` never comes from a request: `BeginScope` plus the column default and interceptor. The seeder never assigns `TenantId`.
- No `IgnoreQueryFilters()`.
- `PostedAt` comes from the engine's `TimeProvider`.
- The seed runs as `inventory_app`.
- No new package, no change to section 6, no new column.

## Blast radius
**Risk:** medium. No High-stakes glob or Shared code is touched, but the change writes ledger rows on every environment's seed, and the DoD's "running twice changes nothing" rests on it.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `Api.Setup.SeedCommand` / `Api seed` | Direct | Calls `IDataSeeder.SeedAsync` | No. It runs the new pass, and exits non-zero only on a bug outcome (D6) | Rerun is safe (D1) |
| AppHost `seed` → `api` (`AppHost.cs:25-33`) | Non-graph | `WaitForCompletion(seed)` | Only if the seed throws, and then `api` doesn't start | Only unexpected outcomes throw. Smoke test in the E2E block |
| `PostingEngine.FindExistingAsync` / `PostAsync` | Direct (callee, new caller) | Seeder | No. Unchanged. `ChangeTracker.Clear()` would drop tracked seed entities | D7: ids are read into locals first |
| `PostingValidation.Receipt` / `.IdempotencyKey`, `RequestHash.Receipt` | Direct (callee, High-stakes path, unchanged) | Seeder | No. Reused read-only | Hidden coupling: seeded hashes use the receipt canonical form, so a form change makes them reused keys, which are skips (D1) |
| `SetupTests.Seed_twice_changes_nothing` | Direct (test) | Runs the seed twice | No (the counts it asserts don't change), but it misses the ledger | 01 extends `Snapshot` and adds ledger counts |
| `Inventory.IntegrationTests` (shared container) | Non-graph | Demo-tenant rows in the database | No. Every test makes its own tenant (§11), and its counts are tenant-scoped. `MovementIds` interleaves, and tests assert only ascending ids per document | — |
| Future Inventory tests on the demo tenant (S11's health check) | Non-graph (audit) | `SeedTests` (c) renames QC for good on the assembly fixture | Yes, a new class would race with the rename or see `QC-OLD` | D8 ownership: such checks go in `SeedTests` or `Platform.IntegrationTests`. Downstream note below |
| S5a guards: SKU immutability, `stock-not-zero` | Non-graph | Seeded stocked items and both branches | By design: stocked seeded items can't change SKU or be deactivated, and QC and MKT can't be deactivated | `CANDLE-WHT` stays free (D3) |
| S4 seeder re-creation on rename | Known risk | Natural keys | Unchanged | D1: the re-created branch gets no stock, and the seed doesn't fail |
| Dev volumes | Non-graph | Next AppHost start | No. Opening stock posts once | Assumption above |
| T5 route guard, T6 catalog, T12 architecture | Non-graph | No new route, table or public type | No | `Seeding` → `Features.Posting` stays inside the assembly, and no T12 rule forbids it |
| S7 documents and stock screens | Downstream | `PostedBy = Guid.Empty`; seeded balances | No | Downstream note: show `00000000-…` as "System" |
| S11 health / T9 | Downstream | Seeded data in the demo tenant | No. It's posted by the engine, so INV-1 to INV-5 hold | S11's health on the demo tenant is a free check |
| S12 T14 (receive 10, issue 3, see 7 / ₱700) | Downstream | Seeded balances | Yes, if T14 uses a stocked item | Downstream note: T14 uses `CANDLE-WHT` (no opening stock) or its own item |

**Known risks in the touched area:**
- S4's renamed-branch re-creation (S4 audit `:11`). It's untouched here, and D1 makes it harmless to the ledger.
- The Testcontainers start-up flake when both integration modules start at once (IMS-8). Use `--max-parallel-test-modules 1` if it shows.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-9 | S5d Opening stock | Added | 01, 02 | IMS-8 | no. `Seeding/**` and `tests/**` match no glob, and S5d isn't on CLAUDE.md's code-guardian list. Optional: `code-guardian-codex` | See below |

Acceptance criteria for IMS-9:
- **R1:** after one seed, the demo tenant has 31 `Receipt` documents with `Reference` "Opening stock" and `PostedBy` `Guid.Empty`. Each has one movement (`LineNo` 1, SUP → branch, `TotalCost` = Round4(qty × cost)). Each branch balance shows `OnHand` = qty, `StockValue` = that cost, `LastUnitCost` = cost and `LastMovementId` = that movement. `CANDLE-WHT` has no balance rows. Σ `OnHand` per item is 0 (INV-2). Spot checks: RICE-WHT@QC 120 / 5820.0000 / 48.5000, VINEGAR-REFILL@MKT 12.25 / 426.3000 / 34.8000.
- **R2:** the keys are exactly `seed:00000000-0000-0000-0000-00000000d001:<SKU>:<CODE>` for the 31 table rows. A second seed leaves the document count, the movement count and every balance row (`OnHand`, `StockValue`, `LastUnitCost`, `LastMovementId`) identical.
- **R3:** `admin@demo.test` posting the seeded RICE-WHT@QC body under its key gets `200`, `Idempotent-Replayed: true` and the seeded document's id.
- **R4:** after QC is renamed to `QC-OLD` and the seed runs again, it doesn't throw. Documents and movements stay at 31, a new physical `QC` exists with no balance rows, and `QC-OLD`'s balances are unchanged.
- **R5:** `Seed_twice_changes_nothing` asserts 31 documents, 31 movements and 50 balance rows (31 branch, 19 Supplier; `Snapshot.BalanceRows`), with equal snapshots. `SeedTests` proves R1, R3, R4 and that the isolation tenant has 0 documents.
- **R6:** every `## Commands` line and CI `build` and `test` are green.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Opening stock in the seeder, seed-twice snapshot | IMS-9 | sonnet | high | — | A | `Seeding/InventorySeeder.cs`, `tests/Platform.IntegrationTests/SetupTests.cs` |
| 02 | SeedTests: content, replay, rename, isolation | IMS-9 | sonnet | medium | 01 | B | `tests/Inventory.IntegrationTests/SeedTests.cs` |

- **No opus.** TASKS.md has `opus` provisionally (spec audit: "ledger"). The plan moves both prompts down:
  - 01 adds no SQL, no lock and no transaction of its own. It composes four existing, tested engine-side calls in the order `DocumentPoster` already uses (`DocumentPoster.cs:42-109`). The prompt carries the whole table and the outcome map. Step-1 validation and the database CHECKs catch misuse.
  - 02 is a sequential test built on existing helpers.
  - `/audit-plan` can raise 01 to opus if it rates the risk higher.
- Groups A → B run in order: they share the .NET build and test tree, and 02 reads 01's internals (workflow §5).

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false
cd ..

# R2, R5: seed twice changes nothing (ledger included)
dotnet test --project tests/Platform.IntegrationTests --filter-method '*Seed_twice_changes_nothing'
# R1, R3, R4, R5: content, API replay, rename skip, isolation
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*SeedTests'
dotnet test --project tests/Architecture.Tests

# Review constraints: the seeder only reaches the ledger through the engine
! grep -nE 'StockBalances|StockMovements|StockDocuments|ExecuteSql|SqlQuery|FromSql' src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
! grep -n 'TenantId' src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
! grep -nE '\b(double|float)\b|DateTime(Offset)?\.(Utc)?Now|IgnoreQueryFilters' src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
git diff --stat develop -- src/Modules/Inventory/Inventory/Persistence src/Modules/Inventory/Inventory/Features src/Modules/Inventory/Inventory/Domain   # prints nothing

# Full stack smoke: two AppHost starts against the dev volume; seed completes both times and the second is a replay
SCRATCH=$(mktemp -d)
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"
for run in 1 2; do
  dotnet run --project src/AppHost > "$SCRATCH/apphost-$run.log" 2>&1 &
  for i in $(seq 120); do [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:4200/api/auth/me)" = 401 ] && break; sleep 2; done
  # $SEED_PASSWORD: the AppHost's generated `seed-password` parameter, read into the shell and never echoed
  curl -s -c "$SCRATCH/admin" -H 'Content-Type: application/json' -d "{\"email\":\"admin@demo.test\",\"password\":\"$SEED_PASSWORD\"}" \
    http://localhost:4200/api/auth/login -o /dev/null -w '%{http_code}\n'                                   # 200 (api started, so seed exited 0)
  QC=$(curl -s -b "$SCRATCH/admin" 'http://localhost:4200/api/locations?pageSize=100' | jq -r '.items[] | select(.code=="QC") | .id')
  RICE=$(curl -s -b "$SCRATCH/admin" 'http://localhost:4200/api/items?pageSize=100' | jq -r '.items[] | select(.sku=="RICE-WHT") | .id')
  curl -s -b "$SCRATCH/admin" -H 'Content-Type: application/json' \
    -H 'Idempotency-Key: seed:00000000-0000-0000-0000-00000000d001:RICE-WHT:QC' \
    -d "{\"locationId\":\"$QC\",\"reference\":\"Opening stock\",\"notes\":null,\"lines\":[{\"itemId\":\"$RICE\",\"quantity\":120,\"unitCost\":48.5}]}" \
    -D - -o /dev/null http://localhost:4200/api/documents/receipts | grep -iE '^HTTP|Idempotent-Replayed'     # 200 + replayed, both runs
  kill %1; wait
done
# On a volume that had QC renamed or S5c smoke data the replay can be 409 (D1); on a fresh volume it is 200.

# R6: after build-unit opens the PR
gh pr checks IMS-9                       # build ✓  test ✓
```

## Rollback
Revert the PR. There's no schema change. The opening-stock receipts already posted stay in the append-only ledger as valid documents. The reverted seeder no longer posts them, and S5d's re-landing replays them by key.

## Proposed Downstream (for TASKS.md after merge, not in this PR)
- **S7:** documents with `PostedBy` `00000000-0000-0000-0000-000000000000` are seed postings: show "System". The demo tenant has 31 opening-stock receipts at QC and MKT.
- **S12 (T14):** the demo tenant's seeded items carry opening stock, except `CANDLE-WHT`. T14's "receive 10, issue 3, see 7 / ₱700.00" uses `CANDLE-WHT` or an item the test creates, or it asserts deltas.
- **S11:** the seeded demo tenant should pass `GET /api/ledger/health`, which gives a free check on a non-test tenant. Add that check inside `SeedTests`' single `[Fact]`, or in `Platform.IntegrationTests`. Never add it as a new `Inventory.IntegrationTests` class.
- **Tests:** `SeedTests` owns `SeedTenants.Demo` in the Inventory test container, which it leaves renamed (`QC-OLD` plus a new `QC`). It's the one allowed exception to §11's "own tenant" rule. Compare idempotency keys with `StringComparer.Ordinal` or in posting order, never with `ORDER BY IdempotencyKey`.
- **Seed:** `InventorySeeder.OpeningStock` is the single source of the opening quantities. Adding a row posts it on the next start, but editing a row's quantity or cost doesn't repost it, because the key is taken (D1). A changed opening balance needs an adjustment (S6).

## Log
- 2026-11-11: planned (clarify pass: D1–D3 answered with the recommended defaults)
- 2026-11-11: audited (PASS-WITH-CHANGES), 3 fixes applied: ordinal or posting-order key comparison (01, 02); `Snapshot.BalanceRows` from one flat list (01); `SeedTests` owns the demo tenant, plus the §11 exception (D8, Blast radius, Downstream). NOTEs not applied.
