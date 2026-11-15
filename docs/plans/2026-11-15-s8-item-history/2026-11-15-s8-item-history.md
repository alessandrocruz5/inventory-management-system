---
type: plan
kind: feature
status: draft
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-15
risk: medium
prompts: 5
graph_built: none
tags: [plan]
---
# S8 Item history (IMS-12)

## Request
> IMS-12

This plans the existing unit `IMS-12 — S8 Item history · Added · spec: §12 S8 · depends: IMS-10 · model: sonnet` (TASKS.md, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S8: *"Movements endpoint with running balance; balances in `GET /items/{id}`; item detail screen. Tests: Running balance matches V1–V6. Tier: Must"*.

Spec §9 rows: *"`GET /items/{id}` · any · Item with its balance in each physical location"*, *"`GET /items/{id}/movements` · any · Ledger history, newest first, with running balance per location (window `SUM() OVER`)"*.

Spec §10 row: *"`/items/:id` · Item detail · any · Balance per location; movement history with running balance; link to each document"*, and the `/stock` row: *"row click opens the item"*.

It also covers the S8 items that earlier units locked:
- IMS-5 (S4): *"S8: `GET /items/{id}` gains the balance per physical location."*
- IMS-11 (S7) plan Downstream `:224` and D4: *"S8: add the `/stock` row click → `/items/:id`. The item detail screen links each movement's document to `/documents/:id`. Reuse `shared/format.ts` (`formatMoney`, `formatQuantity`, `formatManilaDateTime`, `documentTypeLabel`, `postedByLabel`) and `StockService`."*

| # | Requirement |
|---|---|
| R1 | `GET /api/items/{id}` keeps every field it returns today and adds `balances`: one entry per **physical** location where the item has a `StockBalances` row (zero rows included), with `onHand`, `stockValue`, `averageCost` (`Round4(stockValue / onHand)` when `onHand > 0`, else null) and `lastUnitCost`, ordered by location code. |
| R2 | `GET /api/items/{id}/movements` returns the item's ledger history as a stock card: one row per movement per physical location it touches, newest first, paged (§9). Each row carries the signed quantity and value change, the counterpart location, the document (id, type), and the running on-hand and running value **at that location**, computed with a window `SUM() OVER` across the whole history, not just the page. An unknown or foreign item id is `404 not-found`. |
| R3 | `/items/:id` (any role) shows the item, its balance per location, and the paged movement history with running balance, each row linking to `/documents/:id`. |
| R4 | A click on a `/stock` row opens `/items/:itemId`; the pinned totals row does nothing. |
| R5 | The UI conventions hold (§10): money as ₱1,234.50, quantities without trailing zeros, dates in Asia/Manila, plain document labels, "Posted by" as System / You / Another user, usable at 375 px. |
| R6 | Tests: running balance matches V1–V6 through the API (qty and value); T5 probe for the new route; the balance read; Vitest for the service, the screen and the row click. |
| R7 | Every `## Commands` line and CI `build` and `test` are green. |

## Context used
- Note: `CLAUDE.md` (Conventions, Review constraints, High-stakes paths, Hidden coupling): decimal and AwayFromZero rounding; raw SQL "allowed for posting and reports, always through EF Core"; `IgnoreQueryFilters()` banned; T5 probes; "no write request carries a date" (T10, IMS-11 D1).
- Note: spec §5 (the `StockMovements` and `StockBalances` sketch; `IX_Movements_Item (TenantId, ItemId, Id)`; average cost derived, never stored; virtual locations go negative by design), §6 (INV-1: physical `StockValue` = Σ cost in − Σ cost out; reversals copy the original's cost; revaluation movements have quantity 0), §7 vector set V (V1–V6 movement costs and balances), §8 (raw SQL through EF inherits RLS; a foreign id is `404`), §9, §10, §12 S8.
- Note: spec audit `Inventory System — Sprint 1 Spec.audit.md:27(b)` (assigns the balance part of `GET /items/{id}` to S8) and `:51` (`sonnet` for S8).
- Note: TASKS.md Downstream for IMS-4 (T5 probe per route; raw SQL only through EF; a connection opened another way sees nothing), IMS-5 (`S8: GET /items/{id} gains the balance per physical location`; the `Features/<Area>/<Endpoint>/` layout; route raw text), IMS-6 (`FORCESEEK` reads; add no index; the Testcontainers start-up flake), IMS-7 (restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1`), IMS-8 (decimal scale differs between a 201 and a replay).
- Note: S7 plan `2026-11-13-s7-stock-screens.md` D3 (posted-by labels), D4 (row click deferred to S8), D6 (response shapes; average cost in C#, never SQL), Downstream `:224`.
- Graph: none (`graphify-out/` does not exist in this repo); blast radius from grep (workflow §4).
- Code: `Features/Items/GetItem/{Endpoint,Handler}.cs` (the item read; the handler's summary already says "S8 adds its balance per physical location"), `Features/Items/ItemEndpoints.cs:15-24` (the `/items` group), `Features/Items/ItemResponse.cs:6-9` (shared by list/create/update/deactivate, so not changed), `Features/Stock/ListStock/Handler.cs:23-70` (balance join and average cost pattern), `Features/Documents/ListDocuments/Handler.cs` (paged list pattern), `Features/Paging.cs:14-37` (`Resolve`, `ToPageAsync`), `Features/Problems.cs:15-19`, `InventoryModuleExtensions.cs:51-77` (handler registrations, group mapping).
- Code: `Persistence/StockMovement.cs` (columns; `Quantity` 0 only on a revaluation), `Persistence/LocationKind.cs`, `Persistence/InventoryDbContext.cs:52,95,121` (`Kind`/`Type` stored as strings; `IX_Movements_Item`), `Persistence/PostingEngine.cs:19,102-105` (`ITenantContext` injection and `SqlQueryRaw` usage), `Features/Items/DeactivateItem/Handler.cs:39` (`FromSql` in a handler), `Kernel/Tenancy/ITenantContext.cs`.
- Tests: `TenantIsolationTests.cs:222-239` (`Another_tenants_item_id_is_404`, the 404 probe to copy), `:629-634` (the route guard). `ItemsTests.cs:104-120` (`Get_returns_the_item_with_its_version`, stays green: the response is a superset). `DocumentQueryTests.cs:14-60` (test class shape, `GetAsync` helper, `FakeTimeProvider` via `ApiFactory { Clock }`). `PostingTests.cs:556-600` (`PostDocumentAsync`, `Receipt`, `Line`, `Issue`, `IssueLine`, `NewKey`, `ReceiveAsync`, `NewLocationAsync(client, ct, code)`, `NewItemAsync`), `TransferTests.cs:342-345` (`Transfers`, `Transfer`, `TransferLine`), `LedgerRows.cs:20,32,64-100` (`OpenAsync`, `LocationIdAsync`, `AddDocumentAsync`, `AddMovementAsync`).
- Web: `items/items.service.ts:45-47` (`get` has no caller outside its spec, grep), `items/items.service.spec.ts:84` (`get encodes the id`), `documents/document-detail.{ts,html}` (detail screen pattern: `paramMap`, `requestedId` guard, table styles with `data-label` at < 600 px), `documents/documents.ts:143-147` + `documents.html:35` + `documents.spec.ts:11-17,88-93` (row click pattern and its test), `stock/stock.{ts,html,spec.ts}` (pinned totals row), `shared/format.ts`, `app.routes.ts:13-17`, `core/api/paged.ts`.

## Decisions
- **D1 · History is a stock card (user, 2026-11-15).** One row per movement per **physical** location it touches. A transfer A→B is one movement and gives two rows: `Out` at A, `In` at B. A receipt gives one `In` row at its location (counterpart `SUP`), an issue one `Out` row (counterpart `CUS`), an adjustment one row against `ADJ`. A revaluation (quantity 0, S10) is a row with quantity change 0 and a value change; its direction comes from the side it sits on, like every movement, so it needs no special case. Virtual locations never get rows; they appear only as the counterpart.
- **D2 · Running quantity and running value (user, 2026-11-15).** `runningOnHand = SUM(quantityChange)` and `runningValue = SUM(valueChange)` `OVER (PARTITION BY LocationId ORDER BY MovementId ROWS UNBOUNDED PRECEDING)`. `quantityChange` is `+Quantity` on the `In` side and `−Quantity` on the `Out` side; `valueChange` is `±TotalCost` likewise. This is INV-1 over a prefix, so the newest row per location equals that balance's `OnHand`/`StockValue`. Ordering a partition by movement id is correct because a balance's postings are serialized under its `UPDLOCK` and take their ids at step 9 after the lock (§6), the same assumption `LastMovementId` relies on. One movement never has `From = To` (CHECK), so `MovementId` is unique within a partition.
- **D3 · Balances are the existing physical balance rows (user, 2026-11-15).** `GET /items/{id}` lists `StockBalances` rows with `!IsVirtual` for the item, zero rows included, joined to `Locations`, ordered by `Code`. Same set as `GET /stock?itemId=…`. A never-stocked item returns `balances: []`.
- **D4 · The history query is raw SQL through EF, composed with LINQ.** EF Core can't express `SUM() OVER`, and §9 names the window. `db.Database.SqlQuery<ItemMovementRow>($"…")` (an interpolated `FormattableString`, so values are parameters) returns the windowed rows; LINQ then orders (`MovementId DESC, Direction ASC`, so a transfer's `In` row sits above its `Out` row) and `ToPageAsync` counts and pages. EF wraps the raw SQL in a derived table, so the SQL has **no `ORDER BY`, no CTE and no trailing semicolon** (the IMS-6 derived-table gotcha). The window is computed inside the derived table, before paging, so page 2's running values are cumulative over the whole history.
- **D5 · Tenant isolation on the raw statement is twofold.** EF's named `Tenant` filter does not apply to `Database.SqlQuery` on an unmapped type, so: (1) the handler first loads the item through EF (`db.Items`, filtered) and returns `404` if it isn't there, and (2) every table reference in the SQL carries `TenantId = {tenant.TenantId}` from the injected `ITenantContext` (Kernel). RLS is a third layer underneath. The explicit predicate also lets the optimizer seek `IX_Movements_Item (TenantId, ItemId, Id)` on both leading columns. No new index (IMS-6).
- **D6 · Response shapes (the web contract).** JSON camelCase; decimals are numbers.
  - `GET /api/items/{id}` → `{ id, sku, name, baseUnit, isActive, version, balances: ItemBalance[] }`. `ItemBalance` = `{ locationId, locationCode, locationName, onHand, stockValue, averageCost: number|null, lastUnitCost: number|null }`. Average cost uses `CostingPolicy.Round4` in C# after the read (S7 D6), never SQL division. A new `ItemDetailResponse` is used only by `GET /items/{id}`; `ItemResponse` (list, create, update, deactivate) is unchanged.
  - `GET /api/items/{id}/movements?page&pageSize` → `Paged<ItemMovement>`. `ItemMovement` = `{ movementId, documentId, documentType, lineNo, postedAt, postedBy, direction: 'In'|'Out', locationId, locationCode, locationName, counterpartLocationId, counterpartLocationCode, counterpartLocationName, quantityChange, valueChange, runningOnHand, runningValue }`.
- **D7 · The movement history on screen is a plain table with real links.** Not AG Grid: each row's document cell is a `routerLink` to `/documents/:documentId` (§10 "link to each document"), which a grid cell can't give without a custom renderer. It reuses `document-detail`'s table styles, including the `data-label` card layout below 600 px, plus a `mat-paginator` (25/50/100).
- **D8 · One unit, IMS-12.** The existing ticket; §12 makes S8 one PR. Five prompts, API → web. It touches no `## High-stakes paths` glob (`Features/Items/**` is outside `Features/{Posting,Reversals,Ledger}`; no `Persistence/**`, `Domain/**` or migration), and CLAUDE.md doesn't list S8 for code-guardian. A code-guardian pass is still **recommended** because prompt 02 adds raw SQL over the ledger, where tenant isolation rests on D5 rather than the EF filter.

**Assumptions:**
- The history window runs over the item's whole history on every request. A small shop's item has hundreds to low thousands of movements, and `IX_Movements_Item` seeks them. Keyset paging or a cap is out of scope for sprint 1.
- Inactive items stay readable: `GET /items/{id}` and its movements return `200`, and the screen shows an "Inactive" note.
- History has no location filter. The table shows the location on each row, and the stock card per location is visible through the running columns. A filter is a later nicety.
- Only `/stock` gets the row click (§10). The Items grid keeps its row selection for edit/deactivate; linking it, and linking item SKUs on the document detail, is out of scope.
- A reversal (S9) shows as a row of type Reversal with the swapped direction; nothing in S8 special-cases it. S8 can't post one through the API yet.
- Rows of one transfer share `postedAt`, `documentId` and `movementId`; the `In`-before-`Out` tie-break only makes paging deterministic.
- `ItemMovementRow` materializes through `SqlQuery<T>` by column name. If EF can't bind a positional record, the row type uses `{ get; init; }` properties (prompt 02 says so).

**Non-goals:**
- A location or date filter on the history; totals on the history.
- Linking the Items grid or document lines to `/items/:id`.
- The Reverse button (S9), negative balances in red and revaluation rows produced through the API (S10), ledger health (S11).
- Any new index, column, migration, ProblemDetails slug, package or Platform contract.
- Changing `ItemResponse`, `Features/Posting/**`, `Persistence/**` or `Domain/**`.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Features/Items/ItemDetailResponse.cs` | C | `ItemDetailResponse`, `ItemBalanceResponse` | R1, D6 | 01 |
| `src/Modules/Inventory/Inventory/Features/Items/GetItem/Handler.cs` | M | Read the physical balances; return `ItemDetailResponse` | R1, D3 | 01 |
| `tests/Inventory.IntegrationTests/ItemHistoryTests.cs` | C | R1 facts (01), R2 facts incl. V1–V6 (02) | R1, R2, R6 | 01 (02 extends) |
| `src/Modules/Inventory/Inventory/Features/Items/ItemMovementResponse.cs` | C | `ItemMovementResponse` | R2, D6 | 02 |
| `src/Modules/Inventory/Inventory/Features/Items/ListItemMovements/{Endpoint,Handler,Request}.cs` | C | `GET /items/{id}/movements` with the windowed raw SQL | R2, D1, D2, D4, D5 | 02 |
| `src/Modules/Inventory/Inventory/Features/Items/ItemEndpoints.cs` | M | `items.MapListItemMovements()` | R2 | 02 |
| `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs` | M | `AddScoped<ListItemMovementsHandler>()` | R2 | 02 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | M | T5 probe for `GET /api/items/{id:guid}/movements` | R6 | 02 |
| `web/src/app/items/items.service.ts` (+ `.spec.ts`) | M | `ItemBalance`, `ItemDetail`, `ItemMovement`; `get` returns `ItemDetail`; `movements(id, page, pageSize)` | R3 | 03 |
| `web/src/app/items/item-detail.{ts,html,spec.ts}` | C | The item detail screen | R3, R5 | 04 |
| `web/src/app/app.routes.ts` | M | `items/:id` route (authGuard) | R3 | 04 |
| `web/src/app/stock/stock.{ts,html,spec.ts}` | M | Row click → `/items/:itemId`, ignoring the pinned row | R4 | 05 |

## Blast radius
**Risk:** medium. Read-only endpoints and screens; no schema, posting or Domain change. The one sharp edge is the raw SQL in prompt 02, where tenant isolation comes from D5 (EF item check, explicit `TenantId` predicates, RLS) instead of the EF named filter.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `GetItemHandler` return type | Direct | `ItemResponse` → `ItemDetailResponse` | No: the endpoint lambda returns whatever the handler returns; nothing else calls the handler (grep) | — |
| `ItemsTests.Get_returns_the_item_with_its_version` (`:104-120`) | Direct (test) | JSON gains `balances` | No: asserts named fields only | Run `ItemsTests` in prompt 01 |
| `ItemResponse` (list, create, update, deactivate) | Direct | Unchanged | No | D6 keeps it |
| `ItemEndpoints` `/items` group | Direct | New `/{id:guid}/movements` route | No: distinct path; `PUT`/`POST …/deactivate` unaffected | T5 route guard lists all routes |
| T5 route guard (`TenantIsolationTests:629-634`) | Non-graph (test) | Every `/api` route needs a probe | **Yes**, until probed | Prompt 02 adds `[ProbesRoute("GET /api/items/{id:guid}/movements")]` |
| T10 `No_request_type_carries_a_date` | Non-graph (test, review constraint) | `ListItemMovementsRequest(int? Page, int? PageSize)`; `ItemMovementRow` has `PostedAt` | No if named right: T10 scans `*Request`/`*Line` types | Prompt 02: the row type ends in `Row`, never `Line`/`Request` |
| RLS / `security.TenantPolicy`, EF named filter | Non-graph (security) | Raw `SqlQuery` on an unmapped type skips the EF filter | No: D5's EF item check + explicit `TenantId` predicates, RLS underneath | T5 probe (foreign id `404`); `Each_tenant…` style check that A's history holds none of B's rows |
| IMS-6 `FORCESEEK` master-data reads / "add no index" | Known risk | Reads on `StockMovements`, `Locations` | No: no index or statistic added | — |
| Posting lock path (§6 steps 3–9) | Transitive | Concurrent reads of `StockMovements`/`StockBalances` | No: RCSI snapshot reads take no shared locks | — |
| Hidden coupling: ProblemDetails slugs ↔ `PROBLEM_MESSAGES` | Hidden coupling | — | No: only `not-found` and `validation`, both mapped | — |
| `ItemsService.get` (web) | Direct | Return type `Item` → `ItemDetail` (superset) | No: no caller outside its spec (grep) | Prompt 03 updates the spec's flush body |
| `Stock` screen | Direct | New `(rowClicked)` | No: the pinned totals row has no `itemId` | Prompt 05 guards and tests it |
| `app.routes.ts` order | Direct | `items/:id` after `items` | No: distinct paths; `**` stays last | — |
| Angular budgets (`angular.json`, component styles warn at 4 kB) | Non-graph | New component styles | No if small | Prompt 04 keeps styles inline and short |
| S9 reversals | Downstream | Reversal rows appear in history automatically | No | Downstream note |
| S10 negative stock | Downstream | Revaluation rows (qty 0); negative running values; red | No | Downstream note |
| S11 ledger health | Downstream | The window is INV-1 over a prefix | No | Downstream note: health can compare per-location final rows |
| S12/T14 | Downstream | `/items/:id` route and labels | No | Downstream note |

**Known risks in the touched area:**
- **Composable raw SQL** (IMS-6): EF wraps `SqlQuery` in a derived table. `ORDER BY`, a CTE or a trailing `;` in the SQL fails. D4 keeps all ordering in LINQ.
- **The Testcontainers start-up flake** (IMS-6): use `dotnet test -- --max-parallel-test-modules 1` if it shows.
- **Restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1`** on this machine (IMS-7).
- **Decimal scale** (IMS-8): compare JSON numbers with `GetValue<decimal>()`, never as text. `SUM` over `decimal(19,4)` returns `decimal(38,4)`, which still maps to C# `decimal`.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-12 | S8 Item history | Added | 01, 02, 03, 04, 05 | IMS-10 | no (code-guardian recommended for 02's raw SQL, D8) | See below |

Acceptance criteria for IMS-12:
- **R1:** After V1–V5 (A: 10 @ 100, 10 @ 120, issue 15, 5 @ 130; transfer 4 A→B), `GET /api/items/{id}` returns the item's existing fields plus `balances` `[A: 6 / 720.0000 / averageCost 120.0000 / lastUnitCost 120.0000, B: 4 / 480.0000 / 120.0000 / 120.0000]` ordered by code (LUC after an inflow is `Round4(StockValue / OnHand)`, §7: A's V4 leaves 1200 / 10; B's transfer-in leaves 480 / 4), with no SUP/CUS/ADJ. A never-stocked item has `balances: []`. An item issued to 0 shows `onHand` 0, `averageCost` null. Staff can read.
- **R2:** After V1–V6, `GET /api/items/{id}/movements?pageSize=100` returns 7 rows, newest first: `B In +6 +750 → 10 / 1230`, `B In +4 +480 → 4 / 480`, `A Out −4 −480 → 6 / 720`, `A In +5 +650 → 10 / 1200`, `A Out −15 −1650 → 5 / 550`, `A In +10 +1200 → 20 / 2200`, `A In +10 +1000 → 10 / 1000`, with counterparts `SUP`, `A`, `B`, `SUP`, `CUS`, `SUP`, `SUP` and the posting's `documentId`/`documentType`. `pageSize=2&page=2` returns rows 3–4 with the same running values and `total` 7. On a separate item, after receiving 7 @ 56 at A, a `LedgerRows` revaluation movement (qty 0, cost 18, A → ADJ, `LineNo` 1001) is the newest row: `Out`, `quantityChange` 0, `valueChange` −18, running 7 / 374. An item with no movements gives `total` 0. Unknown id → `404 not-found`; tenant B's item id → `404` (T5); `pageSize=101` → `400` on `pageSize`. Staff can read.
- **R3:** `/items/:id` shows SKU · name, unit, an "Inactive" note when inactive, the balance table (location, qty, avg cost, value, ₱ 2 dp), and the history table (posted, type label, location, from/to counterpart, qty change with sign, value change, running qty, running value, posted by) with a link per row to `/documents/<documentId>` and a paginator that reloads the history. A `404` shows the problem message and a link back to `/stock`.
- **R4:** Clicking a `/stock` row navigates to `['/items', itemId]`; the totals row does nothing.
- **R5:** Quantity changes read `+6` / `-4` and value changes `+₱750.00` / `-₱480.00`; money `₱1,230.00`; dates `30 Oct 2026, 2:14 PM`; posted-by System / You / Another user; the tables collapse to labelled cards below 600 px.
- **R6:** `ItemHistoryTests` (V1–V6, paging, revaluation row, empty, 404, Staff, balances) and the T5 probe pass; `items.service.spec`, `item-detail.spec` and `stock.spec` cover R3–R5.
- **R7:** Every `## Commands` line and CI `build` and `test` are green.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Balances in `GET /items/{id}` | IMS-12 | sonnet | medium | — | A | Features/Items/{ItemDetailResponse.cs, GetItem/Handler.cs}, ItemHistoryTests.cs |
| 02 | `GET /items/{id}/movements` with running balance | IMS-12 | sonnet | high | 01 | B | Features/Items/{ItemMovementResponse.cs, ListItemMovements/**, ItemEndpoints.cs}, InventoryModuleExtensions.cs, ItemHistoryTests.cs, TenantIsolationTests.cs |
| 03 | Web items service: detail and movements | IMS-12 | haiku | low | 02 | C | items/items.service.ts (+ spec) |
| 04 | Item detail screen and route | IMS-12 | sonnet | medium | 03 | D | items/item-detail.{ts,html,spec.ts}, app.routes.ts |
| 05 | Stock row click | IMS-12 | haiku | low | 04 | E | stock/stock.{ts,html,spec.ts} |

No prompt needs opus: no high-stakes glob, shared code or concurrency is touched. 02 runs on sonnet at high effort because the SQL, its tenant predicates and the composition rules are spelled out in the prompt.

Every prompt runs in one working tree; 01 and 02 share `ItemHistoryTests.cs` and one .NET build, and 03–05 share `web/` (one build, one lockfile), so the groups run in order.

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if the Testcontainers start-up flake shows: dotnet test -- --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false
# R1, R2, R6 (API): the new tests, the T5 guard, T10 and the unchanged item reads
cd .. && dotnet test --project tests/Inventory.IntegrationTests --filter-class '*ItemHistoryTests' --filter-class '*TenantIsolationTests' --filter-class '*PostingClockTests' --filter-class '*ItemsTests'
# R3–R5 (web): items.service.spec, item-detail.spec, stock.spec run in the full Vitest suite above
# Manual smoke (optional, needs the seed-password secret): dotnet run --project src/AppHost; sign in as the demo Staff user;
# click a /stock row -> /items/:id shows the opening-stock balance and one "Received" history row per location with "System";
# its link opens /documents/:id; at 375 px both tables render as labelled cards.
```

## Rollback
Revert the PR. No schema change and no data written, so nothing to roll back in the database. `GET /items/{id}` drops `balances` again; the web client from the reverted build doesn't read it.

## Downstream (for TASKS.md after merge)
- **S9:** a reversal shows in item history as type Reversal with the swapped direction and the original's cost; the running values return to what they were before the original. T8 can assert that via `GET /items/{id}/movements`. A Reverse button on `/items/:id` is not required.
- **S10:** revaluation movements (qty 0) appear as `In`/`Out` rows with `quantityChange` 0; T15 can assert N4's `A Out 0 −18`. Negative `runningOnHand`/`runningValue` and negative `balances[].onHand` go red on `/items/:id` as on `/stock`.
- **S11:** the newest history row per location equals its balance's `OnHand`/`StockValue` (INV-1 over a prefix). Health's INV-1 can reuse the same `±Quantity`/`±TotalCost` unpivot.
- **S12/T14:** `/stock` row click opens `/items/:id`; history type labels come from `documentTypeLabel`.

## Log
- 2026-11-15: planned
