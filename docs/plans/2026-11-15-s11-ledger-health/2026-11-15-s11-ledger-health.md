---
type: plan
kind: feature
status: done
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-15
risk: high
prompts: 6
graph_built: 2026-11-15 21:38 (refreshed for this plan; `graphify affected` finds C# call edges only inside PostingEngine, so the rest of the blast radius is from grep)
tags: [plan]
---
# S11 Ledger health (IMS-15)

## Request
> IMS-15

This plans the existing unit `IMS-15 — S11 Ledger health · Added · spec: §12 S11 · depends: IMS-13 · model: opus` (TASKS.md, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S11 (`:680`): *"Health (INV-1 to INV-5) and rebuild (exclusive ledger lock) endpoints, admin screen. Tests: T9 · code-guardian review. Tier: Should"*.

Spec §11 T9 (`:642`): *"Seeded random run: 500 operations over 3 items × 3 locations, valid and invalid; one rebuild runs while postings continue → INV-1 to INV-5 hold every 50 operations and after the concurrent rebuild; rebuild diff is empty"*. §13 (`:700`): *"T9: invariants hold through the random run and the rebuild diff is empty."*

Spec rules used: §6 invariants (`:335-341`), Rebuild (`:343`: exclusive `ledger:<TenantId>` lock; `GROUP BY` for `OnHand`, `StockValue`, `LastMovementId`; `LastUnitCost` "from the same windowed query that checks INV-5"; diff returned, then overwritten in the same transaction; health takes no lock, one statement per invariant), step 3 (`:309` the applock), §7 LUC definition (`:383-390`), §8 (raw SQL through EF; RLS), §9 (`:549-550` both routes Admin), §10 (`:606` `/admin/ledger`: pass/fail with failing rows; Rebuild with a confirm step and the returned diff).

It also covers what earlier units locked for S11:
- IMS-7 (S5b): *"INV-5's windowed SQL must return what `CostingPolicy.LastUnitCost` returns. T9 can compare the two."*
- IMS-8 (S5c) deferred nit: *"(S11) the applock's deadlock return of -3 isn't retried."* (resolved as D6).
- IMS-9 (S5d): *"(S11) check ledger health on the demo tenant inside `SeedTests` or `Platform.IntegrationTests` only; `SeedTests` owns `SeedTenants.Demo` and leaves QC renamed. Compare keys with `StringComparer.Ordinal`."*
- IMS-14 (S10): *"(S11) rebuild must also reset empty `(item, ADJ)` balance rows (0 / 0 / null `LastMovementId`); INV-5 must include revaluation movements (qty 0, same document as the inflow); INV-3 is toggle-aware."*

| # | Requirement |
|---|---|
| R1 | `GET /api/ledger/health` (Admin; Staff `403`) returns INV-1 to INV-5 for the current tenant: per invariant its id, name, pass/fail, the failure count and up to 100 failing rows (item and SKU, location and code, movement, check, expected, actual). It takes no lock; each check is one SQL statement, so RCSI gives each a consistent snapshot while postings continue. |
| R2 | INV-5's ledger value of `LastUnitCost` is exactly what `CostingPolicy.LastUnitCost` returns (§7: latest inflow that isn't a reversal and wasn't reversed, with its revaluation), and it comes from the same projection query the rebuild uses. A test compares the two for every physical balance. |
| R3 | `POST /api/ledger/rebuild` (Admin; Staff `403`) takes `ledger:<TenantId>` in `Exclusive` mode and recomputes `OnHand`, `StockValue`, `LastMovementId` and `LastUnitCost` for every balance of the tenant from the ledger. That includes resetting rows with no movements to 0 / 0 / null / null and inserting a missing row. It returns the differences it found, then overwrites them in the same transaction. On a healthy ledger the diff is empty and nothing is written. |
| R4 | Both routes see and write only the current tenant: T5 probes with two tenants. |
| R5 | `/admin/ledger` (Admin only; nav link "Ledger health" for Admins): INV-1 to INV-5 pass/fail with failing rows, "Check again", and a Rebuild button with a confirm step that then shows the returned diff (or "nothing to fix") and re-checks. |
| R6 | T9: a seeded random run of 500 operations over 3 items × 3 locations, valid and invalid. INV-1 to INV-5 hold every 50 operations, both in the test oracle and from `GET /api/ledger/health`. A rebuild runs while postings continue; its diff is empty and the invariants hold afterwards. The demo tenant's ledger is healthy after the seed (IMS-9 downstream). |
| R7 | Every `## Commands` line and CI `build` and `test` are green; code-guardian reviews the PR (High-stakes `Persistence/**`, `Features/Ledger/**`; CLAUDE.md lists S11). |

## Context used
- Note: `CLAUDE.md` (Conventions: decimal, `AwayFromZero` 4 dp, no hard deletes; Review constraints: no UPDATE/DELETE on documents/movements, `TenantId` never from a request, no unnamed `IgnoreQueryFilters()`, API as `inventory_app`; High-stakes paths include `Persistence/**` and `Features/{Posting,Reversals,Ledger}/**`; Hidden coupling: `AllowNegative` mirrors `Locations`, slugs ↔ `PROBLEM_MESSAGES`, vector tables ↔ `CostingPolicy` tests).
- Note: spec §6 `:309,:335-343`, §7 `:383-390`, §8, §9 `:549-550`, §10 `:606`, §11 T9 `:642`, §12 `:663,:680` (S11 is the first slice cut if the sprint runs long), §13 `:700`.
- Note: TASKS.md Downstream for IMS-3 (anything holding locks runs in a transaction inside the execution strategy), IMS-4 (raw SQL only through EF; T5 probe per route; tests create their own tenant), IMS-5 (`Features/<Area>/<Endpoint>/{Endpoint,Handler,…}.cs`; T5 raw text), IMS-6 (don't U-lock ledger reads; `LedgerRows` and `WaitUntilBlockedAsync`; `--max-parallel-test-modules 1` against the Testcontainers flake), IMS-7/8/9/14 bullets above.
- Graph: `graphify update .` (stale since 20:15; last commit 21:37). `graphify affected "TakeLedgerLockAsync" --depth 2` → `AttemptAsync`, `AttemptReversalAsync`, `PostAsync`, `ReverseAsync` (PostingEngine only). `graphify affected "InventoryModuleExtensions"` → no nodes (wiring is by DI and extension calls). `god-nodes` isn't in this graphify build. `grep 'ledger:\|sp_getapplock'` → only `PostingEngine.cs:419-424`.
- Code: `Persistence/PostingEngine.cs:91-114` (`RunAsync`: strategy, `RetryLimitExceededException` → `RetriesExhaustedOutcome`), `:117-125,:268-277` (transaction then `TakeLedgerLockAsync` as the first statement), `:148-166` (step 5 insert of a balance row copying `IsVirtual`/`AllowNegativeStock` from `Locations`; `(item, ADJ)` rows for every receipt/transfer line), `:415-427` (`TakeLedgerLockAsync`: `Shared`, owner `Transaction`, throws on < 0), `:495-541` (`WriteAsync`: `LastMovementId` = max new movement per key, virtual included).
- Code: `Domain/CostingPolicy.cs:76-77` (`LastUnitCostAfterInflow`), `:85-137` (`LastUnitCost`: inflow = `To` = key, `Quantity > 0`, not a reversal, not reversed; `end` = max id of the inflow's document among the key's movements; sums up to `end`).
- Code: `Persistence/StockBalance.cs`, `Persistence/InventoryDbContext.cs:129-145` (balance CHECKs `:133-135`, `IsRowVersion` `:141`), `:126` (FK on `ReversesMovementId`, so a reversal always names an existing movement).
- Code: `Features/Items/ListItemMovements/Handler.cs:32-60` (raw `SqlQuery` with In/Out legs and explicit `TenantId` on every table; "no ORDER BY, CTE or trailing semicolon" because it composes LINQ on top), `Features/Locations/UpdateLocation/Handler.cs:32-92` (strategy + transaction pattern; S10's explicit `TenantId` predicate on raw locks), `Features/Reversals/ReverseDocument/Endpoint.cs` (Admin endpoint pattern), `Features/Stock/StockEndpoints.cs` (group pattern), `Features/InventoryPolicies.cs`, `Features/Problems.cs:21-22` (`ConcurrencyConflict`), `InventoryModuleExtensions.cs:40-83`, `src/Api/Program.cs:60-62` (`/api` group requires auth).
- Tests: `tests/Inventory.IntegrationTests/LedgerInvariants.cs` (the oracle; `MovementsAsync` is private), `LedgerRows.cs:20-30,64-117,119` (`OpenAsync`, `AddDocumentAsync`, `AddMovementAsync`, `AddBalanceAsync`, `WaitUntilBlockedAsync`), `TenantIsolationTests.cs:24-30,153-190,679-701` (Exempt list, list probe, route guard), `PostingTests.cs:20-21,556-600`, `TransferTests.cs:17,342-345`, `AdjustmentTests.cs:17,437-440`, `ReversalTests.cs:20`, `LocationsTests.cs:668` (`AllowNegativeAsync`), `DatabaseFixture.cs:27-31,59-78` (`AdminConnectionString`, `CreateTenantWithUserAsync`, `SignInAsync`), `SeedTests.cs:31-112` (one ordered `[Fact]`), `PostingClockTests.cs:93-107` (T10 scans `*Request`/`*Line` types for dates).
- Web: `web/src/app/app.routes.ts`, `app.html` (Admin-only link pattern), `app.spec.ts:81-92`, `core/auth/auth.service.ts:20` (`isAdmin`), `documents/post-stock.guard.ts` (functional guard with `ensureLoaded()`), `documents/document-detail.ts:131-191` (confirm → call → error handling), `shared/confirm-dialog.ts`, `shared/format.ts` (`formatMoney`, `formatQuantity`), `items/item-detail.html:22,50` (plain tables), `stock/stock.service.ts` and `documents/documents.service.spec.ts` (service and HTTP test pattern).

## Decisions
- **D1. Re-plan of the legacy unit.** Unit id `IMS-15`, sprint 1, epic IMS-1, base `develop`. One unit, one PR.
- **D2. One projection query for INV-1, INV-5 and the rebuild; SQL sums, C# rounds.** A new `Persistence/LedgerProjection` runs one statement that returns, per `(item, location)` key (every balance row and every key a movement touches), the stored values and the ledger values: `Σ` in − out of quantity and cost, `MAX(Id)`, and for physical keys the LUC inputs (the inflow's quantity and cost, and the running `OnHand`/value up to the end of the inflow's document). The one division and `Round4` run in C# through `CostingPolicy.LastUnitCostAfterInflow`. This is because T-SQL `decimal` division cuts the scale to 6, while C# `decimal` keeps 28 significant digits, so a SQL `ROUND(a/b, 4)` can't be shown to equal `Round4` in every case. With the split, "INV-5's SQL returns what `CostingPolicy.LastUnitCost` returns" (IMS-7) holds by construction, and the sums are exact. Rejected: all-SQL division (unprovable equality); loading the whole ledger into C# and reusing the oracle (O(movements) memory, and two statements give no single snapshot without enabling `SNAPSHOT` isolation).
- **D3. Health: no lock, no transaction, one statement per check.** There are four statements: the projection (INV-1 + INV-5), INV-2, INV-3 and INV-4. Each sees one RCSI snapshot (spec §6). Failing rows are capped at 100 per invariant, and `failureCount` is the true total. Rows are ordered by SKU, then location code (`StringComparer.Ordinal`), then movement id.
- **D4. Rebuild.** Inside `CreateExecutionStrategy()`, one transaction. `LedgerLock.AcquireAsync(Exclusive)` is the first statement, then the projection (D2), then the writes, then the diff is returned and the transaction commits. The writes cover differing rows only, in clustered-key order (`SqlGuid` item, then location): a key-targeted raw `UPDATE` of the four projection columns (`… WHERE TenantId = @t AND ItemId = @i AND LocationId = @l`), and an `INSERT … SELECT FROM Locations` for a missing row (copying `IsVirtual`/`AllowNegative` as step 5 does). There's no rowversion check: the applock excludes postings, and a toggle changes only `AllowNegative`, which the rebuild never writes. A non-empty diff logs a Warning with the tenant id, user id and difference count (§8: log ids, never bodies). Exhausted retries return `409 concurrency-conflict` (existing slug).
- **D5. Extract `Persistence/LedgerLock`.** `AcquireAsync(DatabaseFacade, Guid tenantId, LedgerLockMode, CancellationToken)` holds the resource text `ledger:{tenantId:D}`, owner `Transaction`, and the result check. `PostingEngine.TakeLedgerLockAsync` calls it with `Shared`, so its behaviour is identical. The resource name is the coupling that makes the rebuild exclusive; one definition beats a guarantee that only a test enforces.
- **D6. IMS-8 nit (`-3` not retried): no code change.** Both callers take the applock as the first statement of a fresh transaction and hold no other lock, so they can't be part of a deadlock cycle; `-3` is unreachable. `LedgerLock`'s doc comment records this, and the throw stays.
- **D7. INV-3 reads the location, not the mirror.** "Physical" means `Locations.Kind = 'Physical'`, and "toggle off" means `Locations.AllowNegativeStock = 0`. Because the balance CHECKs already forbid the other INV-3 shapes, the only reachable INV-3 failure is a negative balance whose mirror says allowed while its location says no. Reading the mirror would hide exactly that case.
- **D8. Expected values per key.** For a physical key: `OnHand` = Σ in − out, `StockValue` = Σ cost in − out, `LastMovementId` = max touching id, and `LastUnitCost` from D2 (null when no qualifying inflow). For a virtual key: the same `OnHand` and `LastMovementId`, with `StockValue` 0 and `LastUnitCost` null. A row with no movements expects 0 / 0 / null / null (IMS-14). A key with movements but no row fails INV-1 with check `MissingBalance`, and the rebuild inserts it.
- **D9. No migration, no new table, no new slug, no §6 change.** Spec §9 already lists both routes. `PROBLEM_MESSAGES` is untouched.
- **D10. T5 by probes, not exemptions.** A two-tenant test drifts tenant B's balance. A's health stays healthy and lists none of B's ids; A's rebuild returns an empty diff and leaves B's row drifted. B's health reports the drift, and B's rebuild fixes it.
- **D11. UI.** Route `admin/ledger`, guarded by a new functional `adminGuard` (copying `post-stock.guard.ts`), with an Admin-only "Ledger health" nav link. Health loads on open. Invariants show with spec ids and plain names, plus failing rows in plain tables (the `item-detail.html` pattern, not AG Grid) inside a horizontally scrolling wrapper. Rebuild goes through `ConfirmDialog`, then shows the diff table, then re-runs health.
- **D12. T9 shape.** A new `LedgerRandomRunTests` uses a fixed seed that appears in every assertion message. It runs with its own tenant: 3 items, 3 physical locations (one with the toggle on), and a mix of receipts, issues, transfers, adjustments (±, with and without unit cost), reversals of random earlier documents, idempotent replays and deterministic invalid requests. Each operation asserts a status from an allowed set, never `500`. The run also asserts that each outcome class (each `201` type, `422`, `409`, replay `200`, `400`) occurred, so it can't pass vacuously. At every 50th operation it checks the oracle, the health endpoint, and the projection's LUC against `CostingPolicy.LastUnitCost`. One burst of concurrent postings runs with a rebuild, and with health calls, mid-run.
- **D13. T9 is reproducible across the whole run (Codex audit 2026-11-15, FIX 1).** A fixed seed doesn't control how the server schedules concurrent requests. So the burst contains only operations whose status doesn't depend on order: receipts and `+` adjustments with a unit cost, all `201` (`PostingConcurrencyTests.cs:524` already asserts 50 × `201` under contention). Burst documents go into the replay pool but never into the reversal pool, because whether a burst document is "latest" on a key depends on scheduling. Burst outcomes are counted under their own kind. With that, every later status depends only on quantities, which don't depend on order, so two runs give identical outcome counts over all 500 operations, and the "each outcome occurred" assertion is deterministic. Only costs after the burst can differ (`LastUnitCost` takes the latest inflow), and each run checks them with the oracle. Rejected: comparing counts only for operations before the burst. The operations after it inherit its state through the reversal pool and the random sequence, so that comparison would still be flaky.
- **D14. Test command (Codex audit 2026-11-15, FIX 2).** `## Commands` and CI (`.github/workflows/ci.yml:45`) run plain `dotnet test`, and so does the verification. If it fails only with the Testcontainers start-up crash (`handle-crash.sh`, fixture failures before any test body runs; IMS-6), rerun `dotnet test --max-parallel-test-modules 1` and log both results, as S5a's Log did. A failure of any other kind is real. CI's plain `dotnet test` must be green. A serial run never stands in for the declared command without the logged crash.

**Assumptions:**
- A rebuild runs in milliseconds at portfolio scale, and postings wait on the applock meanwhile (no `@LockTimeout`; the client command timeout is 30 s). This is not measured under load.
- A rebuild whose ledger value violates a balance CHECK (a ledger-negative balance at a location whose toggle is now off) can only follow manual corruption of a balance row. The `547` rolls the rebuild back and surfaces as `500`; it is not mapped.
- An uncomposed `db.Database.SqlQuery<T>(…).ToListAsync()` runs the SQL as written, so a CTE is allowed. If EF still wraps it, the executor uses derived tables and `OUTER APPLY` instead (the S8 handler's note is about composed queries).
- The rebuild needs no `Idempotency-Key`: it isn't a document, and a second run returns an empty diff.
- There is no durable audit row for a rebuild. Balances are a projection and the movements are the record; the Warning log (D4) is the trail. **Compliance flag:** if a later sprint needs an auditable "who rebuilt what", that's a new table, so ask first.
- Toggles and deactivations race the rebuild only on balance and FK locks. A `1205` there is retried by the strategy on both sides, as it is for postings (IMS-6).

**Non-goals:** repairing `IsVirtual`/`AllowNegative` mirror drift (health reports the INV-3 case; rebuild writes only the four projection columns); the S10 follow-ups (`DeactivateLocation/Handler.cs:44` `TenantId` predicate, `(item, ADJ)` contention under load); scheduled health checks or alerts; a cross-tenant rebuild; paging failing rows; Playwright (S12); any change to spec §6, migrations, `StockMovements` or `StockDocuments`.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Persistence/LedgerLock.cs` | C | `ledger:<TenantId>` applock in `Shared`/`Exclusive` (D5, D6) | R3 | 01 |
| `src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs` | M | `TakeLedgerLockAsync` delegates to `LedgerLock` (`Shared`); no behaviour change | R3 | 01 |
| `src/Modules/Inventory/Inventory/Persistence/LedgerProjection.cs` | C | Stored vs ledger values per key; LUC via `CostingPolicy` (D2, D8) | R1, R2, R3 | 01 |
| `tests/Inventory.IntegrationTests/LedgerInvariants.cs` | M | `MovementsAsync` private → internal, for the LUC comparison | R2, R6 | 01 |
| `tests/Inventory.IntegrationTests/LedgerProjectionTests.cs` | C | Projection = stored = oracle over V/N/X/A flows, midpoint, reversed inflow; drift and missing row | R2 | 01 |
| `src/Modules/Inventory/Inventory/Features/Ledger/LedgerEndpoints.cs` | C | `/ledger` group (prompt 03 adds rebuild) | R1, R3 | 02 |
| `src/Modules/Inventory/Inventory/Features/Ledger/GetLedgerHealth/Endpoint.cs` | C | `GET /ledger/health`, Admin | R1 | 02 |
| `src/Modules/Inventory/Inventory/Features/Ledger/GetLedgerHealth/Handler.cs` | C | INV-1/5 from the projection; INV-2/3/4 SQL (D3, D7) | R1 | 02 |
| `src/Modules/Inventory/Inventory/Features/Ledger/LedgerHealthResponse.cs` | C | Response records (contract below) | R1 | 02 |
| `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs` | M | Register the handlers, map `/ledger` | R1, R3 | 02, 03 |
| `tests/Inventory.IntegrationTests/LedgerHealthTests.cs` | C | Healthy, each invariant's failure, Staff 403 | R1 | 02 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | M | T5 probes for both routes (D10) | R4 | 02, 03 |
| `src/Modules/Inventory/Inventory/Features/Ledger/RebuildLedger/Endpoint.cs` | C | `POST /ledger/rebuild`, Admin | R3 | 03 |
| `src/Modules/Inventory/Inventory/Features/Ledger/RebuildLedger/Handler.cs` | C | Exclusive lock, diff, write, log (D4) | R3 | 03 |
| `src/Modules/Inventory/Inventory/Features/Ledger/LedgerRebuildResponse.cs` | C | Response records (contract below) | R3 | 03 |
| `tests/Inventory.IntegrationTests/LedgerRebuildTests.cs` | C | Empty diff, drift fixed, ADJ reset, missing row, exclusivity, Staff 403 | R3 | 03 |
| `tests/Inventory.IntegrationTests/LedgerRandomRunTests.cs` | C | T9 (D12) | R6 | 04 |
| `tests/Inventory.IntegrationTests/SeedTests.cs` | M | Demo tenant health after both seeds (IMS-9) | R6 | 04 |
| `web/src/app/ledger/ledger.service.ts` (+ `.spec.ts`) | C | `health()`, `rebuild()` and types | R5 | 05 |
| `web/src/app/core/auth/admin.guard.ts` (+ `.spec.ts`) | C | Admin-only route guard | R5 | 05 |
| `web/src/app/app.html`, `app.spec.ts` | M | Admin-only nav link and its test | R5 | 05 |
| `web/src/app/ledger/ledger-health.ts`, `.html`, `.scss`, `.spec.ts` | C | The screen (D11) | R5 | 06 |
| `web/src/app/app.routes.ts` | M | Route `admin/ledger` behind `authGuard` + `adminGuard`, added with the component it loads | R5 | 06 |

**API contract** (prompts 02, 03, 05 and 06 share it; JSON is camelCase, and decimals are numbers):
```jsonc
// GET /api/ledger/health → 200
{ "checkedAt": "2026-11-15T13:00:00+00:00", "healthy": false,
  "invariants": [                       // always 5, in order INV-1..INV-5
    { "id": "INV-1", "name": "Projection", "passed": false, "failureCount": 1,
      "failures": [ { "itemId": "…", "sku": "RICE-WHT", "locationId": "…", "locationCode": "QC",
                      "movementId": null, "check": "OnHand", "expected": 120, "actual": 121 } ] } ] }
// names: INV-1 Projection · INV-2 Conservation · INV-3 Physical sanity · INV-4 Reversal mirror · INV-5 Last unit cost
// checks: INV-1 MissingBalance | OnHand | StockValue | LastUnitCost (virtual only) | LastMovementId
//         INV-2 OnHandSum (location fields null; expected 0)
//         INV-3 NegativeNotAllowed | ValueWithStock | ValueWithoutStock
//         INV-4 Item | Quantity | TotalCost | FromLocation | ToLocation (movementId = the reversal movement;
//               expected/actual null for the id checks) | ReversesReversal | ReversedTwice (movementId = the original;
//               actual = the count)
//         INV-5 LastUnitCost
// POST /api/ledger/rebuild → 200 (no body, no Idempotency-Key)
{ "rebuiltAt": "…", "balancesChecked": 69,
  "differences": [ { "itemId": "…", "sku": "…", "locationId": "…", "locationCode": "…", "inserted": false,
                     "before": { "onHand": 5, "stockValue": 50, "lastUnitCost": 10, "lastMovementId": 41 },   // null when inserted
                     "after":  { "onHand": 4, "stockValue": 40, "lastUnitCost": 10, "lastMovementId": 41 } } ] }
```

## Blast radius
**Risk:** high. The slice touches `Persistence/**` (including the posting engine's lock call), writes `StockBalances` under an exclusive ledger lock, and adds raw SQL under RLS in `Features/Ledger/**`.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `PostingEngine.AttemptAsync` / `AttemptReversalAsync` → `PostAsync`, `ReverseAsync`, `DocumentPoster`, `ReverseDocumentHandler`, `InventorySeeder` | Direct + transitive | `TakeLedgerLockAsync` | No: same resource, mode, owner and failure behaviour | Prompt 01 runs every posting, reversal, concurrency and seed test |
| Postings during a rebuild | Non-graph (applock) | `ledger:<TenantId>` `Exclusive` | No, but they wait for the rebuild's duration | Rebuild is a short read plus writes to drifted rows only; T9's burst asserts every posting `201` (order-independent operations, D13) |
| Toggle (`UpdateLocation`) and deactivations vs rebuild | Non-graph (row and FK locks) | rebuild's `UPDATE`/`INSERT` on `StockBalances` | Possible `1205`; no correctness break | Both sides run under the execution strategy (IMS-6); rebuild never writes `AllowNegative` (D4) |
| `StockBalances` CHECKs | Non-graph (schema) | rebuild writes | Only after manual corruption (Assumption) | 547 rolls back; documented |
| RLS `security.TenantPolicy` / T6 catalog | Non-graph | raw SQL in health and rebuild | No new tables; RLS filters every statement | Explicit `TenantId = @t` on every table as well (S8/S10 convention); D10 probes |
| T5 route guard (`TenantIsolationTests.cs:679-701`) | Test | two new `/api` routes | Yes, the guard fails until probed | Prompts 02 and 03 add the `[ProbesRoute]` tests |
| T10 `No_request_type_carries_a_date` | Test | Features types named `*Request` | No: no request types (responses carry dates) | — |
| T12 architecture rules (one public type, unnamed `IgnoreQueryFilters` ban, `Domain` deps) | Test | new internal types; Persistence → Domain | No | Everything `internal`; no `IgnoreQueryFilters` |
| `LedgerInvariants` oracle and its callers (`NegativeStockTests`, `PostingVectorTests`) | Test | `MovementsAsync` visibility | No | Visibility only |
| `SeedTests` single ordered fact | Test | adds health assertions | No; the order stays, read-only checks | Assert after step 3 and after step 4 |
| Hidden coupling: §7 LUC ↔ `CostingPolicy.LastUnitCost` ↔ new projection SQL | Non-graph | D2 | New coupling | Division in C# (D2); `LedgerProjectionTests` and T9 compare per balance |
| Hidden coupling: S10 empty `(item, ADJ)` rows | Non-graph | projection keys | Would false-fail if the projection inner-joined | Full outer join over balance and movement keys (D8); tested |
| Web `app.html` nav, `app.spec.ts` | Direct | new Admin-only link | No | Prompt 05 adds the Admin-only test |
| `PROBLEM_MESSAGES` / Kernel `ProblemTypes` | Hidden coupling | — | No: no new slug (D9) | — |

**Known risks in the touched area:** the Testcontainers start-up flake (IMS-6): run plain `dotnet test` first, and fall back to `--max-parallel-test-modules 1` only after a logged start-up crash (D14); restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1` (IMS-7); compare decimals by value, not text (IMS-8: `1` vs `1.0000`); `HOLDLOCK` next-key locks from deactivations can briefly block unrelated rows (IMS-6).

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-15 | S11 Ledger health | Added | 01, 02, 03, 04, 05, 06 | IMS-13 | yes | R1: Admin `GET /api/ledger/health` returns 5 invariants with pass/fail, counts and ≤ 100 failing rows; Staff 403. R2: projection LUC = `CostingPolicy.LastUnitCost` for every physical balance in tests. R3: Admin `POST /api/ledger/rebuild` under the exclusive lock returns the diff and fixes it; empty diff and no writes on a healthy ledger; empty ADJ rows reset; a missing row inserted. R4: two-tenant probes pass. R5: `/admin/ledger` shows results, confirms and shows the diff; Admin-only route and link. R6: T9 green; demo tenant healthy after the seed. R7: Commands green; code-guardian review. |

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Ledger lock and projection | IMS-15 | opus | high | — | A | `Persistence/LedgerLock.cs`, `Persistence/PostingEngine.cs`, `Persistence/LedgerProjection.cs`, `tests/…/LedgerInvariants.cs`, `tests/…/LedgerProjectionTests.cs` |
| 02 | Health endpoint | IMS-15 | opus | high | 01 | B | `Features/Ledger/{LedgerEndpoints.cs, LedgerHealthResponse.cs, GetLedgerHealth/Endpoint.cs, GetLedgerHealth/Handler.cs}`, `InventoryModuleExtensions.cs`, `tests/…/LedgerHealthTests.cs`, `tests/…/TenantIsolationTests.cs` |
| 03 | Rebuild endpoint | IMS-15 | opus | high | 02 | C | `Features/Ledger/{LedgerEndpoints.cs, LedgerRebuildResponse.cs, RebuildLedger/Endpoint.cs, RebuildLedger/Handler.cs}`, `InventoryModuleExtensions.cs`, `tests/…/LedgerRebuildTests.cs`, `tests/…/TenantIsolationTests.cs` |
| 04 | T9 random run and seed health | IMS-15 | opus | high | 03 | D | `tests/…/LedgerRandomRunTests.cs`, `tests/…/SeedTests.cs` |
| 05 | Web: service, guard, nav | IMS-15 | sonnet | medium | — | A | `web/src/app/ledger/ledger.service{,.spec}.ts`, `core/auth/admin.guard{,.spec}.ts`, `app.html`, `app.spec.ts` |
| 06 | Web: ledger health screen and route | IMS-15 | sonnet | high | 05 | B | `web/src/app/ledger/ledger-health.{ts,html,scss,spec.ts}`, `app.routes.ts` |

Groups A and B pair a .NET prompt with a web prompt: their files and toolchains are disjoint. 02 and 03 have 7 files each, above the guideline of about 5. The route, its registration and its T5 probe must land together or the route guard fails, and the extra files are small records.

- 01 opus: High-stakes `Persistence/**`; edits the posting engine's lock call; the projection must equal `CostingPolicy.LastUnitCost` exactly.
- 02 opus: High-stakes `Features/Ledger/**`; raw SQL under RLS that defines the invariants the portfolio claims.
- 03 opus: writes `StockBalances` under an exclusive ledger lock, with lock order against postings, toggles and CHECKs.
- 04 opus: a seeded concurrency test (T9) that must be deterministic and non-flaky; it's the slice's evidence.

## End-to-end verification
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test   # the declared command; only after a start-up crash: dotnet test --max-parallel-test-modules 1, logging both (D14)
(cd web && npm run lint && npm run build && npm test -- --watch=false)
# R1, R4
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerHealthTests' --filter-class '*TenantIsolationTests'
# R2
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerProjectionTests'
# R3
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerRebuildTests'
# R6
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*LedgerRandomRunTests' --filter-class '*SeedTests'
# R7 (no new public type, no unnamed IgnoreQueryFilters)
dotnet test --project tests/Architecture.Tests
```
- R5 manual: `dotnet run --project src/AppHost`, sign in as `admin@demo.test`, open "Ledger health". All five pass. Rebuild → confirm → "Nothing to fix". As Staff, there's no link, and `/admin/ledger` redirects to `/`.

## Rollback
Revert the PR. There's no migration or schema change, and `PostingEngine`'s lock is behaviour-identical, so no data step is needed. A rebuild that already ran only rewrote projection columns to their ledger values, which is correct under any version.

## Log
- 2026-11-15: planned
- 2026-11-15: audited, 2 fixes applied (Codex PASS-WITH-CHANGES: D13 T9 burst order-independent, D14 plain `dotnet test` first)
- 2026-11-15: applied Codex audit (2 applied, 0 disputed)
- 2026-11-15: build started on IMS-15; rollback point dd38cf3e34a69554fac02e2aa5bbf9bca1835db3
- 2026-11-15: 01 done (opus, 0 retries): LedgerLock (Shared/Exclusive, mode as fixed-switch parameter) + LedgerProjection (one CTE statement, LUC via CostingPolicy); 80/80 integration, 21/21 architecture; verified SQL matches CostingPolicy.LastUnitCost rule for rule
- 2026-11-15: 02 done (opus, 0 retries): GET /api/ledger/health (4 lock-free statements: projection→INV-1/5, INV-2, INV-3 vs location toggle, INV-4 one UNION ALL matching the oracle); T5 probe; 8 LedgerHealthTests; verified 38/38 integration, 21/21 architecture, build + format clean, scope = 7 declared files
- 2026-11-16: 03 done (opus, 1 retry — orchestrator error: a premature completion report led to a second agent dispatched while the first was still running; the retry only added the route/DI lines, the first agent finished the rest, deduped, and the merged result was verified): POST /api/ledger/rebuild (Exclusive applock first, key-targeted INSERT/UPDATE of the 4 ledger columns only, rows-affected = 1 asserted, decimals as decimal(19,4) SqlParameters); T5 probe; 7 LedgerRebuildTests incl. engine-vs-LedgerLock exclusive wait; verified build + format clean, 64/64 filtered integration, 21/21 architecture, scope = 7 declared files. Note for code-guardian: no test writes a 4-dp ledger value back through rebuild (T9 in 04 should cover it).
- 2026-11-16: 04 done (opus, 0 retries): T9 LedgerRandomRunTests (seed 20261115, 500 ops + 20-posting order-independent burst with concurrent rebuild and 3 health calls; exact status predicted by an OnHand/last-movement model; 3 views every 50 ops) + demo-tenant health in SeedTests step 3 and after step 4; verified build + format clean, two runs with identical 17-line outcome counts (~9 s each), burst 20×201, rebuild diff empty, plain dotnet test 578/578, scope = 2 declared files. Reversal 422 insufficient-stock is allowed but unreachable under latest-only. 03's note stays open: the rebuild diff is empty, so T9 never writes a 4-dp value back (only shows the comparison holds at 4 dp).
- 2026-11-16: 05 done (sonnet, 0 retries): LedgerService (health/rebuild + contract types), adminGuard, Admin-only Ledger health nav link (own @if block after Locations), 3 spec files; verified lint + build + 177/177 web tests, scope = 6 declared files
- 2026-11-16: 06 done (sonnet, 0 retries): /admin/ledger LedgerHealth screen (5 labelled invariant blocks, failure tables, confirm-dialog rebuild with diff, ProblemDetails errors) + route behind authGuard/adminGuard; verified lint + build + 183/183 web tests, scope = 5 declared files
- 2026-11-16: applied Codex code review (3 applied, 0 disputed)
- 2026-11-16: IMS-15 merged (PR #15); plan done

## Outcome
**Shipped:** health and rebuild endpoints, `LedgerLock` and `LedgerProjection`, the T9 random run, seed-health checks and the `/admin/ledger` screen, in one PR (IMS-15, base `develop`). CI green; plain `dotnet test` 580/580; web 183/183.

**Deviations:** the Codex code review (REQUEST CHANGES, 0 blockers) was applied in a follow-up commit. The health handler now counts every failure but keeps and streams at most 100 rows per invariant; the projection still loads every balance key (D2), so only failure rows are bounded. INV-3 checks zero stock first. Added a 105-failure cap test and a 4-dp rebuild write-back test, and confirmed the latter fails with a scale-2 parameter.

**Gotchas:** posting is limited to 100 lines per document, so large-ledger tests need several receipts. Decimal `SqlParameter`s must be `decimal(19,4)` or EF sends `decimal(18,2)` and rounds. The R5 manual browser check was not done. Prompt 03's orchestration slip (a duplicate agent dispatched during the build) was merged and verified.
