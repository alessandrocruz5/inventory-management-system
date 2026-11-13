---
type: plan
kind: feature
status: audited
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-13
risk: medium
prompts: 10
graph_built: 2026-11-13T00:31:55+08:00
tags: [plan]
---
# S7 Stock screens (IMS-11)

## Request
> IMS-11

This plans the existing unit `IMS-11 — S7 Stock screens · Added · spec: §12 S7 · depends: IMS-10 · model: sonnet` (TASKS.md, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S7: *"`GET /stock` (with totals), `GET /documents`, `GET /documents/{id}`; stock on hand, post-stock form, documents list and detail. Tests: T13."*

Spec §9 rows: *"`GET /documents` · any · Filter by type and posted-at range"*, *"`GET /documents/{id}` · any · Lines, movement costs, reversal link either way"*, *"`GET /stock` · any · Balances by item × physical location with average cost and value; filter by item or location; totals for the whole filter, not just the page"*.

Spec §10 rows `/stock`, `/documents/new/:type`, `/documents`, `/documents/:id`, the Labels, the Post-stock form and the Conventions. Spec §11 T13: *"Line editor, money and quantity formatting, idempotency key reuse · Vitest · Frontend logic"*.

It also covers the S7 items that earlier units locked:
- IMS-5 (S4): reuse `web/src/app/core/api/paged.ts`, `problemFieldErrors` and `shared/confirm-dialog.ts`; AG Grid 36.2.0 is exact-pinned.
- IMS-6 (S5a): *"S7: decide on `IX StockDocuments (TenantId, PostedAt)` for the `GET /documents` date filter"*. Decided: add it (D2).
- IMS-8 (S5c): a `201` and a replay format the same decimal differently (`1` vs `1.0000`), so compare numbers, not text (T13).
- IMS-9 (S5d): show `PostedBy` `Guid.Empty` as "System".
- IMS-10 (S6, plan Downstream): the post-stock form sends the D3 bodies. Adjustments send `reasonCode` (one of the five `AdjustmentReason` names, exact case) and a signed `quantityDelta`, with `unitCost` only on positive lines. A `400` on `lines[i].unitCost` can arrive after submit (A7). An adjustment's out-of-range error arrives as `lines[i].quantityDelta`, so map that key to the quantity field.

| # | Requirement |
|---|---|
| R1 | `GET /api/stock` returns balances by item × **physical** location: item, location, `onHand`, `stockValue`, average cost (`stockValue / onHand` rounded AwayFromZero to 4 dp; null when `onHand <= 0`) and `lastUnitCost`. It filters by `itemId` and/or `locationId`, is paged (§9), and carries `totals.stockValue` for the whole filter, not just the page. |
| R2 | `GET /api/documents` lists documents newest first, paged, filtered by `type` and by a posted-at range. |
| R3 | `GET /api/documents/{id}` returns the document with its lines (each movement with item, from/to location, quantity, unit cost and total cost) and the reversal link either way (`reversesDocumentId`, `reversedByDocumentId`). An unknown or foreign id is `404 not-found`. |
| R4 | `/stock` is the landing page: an AG Grid of item, location, qty, unit, avg cost and value, a location filter, and a totals row from the API's totals. |
| R5 | `/documents/new/:type` is one form component for receipt, issue, transfer and adjustment (§10 Post-stock form). It has header fields by type and a line editor with item autocomplete, quantity, unit cost where it applies, on-hand beside each line, and a warning before submit. It follows the Idempotency-Key lifecycle, disables Submit while in flight, highlights the line on `insufficient-stock`, and links to the original on `idempotency-key-reused`. Adjustments are Admin-only. |
| R6 | `/documents` lists documents with type and date filters. `/documents/:id` shows the lines, the costs and the reversal link either way. |
| R7 | The UI conventions hold (§10): money as ₱1,234.50, quantities without trailing zeros, dates in Asia/Manila (`30 Oct 2026, 2:14 PM`), the plain verbs in the navigation and form titles, buttons hidden for roles that can't use them, and forms usable at 375 px. |
| R8 | Tests: T13 in Vitest (line editor, money and quantity formatting, idempotency key reuse); T5 probes for the three new routes; integration tests for R1–R3; T10 still guards every request type. |
| R9 | Every `## Commands` line and CI `build` and `test` are green. The code-guardian review has no open Blocker (the unit touches `**/Migrations/**` and `Persistence/**`). |

## Context used
- Note: `CLAUDE.md` (Conventions, Review constraints, High-stakes paths, Hidden coupling): decimal and AwayFromZero rounding; "no request carries a date"; ProblemDetails slugs ↔ `PROBLEM_MESSAGES`; RequestHash ↔ client.
- Note: spec §3, §5 (the `StockDocuments`, `StockMovements` and `StockBalances` sketch; average cost is derived, never stored), §9 (the routes, the request bodies, the problem types), §10 (screens, labels, the post-stock form, conventions), §11 (T5, T10, T13), §12 S7–S10 (what S8, S9 and S10 own).
- Note: spec audit `Inventory System — Sprint 1 Spec.audit.md:26` (totals for the whole filter, server-side, decimal), `:27(b)` (assigns `GET /stock`, `GET /documents`, `GET /documents/{id}` to S7), `:35` (the `IX (TenantId, PostedAt)` NOTE).
- Note: TASKS.md Downstream for IMS-4 (T5 probes for every route; route raw text with a trailing slash), IMS-5 (S7 reuse list; the `Features/<Area>/<Endpoint>/` layout; `GET /locations` returns inactive rows too), IMS-6 (the S7 index decision; `FORCESEEK` reads on Items, Locations, Balances and Movements; the Testcontainers flake), IMS-8 (decimal-scale nit), IMS-9 (show "System"), plus the IMS-10 plan's `## Downstream` `:265-269` (form bodies, the `quantityDelta` key).
- Note: S5c plan `:117` and `:206` (the server canonicalizes the hash, so the client only has to resend the same values) and `:140` (the posting `Location` header points at `GET /documents/{id}`, which S7 adds). S5d plan `:63`, `:213` (`PostedBy = Guid.Empty`, `InventorySeeder.SystemPrincipal`).
- Graph: `graphify update .` (no topology change since the 00:31 build). `graphify affected "InventoryModuleExtensions" --depth 2` found no nodes, and `InventoryDbContext` has no unique node. The C# graph is too thin, so the blast radius below comes from grep (workflow §4).
- Code: `Features/Items/ListItems/{Endpoint,Handler,Request}.cs` (the list pattern: `[AsParameters]`, `Paging.Resolve`, `Problems.Validation`, `ToPageAsync`), `Features/Items/GetItem/Handler.cs:12-21` (the get/404 pattern), `Features/Items/ItemEndpoints.cs:15-24` (group mapping), `Features/Paging.cs:6-37`, `Features/Problems.cs:15-19`.
- Code: `Features/Posting/DocumentEndpoints.cs:14-22` (the `/documents` group; it sits in a high-stakes glob, so S7 maps its own group instead), `DocumentResponse.cs:34-59` (the movement fields), `DocumentPoster.cs:157` (`Created($"/api/documents/{id}")`), `PostingValidation.cs:45` (`MaxLines = 100`), `:229-248` (duplicate item → `lines[i].itemId`), `:305-330` (exact-name enum parsing, which `type` copies), `Persistence/PostingEngine.cs:28-60` (the replay read), `Domain/CostingPolicy.cs:7` (`Round4`).
- Code: `Persistence/InventoryDbContext.cs:78-104` (the `StockDocuments` config and its indexes), `Persistence/Migrations/` (latest `20261105155422_Ledger`), `InventoryModuleExtensions.cs:46-69` (DI, mapping).
- Code: `src/Api/Program.cs:15,47` (`AddProblemDetails`, `UseStatusCodePages`: binding failures are plain `400` problems, as for `bool? active` on `/items` today).
- Tests: `TenantIsolationTests.cs:24-32` (the `Exempt` map), `:127-168` (the list and 404 probe pattern), `:521-545` (the route guard), `:576` (`ProbesRouteAttribute`). `PostingClockTests.cs:68-87` (T10 scans every `*Request`/`*Line` type under `Inventory.Features` for date properties), `:22-46` (the `FakeTimeProvider` pattern). `PostingTests.cs` helpers (`PostDocumentAsync`, `Receipt`, `Issue`, `Line`, `IssueLine`, `NewKey`, `ReceiveAsync`, `NewLocationAsync`, `NewItemAsync`, `AddUserAsync`, `AssertValidationAsync`). `LedgerRows.cs:64-78` (`AddDocumentAsync(type: "Reversal", reverses: …)` inserts a reversal row before S9 exists). `RowLevelSecurityTests.cs:200-223` (unique keys lead with TenantId; a non-unique index is outside the check). `MigrationRollbackTests.cs:39-63` (migrates down to `Initial` and up again).
- Web: `app.routes.ts:4-22`, `app.html:1-14`, `app.scss` (the toolbar already wraps), `app.spec.ts:14-58` (its `AuthService` mock has only `user` and `logout`), `home/home.ts` (referenced only by `app.routes.ts:9`), `login/login.ts:48` (navigates to `/` after sign-in), `items/items.ts` + `items.html` (the AG Grid + paginator pattern), `items/items.spec.ts:8-45` (the component test pattern), `items/items.service.ts` + spec (the service pattern), `items/item-dialog.ts:42-95` (typed forms and `problemFieldErrors`), `locations/locations.service.ts:6-25`, `core/problems/problem-messages.ts` (every slug S7 meets already exists), `core/auth/auth.guard.ts`, `auth.service.ts:18-27` (`isAdmin`, `ensureLoaded`), `shared/confirm-dialog.ts`, `angular.json:37-47` (budgets: component styles warn at 4 kB).

## Decisions
- **D1 · T10 gets a named exemption for the read filter (user, 2026-11-13).** `ListDocumentsRequest` binds `DateTimeOffset? PostedFrom` and `DateTimeOffset? PostedTo`. T10 (`No_request_type_carries_a_date`) keeps scanning every request and line type, but allows exactly these two properties through a map with a reason each ("read filter; never stored"). It also asserts that every exempt property still exists and is still seen by the scan, so a stale entry fails. This is how we read the review constraint "PostedAt comes only from TimeProvider; no request carries a date": no request carries a date that is *stored*. Prompt 03 also rewords the `CLAUDE.md` review constraint to "no write request carries a date (T10 allowlists read filters, IMS-11 D1)", so code-guardian reads the decision instead of flagging it (audit FIX 3, user-approved). Spec §6 and §11 stay unchanged, and the PR description cites D1. Rejected: narrowing T10 to `Features.Posting` (a future dated write route outside it would slip through), and dropping the date filter (a spec deviation).
- **D2 · Add `IX_StockDocuments_PostedAt (TenantId, PostedAt)` (user, 2026-11-13).** One migration, `DocumentsPostedAtIndex`, plus one `HasIndex` line. SQL Server appends the clustered key `Id` to a non-unique index's key, so `ORDER BY PostedAt DESC, Id DESC` within a tenant is an ordered (backward) range scan, with no sort. `CREATE INDEX` is not an `UPDATE`, so the append-only triggers, the `DENY`s and RLS are unaffected. Because of this, IMS-11 is high-stakes (`**/Migrations/**`, `Persistence/**`): code-guardian is required and prompt 01 runs on opus.
- **D3 · "Posted by" is System / You / Another user (user, 2026-11-13).** The APIs return `postedBy` (a Guid). The UI shows `00000000-0000-0000-0000-000000000000` as "System" (S5d), the signed-in user's id as "You" (compared case-insensitively), and anything else as "Another user". No Platform contract is added.
- **D4 · No row click on `/stock` in S7 (user, 2026-11-13).** `/items/:id` is S8's. S8 adds the row click with the item detail screen (Downstream).
- **D5 · S7 maps its own route groups, not `Features/Posting/DocumentEndpoints.cs`.** That file is under the high-stakes glob `Features/{Posting,…}/**`. The read endpoints go in `Features/Documents/` (a second `MapGroup("/documents")`; ASP.NET Core merges same-prefix groups) and `Features/Stock/`, registered in `InventoryModuleExtensions`. Route raw texts: `GET /api/stock/`, `GET /api/documents/`, `GET /api/documents/{id:guid}` (IMS-5: a `""` route has a trailing slash).
- **D6 · Response shapes (the web services' contract).** JSON is camelCase; decimals are numbers.
  - `GET /api/stock?itemId&locationId&page&pageSize` → `{ items: StockRow[], page, pageSize, total, totals: { stockValue } }`. `StockRow` = `{ itemId, sku, itemName, baseUnit, locationId, locationCode, locationName, onHand, stockValue, averageCost: number|null, lastUnitCost: number|null }`, ordered by `sku`, then `locationCode`. Only `!IsVirtual` balances appear, zero rows included. Average cost = `CostingPolicy.Round4(StockValue / OnHand)` when `OnHand > 0`, else null, computed in C# after the page is read (never in SQL, whose decimal division scale differs). `totals.stockValue` = `SUM(StockValue)` over the same filter, in a second statement (like `ToPageAsync`'s count). Quantity isn't totalled, because units differ across items.
  - `GET /api/documents?type&postedFrom&postedTo&page&pageSize` → `Paged<DocumentSummary>`. `DocumentSummary` = `{ id, type, reasonCode, reference, postedAt, postedBy, lineCount, reversesDocumentId, reversedByDocumentId }`, ordered `PostedAt DESC, Id DESC`. `lineCount` counts movements with `LineNo < 1000` (a revaluation is `1000 + n`, §5). `type` must equal a `StockDocumentType` name exactly (ordinal; copy `PostingValidation.cs:305-330`), else `400` on `type`. The range is half-open, `postedFrom <= PostedAt < postedTo`; `postedTo <= postedFrom` is `400` on `postedTo`.
  - `GET /api/documents/{id}` → `{ id, type, reasonCode, reference, notes, postedAt, postedBy, reversesDocumentId, reversedByDocumentId, lines: DocumentLine[] }`. `DocumentLine` = `{ movementId, lineNo, itemId, sku, itemName, baseUnit, fromLocationId, fromLocationCode, fromLocationName, toLocationId, toLocationCode, toLocationName, quantity, unitCost: number|null, totalCost, reversesMovementId }`, in `LineNo` order, every movement included. `unitCost` = `Round4(TotalCost / Quantity)` when `Quantity > 0`, else null.
  - A filter id from another tenant (or an unknown one) on `/stock` gives an empty page and `totals.stockValue` 0, never `400`. Filters aren't body ids, and the tenant filter and RLS already hide the rows (T5 "zero foreign rows in any list").
- **D7 · The client's numbers.** Inputs are JS numbers, so the line validators require a value `> 0` (quantity) or `>= 0` (unit cost), at most 4 decimal places, and at most `999,999,999.9999`, the server's `PostingValidation.MaxAmount` (`PostingValidation.cs:40`; audit FIX 1). Within that range (13 significant digits) a double round-trips every 4-dp value, so `JSON.stringify` sends what the user typed. The server canonicalizes the hash (S5c `:117`), so a retry only has to resend the same form values. Display rounding uses Angular's `formatNumber`/`formatCurrency`, which round on the decimal digits, half away from zero.
- **D8 · Dates over the wire are UTC `Z` instants.** The documents screen turns Manila calendar days into `postedFrom = <from day> 00:00 +08:00` and `postedTo = <to day + 1> 00:00 +08:00`, sent as `toISOString()` (`…Z`). Never send `+08:00` in a query string: Angular's `HttpParams` codec leaves `+` unencoded, so the server would read a space. The Philippines has no DST, so `+0800` is fixed for display (`formatDate(…, 'd MMM y, h:mm a', 'en-US', '+0800')`).
- **D9 · Adjustment lines use an Add/Remove choice plus a positive quantity.** The client sends `quantityDelta = ±quantity`, and `unitCost` only on Add lines (null on Remove). This is plainer for a non-technical owner than a signed number, and it makes "unit cost for positive adjustments" visible. The server's `lines[i].quantityDelta` errors map to the quantity field.
- **D10 · The post-stock route resets on a type change.** Angular reuses the component when only `:type` changes, so the component subscribes to `paramMap`. Each emission resets the header, the lines and the errors, and makes a new Idempotency-Key ("the user starts a new form", §10). The key is otherwise generated once, at form open, and renewed only after a success (the component navigates away) or an `idempotency-key-reused`.
- **D11 · Role gating.** `postStockGuard` (functional, with `authGuard`) sends an unknown `:type`, or `adjustment` for a non-Admin, to `/`. The "Correct stock" link is shown only to Admin. The API still enforces every role (§10).
- **D12 · One unit, IMS-11.** It is the existing ticket, and §12 makes it one PR. Ten prompts, ordered migration → API → web.

**Assumptions:**
- The stock grid shows zero-balance rows (an item issued down to 0 stays visible, which tells the owner it's out). Inactive items or locations can only hold zero rows, because deactivation requires zero stock. No "hide zero" toggle in S7.
- The location pickers (stock filter, post-stock header) read `GET /api/locations?page=1&pageSize=100` and keep `!isSystem && isActive` rows. A tenant with more than 100 locations is out of scope for a small shop.
- Item autocomplete reads `GET /api/items?search=…&active=true&page=1&pageSize=10`, debounced 300 ms.
- On-hand beside a line is `GET /api/stock?itemId&locationId&page=1&pageSize=1` → `items[0]?.onHand ?? 0`, at the line's source location (receipt, issue and adjustment: `locationId`; transfer: `fromLocationId`). It's re-read when the item or that location changes. A stale value only affects the client-side warning; the server decides.
- "Warn before submit if a line exceeds it" applies to outflow lines (issue, transfer, adjustment Remove). It opens `ConfirmDialog`, and the user may still post (the server returns `422` unless S10's toggle allows negatives).
- Page rows and `totals` are two statements under RCSI, so a posting that commits between them can make the totals differ from the rows by that posting. This is a display screen; S11's health check is the proof of correctness.
- A malformed `postedFrom`/`postedTo`/`itemId` query value is a framework binding `400` (ProblemDetails without our `validation` slug), the same as `?active=maybe` on `/items` today.
- Navigation stays as plain links in the existing wrapping toolbar (`app.scss` already has `flex-wrap: wrap`): Stock, Receive stock, Use or sell stock, Move stock, Correct stock (Admin), Documents, Items, Locations. No menu component.
- Document types display as Received / Used or sold / Moved / Corrected / Reversal, and reasons as Damaged / Lost / Found / Count correction / Other. The API keeps the technical names (§10 Labels).

**Non-goals:**
- Row click to `/items/:id`, the item detail screen, and `GET /items/{id}` balances (S8).
- The Reverse button and `POST /documents/{id}/reversal` (S9). S7's detail shows the reversal links, which S9's button will create.
- Negative balances in red, and the allow-negative switch (S10, per §12; no balance can be negative before then).
- Ledger health (S11). Playwright/T14 (S12).
- Any new ProblemDetails slug, Platform contract, package, or column.
- Changing `Features/Posting/**`, the posting engine, or the canonical request forms.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs` | M | `HasIndex(TenantId, PostedAt)` named `IX_StockDocuments_PostedAt` | R2, D2 | 01 |
| `src/Modules/Inventory/Inventory/Persistence/Migrations/<ts>_DocumentsPostedAtIndex.cs` (+ `.Designer.cs`) | C | Generated `CreateIndex`/`DropIndex` | R2, D2 | 01 |
| `src/Modules/Inventory/Inventory/Persistence/Migrations/InventoryDbContextModelSnapshot.cs` | M | Generated | D2 | 01 |
| `tests/Inventory.IntegrationTests/LedgerSchemaTests.cs` | M | The index exists, keyed `(TenantId, PostedAt)` | R8, D2 | 01 |
| `src/Modules/Inventory/Inventory/Features/Stock/StockEndpoints.cs` | C | `/stock` group | R1 | 02 |
| `src/Modules/Inventory/Inventory/Features/Stock/ListStock/{Endpoint,Handler,Request}.cs` | C | `GET /stock` | R1 | 02 |
| `src/Modules/Inventory/Inventory/Features/Stock/StockResponse.cs` | C | `StockRowResponse`, `StockTotals`, `StockPageResponse` | R1, D6 | 02 |
| `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs` | M | Register the handlers and map the groups | R1–R3 | 02, 03, 04 |
| `tests/Inventory.IntegrationTests/StockTests.cs` | C | R1 behaviour | R1, R8 | 02 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | M | T5 probes for the three routes | R8 | 02, 03, 04 |
| `src/Modules/Inventory/Inventory/Features/Documents/DocumentQueryEndpoints.cs` | C | The read-side `/documents` group | R2, R3, D5 | 03 (04 extends) |
| `src/Modules/Inventory/Inventory/Features/Documents/ListDocuments/{Endpoint,Handler,Request}.cs` | C | `GET /documents` | R2 | 03 |
| `src/Modules/Inventory/Inventory/Features/Documents/DocumentSummaryResponse.cs` | C | List row | R2, D6 | 03 |
| `tests/Inventory.IntegrationTests/PostingClockTests.cs` | M | T10 named exemption | R8, D1 | 03 |
| `CLAUDE.md` | M | Review constraint reworded to "no write request carries a date" | D1, R9 | 03 |
| `tests/Inventory.IntegrationTests/DocumentQueryTests.cs` | C | R2 and R3 behaviour | R2, R3, R8 | 03 (04 extends) |
| `src/Modules/Inventory/Inventory/Features/Documents/GetDocument/{Endpoint,Handler}.cs` | C | `GET /documents/{id}` | R3 | 04 |
| `src/Modules/Inventory/Inventory/Features/Documents/DocumentDetailResponse.cs` | C | Detail and line records | R3, D6 | 04 |
| `web/src/app/shared/format.ts` (+ `.spec.ts`) | C | Money, quantity, Manila date, day range, type, reason and posted-by labels | R7, R8 | 05 |
| `web/src/app/stock/stock.service.ts` (+ `.spec.ts`) | C | `/api/stock` client | R4, R5 | 05 |
| `web/src/app/documents/documents.service.ts` (+ `.spec.ts`) | C | `/api/documents` list, get and the four posts with `Idempotency-Key` | R5, R6 | 05 |
| `web/src/app/stock/stock.{ts,html,scss,spec.ts}` | C | Stock on hand screen | R4, R7 | 06 |
| `web/src/app/documents/posting-form.ts` (+ `.spec.ts`) | C | Pure form logic: bodies, key holder, error mapping, outflow warnings, amount validator (T13) | R5, R8 | 07 |
| `web/src/app/documents/post-stock.{ts,html,scss,spec.ts}` | C | The post-stock form | R5, R7 | 08 |
| `web/src/app/documents/post-stock.guard.ts` (+ `.spec.ts`) | C | `:type` and Admin gate | R5, D11 | 08 |
| `web/src/app/documents/documents.{ts,html,spec.ts}` | C | Documents list | R6, R7 | 09 |
| `web/src/app/documents/document-detail.{ts,html,spec.ts}` | C | Document detail | R6, R7 | 09 |
| `web/src/app/app.routes.ts` | M | `''` → redirect to `stock`; `stock`; `documents`, `documents/new/:type`, `documents/:id` | R4–R6 | 10 |
| `web/src/app/app.html` | M | Navigation links (plain verbs) | R7 | 10 |
| `web/src/app/app.spec.ts` | M | `isAdmin` in the mock; new links; "Correct stock" Admin-only | R7, R8 | 10 |
| `web/src/app/home/home.ts` | D | Replaced by the Stock landing page; only `app.routes.ts` referenced it | R4 | 10 |

## Blast radius
**Risk:** medium. It adds one index migration on a ledger table (high-stakes glob) plus read-only endpoints and new screens. Nothing writes, and no posting code changes.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `InventoryDbContext` model + snapshot | Direct | New index | No: additive | Prompt 01 checks that the generated migration has only `CreateIndex`/`DropIndex` |
| Posting inserts into `StockDocuments` | Transitive | One more index to maintain | No: one insert per document | — |
| `TR_StockDocuments_AppendOnly`, `DENY UPDATE/DELETE`, RLS predicates | Non-graph | `CREATE INDEX` on the table | No: DDL by `migrate` (sa); no column change, so the schema-bound policy is untouched | `LedgerImmutabilityTests`, `RowLevelSecurityTests` stay green |
| `MigrationRollbackTests` | Non-graph (test) | Migrates down to `Initial` and up | No: `Down` drops the index | Run it in prompt 01 |
| `RowLevelSecurityTests.Tenant_tables_lead_every_key_with_TenantId` | Non-graph (test) | Checks PK/unique only | No: non-unique, and it leads with TenantId anyway | — |
| IMS-6 `FORCESEEK` master-data reads (8622) | Known risk | Indexes on Items/Locations/Balances/Movements | No: the new index is on `StockDocuments` | — |
| `InventoryModuleExtensions.AddInventoryModule` / `MapInventoryEndpoints` | Direct | Additive registrations | No | — |
| T5 route guard (`TenantIsolationTests:521`) | Non-graph (test) | Every `/api` route needs a probe | **Yes**, until probed | Each route's prompt adds its `[ProbesRoute]` test (02, 03, 04) |
| T10 `No_request_type_carries_a_date` | Non-graph (test, review constraint) | `ListDocumentsRequest.PostedFrom/PostedTo` | **Yes** | D1 named exemption in prompt 03 |
| Posting `Location: /api/documents/{id}` header | Non-graph (HTTP contract) | Forward reference from S5c | No: it now resolves | Prompt 04 tests following it |
| `Features/Posting/DocumentEndpoints` `/documents` group | Direct (route table) | Second group, same prefix | No: distinct methods/paths; ASP.NET merges | T5 route guard lists all routes |
| ProblemDetails slugs ↔ `PROBLEM_MESSAGES` | Hidden coupling | — | No: no new slug | — |
| RequestHash canonical form ↔ client | Hidden coupling | The form's bodies | No: the server canonicalizes; the form resends the same values (D7) | Prompt 07 tests the exact bodies per type |
| `app.spec.ts` | Direct (test) | `AuthService` mock lacks `isAdmin`; `app.html` will call it | **Yes** | Prompt 10 adds `isAdmin` to the mock |
| `home/home.ts` | Direct | `''` route retarget | No other importer (grep) | Deleted in 10 |
| `login.ts:48` → `/` | Direct | Lands on Stock now | No: `''` redirects to `/stock` (§10 landing page) | — |
| Server `PostingValidation.MaxAmount` (`999,999,999.9999`) | Non-graph (HTTP contract) | The client amount validator | Was **yes** (client allowed up to 1e11; audit FIX 1) | Prompt 07 mirrors `MaxAmount` |
| code-guardian gate (R9) ↔ `## Review constraints` | Non-graph (process) | The literal "no request carries a date" | Was **yes** (a Blocker on D1; audit FIX 3) | Prompt 03 rewords the constraint line |
| S12/T14 deep links to `/stock` (spec §10) | Downstream | The route table | Was **yes** (no `/stock` path; audit FIX 2) | Prompt 10 adds `stock` and the `''` redirect |
| Angular budgets (`angular.json:37-47`) | Non-graph | New lazy routes; component styles | Component style warns at 4 kB | Keep each `.scss` small (prompt rule) |
| S8 item history | Downstream | Stock row click; detail links | No | Downstream note (D4) |
| S9 reversals | Downstream | `reversedByDocumentId`/`reversesDocumentId` on detail; the Reverse button goes on `document-detail` | No | Downstream note |
| S10 negative stock | Downstream | Red negatives in the stock grid (`cellClassRules` on `onHand < 0`); `AllowNegative` makes the outflow warning advisory | No | Downstream note |
| S12/T14 | Downstream | Playwright drives these screens and labels | No | Downstream note: the labels and routes are fixed here |

**Known risks in the touched area:**
- **The Testcontainers start-up flake** (IMS-6). Use `dotnet test -- --max-parallel-test-modules 1` if it shows.
- **Restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1`** on this machine (IMS-7).
- **The decimal scale differs between a 201 and a replay** (IMS-8 nit): `1` vs `1.0000`. Web tests compare numbers or formatted strings, never raw JSON text.
- **JS number precision** for large amounts. D7 bounds the inputs.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-11 | S7 Stock screens | Added | 01, 02, 03, 04, 05, 06, 07, 08, 09, 10 | IMS-10 | yes: `**/Migrations/**` and `Persistence/**` (01). code-guardian required | See below |

Acceptance criteria for IMS-11:
- **R1:** At location A, receive 10 @ 100.0000 then 3 @ 33.3333. `GET /api/stock?locationId=A` returns one row with `onHand` 13, `stockValue` 1099.9999 and `averageCost` 84.6154. An item issued to 0 shows `onHand` 0, `stockValue` 0 and `averageCost` null. SUP/CUS/ADJ never appear. With 3 rows and `pageSize=1`, `total` is 3 and `totals.stockValue` is the sum of all 3. The `itemId` and `locationId` filters narrow both the rows and the totals. A foreign `locationId` gives an empty page and totals 0. `pageSize=101` gives `400` on `pageSize`. Staff can read.
- **R2:** Three documents posted at t1 < t2 < t3 (a `FakeTimeProvider`) list t3, t2, t1. `postedFrom=t2` includes t2; `postedTo=t3` excludes t3. `type=Receipt` filters; `type=receipt` and `type=1` give `400` on `type`; `postedTo <= postedFrom` gives `400` on `postedTo`. `lineCount` counts the request lines. An adjustment row carries its `reasonCode`. Tenant A never sees tenant B's documents.
- **R3:** Following a receipt's `201` `Location` header returns `200` with the same id. Its line shows the SKU, `SUP` → the location's code, `quantity`, `unitCost` and `totalCost`. A transfer's line shows from A to B. A reversal row inserted with `LedgerRows` makes the original's `reversedByDocumentId` and the reversal's `reversesDocumentId` point at each other. A random or foreign id gives `404 not-found`.
- **R4:** `/` redirects to `/stock`, which renders the stock grid with item, location, qty, unit, avg cost and value. The location filter reloads page 1. The pinned totals row shows the API's `totals.stockValue` as ₱ with 2 dp.
- **R5:** The form generates one key on open, sends the same key on a retry after a `400`/`422`/network error, renews it after `idempotency-key-reused` (showing a link to `/documents/<originalDocumentId>`), and renews it on a `:type` change. Submit is disabled while posting; editing a field clears its server error, so a corrected retry reuses the key. `insufficient-stock` marks every line with that `itemId` with "available vs requested". `lines[i].quantityDelta`/`quantity`/`unitCost`/`itemId` and header errors land on their controls. Outflow lines over on-hand trigger the confirm before posting. Staff navigating to `/documents/new/adjustment` lands on `/`. Success navigates to `/documents/<id>`.
- **R6:** `/documents` filters by type and by Manila day range (sent as UTC `Z` instants, D8) and pages. A row opens `/documents/:id`. The detail shows lines with unit and total cost, and links "Reverses …" / "Reversed by …".
- **R7:** `formatMoney(1234.5)` is `₱1,234.50`; `formatQuantity(10)` is `10` and `formatQuantity(1.25)` is `1.25`; `formatManilaDateTime('2026-10-30T06:14:00Z')` is `30 Oct 2026, 2:14 PM`; "Posted by" reads System / You / Another user. Staff sees no "Correct stock". Forms use one column below 600 px.
- **R8:** T13 specs exist (`format.spec.ts`, `posting-form.spec.ts`, `post-stock.spec.ts`), and the T5 probes and the T10 exemption are green.
- **R9:** Every `## Commands` line and CI `build` and `test` are green, and code-guardian has no open Blocker.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Documents posted-at index | IMS-11 | opus | medium | — | A | InventoryDbContext.cs, Migrations (new + snapshot), LedgerSchemaTests.cs |
| 02 | `GET /stock` with totals | IMS-11 | sonnet | medium | 01 | B | Features/Stock/**, InventoryModuleExtensions.cs, StockTests.cs, TenantIsolationTests.cs |
| 03 | `GET /documents` and the T10 exemption | IMS-11 | sonnet | high | 02 | C | Features/Documents/{DocumentQueryEndpoints, ListDocuments/**, DocumentSummaryResponse}, InventoryModuleExtensions.cs, CLAUDE.md, PostingClockTests.cs, DocumentQueryTests.cs, TenantIsolationTests.cs |
| 04 | `GET /documents/{id}` | IMS-11 | sonnet | medium | 03 | D | Features/Documents/{GetDocument/**, DocumentDetailResponse, DocumentQueryEndpoints}, InventoryModuleExtensions.cs, DocumentQueryTests.cs, TenantIsolationTests.cs |
| 05 | Web formatting and API services | IMS-11 | sonnet | medium | 04 | E | shared/format.ts, stock/stock.service.ts, documents/documents.service.ts (+ specs) |
| 06 | Stock on hand screen | IMS-11 | sonnet | medium | 05 | F | stock/stock.{ts,html,scss,spec.ts} |
| 07 | Posting form logic (T13) | IMS-11 | sonnet | high | 05 | G | documents/posting-form.ts (+ spec) |
| 08 | Post-stock screen and guard | IMS-11 | sonnet | high | 06, 07 | H | documents/post-stock.{ts,html,scss,spec.ts}, post-stock.guard.ts (+ spec) |
| 09 | Documents list and detail screens | IMS-11 | sonnet | medium | 08 | I | documents/documents.{ts,html,spec.ts}, document-detail.{ts,html,spec.ts} |
| 10 | Routes, landing page and navigation | IMS-11 | sonnet | low | 09 | J | app.routes.ts, app.html, app.spec.ts, home/home.ts (D) |

- **01 on opus:** it touches `**/Migrations/**` and `Persistence/**` (high-stakes globs), on a table with append-only triggers and RLS.

Every prompt runs in one working tree, and the web prompts share `web/` (one build, one lockfile), so the groups run in order.

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if the Testcontainers start-up flake shows: dotnet test -- --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false
# R1–R3, R8 (API): the new and changed integration tests
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*StockTests' --filter-class '*DocumentQueryTests' --filter-class '*TenantIsolationTests'
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*PostingClockTests' --filter-class '*LedgerSchemaTests' --filter-class '*MigrationRollbackTests' --filter-class '*RowLevelSecurityTests' --filter-class '*LedgerImmutabilityTests'
# R4–R8 (web): T13 and the screens are in the full suite above (format.spec, posting-form.spec, post-stock.spec, stock.spec, documents.spec, document-detail.spec, app.spec)
# Manual smoke (optional, needs the seed-password secret): dotnet run --project src/AppHost; sign in as the demo Staff user;
# / redirects to /stock and shows QC and MKT opening stock with a ₱ total; Receive stock posts and opens its detail; Documents lists it first;
# Staff sees no "Correct stock"; at 375 px the post-stock form is one column.
```

## Rollback
Revert the PR, then roll the schema back on the PR's build before running the older code: `dotnet ef database update Ledger --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext` (as the server admin). That drops only `IX_StockDocuments_PostedAt`. If the old code runs with the index still there, nothing breaks: an extra index is harmless, and EF never checks for unknown indexes. No data is written by this slice. Documents posted through the new form are ordinary ledger rows, valid with or without it.

## Downstream (for TASKS.md after merge)
- **S8:** add the `/stock` row click → `/items/:id` (D4). The item detail screen links each movement's document to `/documents/:id`, which exists now. Reuse `shared/format.ts` (`formatMoney`, `formatQuantity`, `formatManilaDateTime`, `documentTypeLabel`, `postedByLabel`) and `StockService`.
- **S9:** put the Reverse button (Admin) on `documents/document-detail`, shown when `reversedByDocumentId` is null and the type isn't `Reversal`. After a reversal, reload the detail; `reversedByDocumentId` then links to it. `DocumentsService` holds the `Idempotency-Key` pattern to copy.
- **S10:** the stock grid's `onHand` and `stockValue` columns get `cellClassRules` for `< 0` (red). With `AllowNegative`, `linesOverOnHand` is still only a warning; keep the confirm.
- **S12/T14:** the labels are fixed: nav "Receive stock", "Use or sell stock", "Move stock", "Correct stock", "Stock", "Documents"; routes `/stock` (`/` redirects there), `/documents/new/{receipt|issue|transfer|adjustment}`; money renders as `₱700.00`.
- **Index:** `IX_StockDocuments_PostedAt (TenantId, PostedAt)` exists; S11's scans may use it.

## Log
- 2026-11-13: planned
- 2026-11-13: audited, 4 fixes applied (0 BLOCK, 4 FIX; 5 NOTEs not applied)
