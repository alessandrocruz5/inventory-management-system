---
type: plan
kind: feature
status: audited
repo: inventory-management-system
delivery: pr
base: develop
sprint: 3
epic: IMS-27
created: 2026-11-18
risk: high
prompts: 15
graph_built: 2026-11-18T10:55:22+08:00
tags: [plan]
---
# Sprint 3 — Food-service pack: unit conversions and stocktakes

## Request
> food-service pack (unit conversions, stocktakes). this is now sprint 3

| # | Requirement |
|---|---|
| R1 | An item can have alternate units (for example `sack` = 25 `kg`, `case` = 12 `pc`), each a fixed factor of its base unit. An Admin manages them; anyone can see them. |
| R2 | Receipts, issues, transfers and adjustments accept a quantity, and a unit cost on inflows, in any active unit of the item. The ledger stays in base units, and the cost is exactly `Round4(entered quantity × unit cost per entered unit)`. |
| R3 | An item's base unit can't change once the item has a movement or an alternate unit, because that would silently reinterpret the ledger or the factors. |
| R4 | Staff or an Admin starts a stocktake for one physical location and records counted quantities per item, in any unit of the item. |
| R5 | An Admin posts a stocktake as one `CountCorrection` adjustment, with each line's variance measured against the system quantity when that line was counted. Posting is exactly-once: a retry, a refresh or a parallel click never posts twice. |
| R6 | Screens for R1, R2, R4 and R5 that work at 375 px, plus test evidence (vector set U, the stocktake rules and a Playwright flow). |

## Context used
- Note: `docs/Inventory System — Sprint 1 Spec.md` §1: the roadmap puts "unit conversions, stocktakes" in the food pack ("recipes buy in kg and use in grams; actual-vs-theoretical needs two physical counts"), and says the food pack should need nothing from the ledger beyond new virtual kinds. This plan holds to that: there is no new ledger column and no new document type.
- Note: spec §3: units of measure and stocktakes were out of scope for Sprint 1. §14 Decisions: "unit conversions and stocktakes move to sprint 2 with food". Sprint 2 became the UI redesign, so this work is now Sprint 3.
- Note: spec §5: `Items.BaseUnit` is free text, and `AdjustmentReason` already has `CountCorrection`. §6: the posting steps, the 1–100 lines rule and the receipt cost `quantity × unitCost`. §7: `InflowCost`. §9: routes and slugs. §10: post-stock form. §11: T5, T6, T10 and T12.
- Note: TASKS.md Downstream for IMS-4: every new tenant table goes in `security.TenantPolicy` in its creating migration (T6), and every new `/api` route gets a T5 probe. IMS-3: each new slug lands in spec §9, Kernel `ProblemTypes` and Angular `PROBLEM_MESSAGES` in the same PR. IMS-6: raw SQL runs only through EF, and `NEXT VALUE FOR` is unaffected. IMS-8: `DocumentPoster.PostAsync` is the base of every posting route. IMS-9: seed keys are `seed:…`, and e2e uses `CANDLE-WHT` or its own items.
- Note: CLAUDE.md Hidden coupling: the RequestHash canonical form, ProblemDetails slugs ↔ the Angular map, `security.TenantPolicy` and the `HasTrigger` declarations.
- Graph: `graphify update .` reported no topology change. The CLI in this install has no `god-nodes` or `affected`, so the blast radius below comes from grep and reading the code (workflow §4, "Without a graph").
- Code: `src/Modules/Inventory/Inventory/Domain/PostingRules.cs:13`: `PostingLine(LineNo, ItemId, From, To, Quantity, UnitCost)`. `:117-119`: inflow cost is `CostingPolicy.InflowCost(qty, unitCost)`, the one place R2's exact cost hooks in.
- Code: `Features/Posting/DocumentPoster.cs:28-133`: the order is key, pure validation, hash, step-2 replay, database checks with every 400 before any 422, then `PostingLine`s and the engine. `:107-119` is the only place a request line becomes a `PostingLine`.
- Code: `Features/Posting/RequestHash.cs`: four canonical forms, pinned by golden JSON in `tests/Inventory.UnitTests/RequestHashTests.cs:60,73`.
- Code: `Features/Posting/PostingValidation.cs`: `ValidatedLine(LineNo, ItemId, Quantity, UnitCost)`, `MaxAmount`, `MaxColumn`, `MaxLines = 100`, and `IdempotencyKey(...)`.
- Code: `Features/Items/UpdateItem/Handler.cs:67-75`: the SKU-immutability `HOLDLOCK, FORCESEEK` check, which R3 copies. `BaseUnit` is editable today even after movements.
- Code: `Persistence/Migrations/20261105132515_Items.cs`: the table-plus-RLS migration pattern. `Persistence/InventoryDbContext.cs:61-73,148-152`: `ConfigureTenantOwned`.
- Code: `tests/Inventory.IntegrationTests/TenantIsolationTests.cs:26,775-797`: the `Exempt` map, `[ProbesRoute]` and `Every_api_route_is_probed_or_exempt`. `PostingClockTests.cs:89-109`: T10 scans `Inventory.Features` types whose names end in `Request` or `Line` for date properties.
- Code: `Seeding/InventorySeeder.cs:28-94`: demo items. `RICE-WHT` and `SUGAR-BRN` are in `kg`, `EGG-MED` in `pc`.
- Code: `web/src/app/documents/posting-form.ts:24-27,77-123,214`: `PostingLine`, `buildPostingBody` and the `LINE_KEY` regex. `web/src/app/items/item-dialog.ts:82`: maps any `errors` key to its control, so a `baseUnit` error shows with no change. `tests/e2e/support/app.ts`: `signIn` and `createLocation`.

## Options
**A. Recording the entered unit**

| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| Input-only: convert at the API boundary; the ledger and documents stay in base units | Low | — | Low: no ledger-side table | Low | The RequestHash still covers the unit, so replays are exact |
| Record it: a new append-only `DocumentLineUnits` table written inside the posting transaction | +1 high-stakes prompt, triggers, DENY, T6/T7 | +1 day | Medium: a §6 step is added | Medium | Better audit trail (like storing FX rates) |

**Recommendation:** input-only. **Chosen:** input-only.

**B. Basis of a stocktake's variance**

| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| Snapshot at count: delta = counted − the system quantity when the line was counted | Low: uses the adjustment path as it is | — | Low | Low | Sales posted after the count are kept |
| On-hand at posting: delta = counted − on-hand at post | Lowest | — | **High**: silently erases sales posted between count and post | Low | Misstates stock |
| Reject stale lines: 409 if the balance moved since the count | Engine hook at step 7 | +1 day | Medium | Medium | Strictest, but painful during service |

**Recommendation:** snapshot at count. **Chosen:** snapshot at count.

## Decisions
- D1 (A, chosen): units are converted at the API boundary. Every ledger table, response and the stock screen stay in base units. The entered unit is not stored anywhere in the ledger.
- D2 (§6/§7 change, **approved by the user 2026-11-18**): `PostingLine` gains `decimal UnitFactor = 1m`, the number of base units in one entered unit. An inflow line with a unit cost is costed `CostingPolicy.InflowCost(Quantity / UnitFactor, UnitCost)`, which is exactly `Round4(entered quantity × unit cost per entered unit)`: `Quantity` is `entered × factor`, exact by D4, so the division returns the entered quantity exactly. Outflows, default adjustment costs, reversals and revaluations ignore the factor. With a factor of 1 every existing vector (V, R, A, N, X) is unchanged. Without D2, "1 case of 12 @ ₱500" would go through 41.6667/pc and cost 500.0004.
- D3: `ItemUnits (TenantId, ItemId, Code, Factor decimal(19,4) > 0, IsActive, Created*/Updated*)`, PK `(TenantId, ItemId, Code)`. A code is lowercase, at most 16 characters and never equal to the base unit. A factor is immutable: a new pack size gets a new code. A unit can be deactivated but never deleted, and an inactive code stays reserved.
- D4: base quantity = `quantity × factor`. It must be exact to 4 decimal places and at most `MaxAmount`, or the line gets `400 validation` on its quantity field. The system never rounds a quantity.
- D5: request lines carry an optional `unit`. Missing, null or equal to the base unit means factor 1. Otherwise it must be an active unit of that item, or the line gets `400` on `lines[i].unit`. Unit checks are 400s, so they run with the other database 400s, before any 422. `insufficient-stock`'s `available` and `requested` are in base units.
- D6 (hidden coupling): the canonical RequestHash form writes `"unit"` only when it isn't null, as the last property of the line. Every existing hash is byte-for-byte unchanged, so a retry of a pre-Sprint-3 post still replays.
- D7 (R3): `PUT /items/{id}` rejects a base-unit change with `400` on `baseUnit` while the item has a movement (the same `HOLDLOCK` range check as the SKU) or any `ItemUnits` row (an `UPDLOCK, HOLDLOCK` range over the item's slice of `ItemUnits`). Creating a unit takes `UPDLOCK` on the item row first. The two can deadlock, and the execution strategy retries the loser, as it does for SKU edits racing a first posting (IMS-6 Downstream).
- D8 (B, chosen): a stocktake is a mutable work document, not ledger data. `Stocktakes (Status: Draft | Posting | Posted | Cancelled)` and `StocktakeLines (Expected*, Counted*, UnitCost)` are tenant tables under RLS. Counting a line stores the counted base quantity, plus the balance's `OnHand` and `LastMovementId` read at that moment. Posting posts `delta = counted − expected` for every line whose delta isn't zero.
- D9: posting goes through `DocumentPoster`, then `PostingEngine`, as an `Adjustment` with `ReasonCode = CountCorrection`. A positive delta carries the line's `unitCost` (per base unit) when there is one, and otherwise falls back to §6's average rule. A negative delta never carries one. No new document type, no `StockDocuments` column, and the engine is untouched.
- D10 (§6 change, **approved by the user 2026-11-18**): the posting's Idempotency-Key is server-derived: `stocktake:<id:D>`. The posting flow is (1) Draft → Posting under `UPDLOCK` on the stocktake row, which freezes the lines, then (2) post, then (3) Posting → Posted with `PostedDocumentId`. A crash between (2) and (3) leaves the stocktake in Posting. Posting again replays the same document, because the frozen lines give the same RequestHash. A rejection (400 or 422) writes nothing, so the stocktake goes back to Draft. Parallel posts make one document (engine step 4). Client keys starting `stocktake:` get `400` on `Idempotency-Key`, so a client can't front-run a stocktake's key.
- D11: roles. Staff and Admin start stocktakes and count. Only an Admin posts or cancels. Anyone can view.
- D12: one open stocktake (Draft or Posting) per location, enforced by `UX_Stocktakes_Open (TenantId, LocationId) WHERE Status IN ('Draft','Posting')`. A second start gets `409 stocktake-open-exists` with `stocktakeId`. Counting, posting or cancelling a stocktake in the wrong state gets `409 stocktake-not-open` with `status`. The two new slugs land in spec §9, Kernel and Angular in the same PR as the API (IMS-30).
- D13: a new stocktake is prefilled with every active item that has a non-zero physical balance at the location, uncounted. Anyone counting can add other items. Posting needs every line counted (`400` on `lines[j].quantity`, "enter 0 for none") and at most 100 non-zero deltas (§6's line limit; otherwise `400` on `lines`). With no differences, the stocktake is Posted with no document.
- D14: the stocktake's line order everywhere (GET, error mapping) is by `Sku` with `StringComparer.Ordinal`. Errors the poster returns as `lines[i].unitCost` or `lines[i].quantityDelta` are remapped to the stocktake line index `j` as `lines[j].unitCost` or `lines[j].quantity`.
- D16 (Codex audit 2026-11-18): IMS-29's rollback fails closed. Minimal APIs use System.Text.Json's default, which skips unknown members, so reverting IMS-29 would silently turn an alternate-unit quantity into a base-unit quantity. IMS-29 is therefore rolled back by a fix-forward PR that keeps `unit` and rejects any value other than the base unit with `400 lines[i].unit`, after IMS-30 has been rolled back (see Rollback). A global `UnmappedMemberHandling.Disallow` was rejected: it changes every endpoint's behaviour, and a revert of IMS-29 would remove it too.
- D15: counts write under `UPDLOCK` on the stocktake row, which serializes them against posting's Draft → Posting step. A re-count overwrites the line and takes a new snapshot (last write wins). Lines carry no version.

**Assumptions:**
- The scope is unit conversions and stocktakes only, as the request's parenthesis says. Recipes, consumption, waste, prep and the Consumption/Scrap/Production virtual kinds from spec §1 are a later sprint.
- Units are per item, with no tenant-wide unit catalog. A food business's packs differ per item, and kg ↔ g is a one-row setup per item.
- `Factor` is `decimal(19,4)`, the project's decimal convention. That makes `oz` → `kg` (0.0283495) lossy, so tenants enter weights in a metric base. PH food service is metric.
- A stocktake's `unitCost` is per base unit only, and it's needed only for a positive delta on a balance with no stock to average.
- The 100-difference cap won't bind for small businesses. Posting in several documents, which would break exactly-once, is a non-goal.
- A count's snapshot is a plain RCSI read with no lock. The physical count and the snapshot can never be atomic anyway.
- Deactivating an item or location doesn't check for open stocktakes. Posting then gets `422 inactive-item` / `inactive-location`, and the Admin cancels.
- Seeding three demo units (`RICE-WHT` sack 25, `SUGAR-BRN` sack 50, `EGG-MED` tray 30) is in scope, so R1 and R2 can be demonstrated. Seeding doesn't change any stock.

**Non-goals:** recording the entered unit (D1), showing stock in non-base units, a tenant unit catalog, editing a factor, reactivating a unit, recipes and theoretical usage, actual-vs-theoretical reports, blind counts, freezing a location during a count, multi-document stocktakes, scheduled or cycle-count planning, a new document type or `StockDocuments` column, reserving the `seed:` key prefix (noted as a known risk).

## Change list
Paths are relative to the repo root. `Inv/` is `src/Modules/Inventory/Inventory/`.

| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `Inv/Persistence/ItemUnit.cs` | C | Entity (D3) | R1 | 01 |
| `Inv/Persistence/InventoryDbContext.cs` | M | `ItemUnits`, then `Stocktakes`/`StocktakeLines` configs | R1, R4 | 01, 09 |
| `Inv/Persistence/Migrations/*_ItemUnits.cs` (+Designer, snapshot) | C/M | Table, check, FK, RLS (T6) | R1 | 01 |
| `tests/Inventory.IntegrationTests/LedgerSchemaTests.cs` | M | `CK_ItemUnits_Factor`, stocktake checks and unique index | R1, R4 | 01, 09 |
| `Inv/Features/Items/Units/{CreateItemUnit,DeactivateItemUnit}/*` | C | Admin unit routes | R1 | 02 |
| `Inv/Features/Items/ItemUnitResponse.cs` | C | Response DTO | R1 | 02 |
| `Inv/Features/Items/{ItemEndpoints,ItemDetailResponse}.cs`, `GetItem/Handler.cs` | M | Map routes; `units` on GET /items/{id} | R1 | 02 |
| `Inv/InventoryModuleExtensions.cs` | M | Register new handlers | R1, R4, R5 | 02, 10, 11, 12 |
| `tests/Inventory.IntegrationTests/ItemUnitsTests.cs` | C | Unit API tests | R1 | 02 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | M | T5 probes for every new route | R1, R4, R5 | 02, 10, 11, 12 |
| `Inv/Features/Items/UpdateItem/Handler.cs` | M | Base-unit immutability (D7) | R3 | 03 |
| `tests/Inventory.IntegrationTests/ItemsTests.cs` | M | R3 tests | R3 | 03 |
| `Inv/Seeding/InventorySeeder.cs`, `tests/Inventory.IntegrationTests/SeedTests.cs` | M | Demo units | R1 | 03 |
| `docs/Inventory System — Sprint 1 Spec.md` | M | §5, §6, §7, §9, §10, §11, §15 amendments | R1–R6 | 03, 05, 07, 12, 15 |
| `web/src/app/items/{items.service,item-detail}.*`, `item-unit-dialog.*` | M/C | Units section (Admin add/deactivate) | R1 | 04 |
| `Inv/Domain/PostingRules.cs` | M | `UnitFactor` (D2) | R2 | 05 |
| `tests/Inventory.UnitTests/{CostingVectorTests,PostingRulesTests}.cs` | M | Vector set U and factor guards | R2 | 05 |
| `Inv/Features/Posting/{PostReceipt,PostIssue,PostTransfer,PostAdjustment}/Request.cs` | M | `unit` on lines | R2 | 06 |
| `Inv/Features/Posting/PostingValidation.cs` | M | Normalize `unit`; reserve `stocktake:` keys (D10) | R2, R5 | 06 |
| `Inv/Features/Posting/RequestHash.cs` | M | `unit` only when present (D6) | R2 | 06 |
| `tests/Inventory.UnitTests/{RequestHashTests,PostingValidationTests}.cs` | M | Golden hashes unchanged; new forms | R2 | 06 |
| `Inv/Features/Posting/UnitConversion.cs` | C | Exact base conversion and factor lookup | R2, R4 | 07 |
| `Inv/Features/Posting/DocumentPoster.cs` | M | Resolve and convert units; `PostValidatedAsync` | R2, R5 | 07, 12 |
| `tests/Inventory.IntegrationTests/UnitPostingTests.cs` | C | T16 API run | R2 | 07 |
| `web/src/app/documents/{posting-form,post-stock}.*` | M | Unit select per line | R2 | 08 |
| `Inv/Persistence/{Stocktake,StocktakeLine,StocktakeStatus}.cs` | C | Entities | R4 | 09 |
| `Inv/Persistence/Migrations/*_Stocktakes.cs` (+Designer, snapshot) | C/M | Tables, checks, FKs, `UX_Stocktakes_Open`, RLS | R4 | 09 |
| `src/Shared/Kernel/Problems/ProblemTypes.cs`, `Inv/Features/Problems.cs` | M | Two slugs (D12) | R4, R5 | 10 |
| `web/src/app/core/problems/problem-messages{,.spec}.ts` | M | Same slugs (hidden coupling) | R4, R5 | 10 |
| `Inv/Features/Stocktakes/{StartStocktake,ListStocktakes,GetStocktake}/*`, `StocktakeEndpoints.cs`, `StocktakeResponses.cs` | C | Start, list, detail | R4 | 10 |
| `Inv/Features/Stocktakes/{CountStocktakeLine,CancelStocktake}/*` | C | Count and cancel | R4 | 11 |
| `Inv/Features/Stocktakes/PostStocktake/*` | C | Post (D9, D10, D13, D14) | R5 | 12 |
| `tests/Inventory.IntegrationTests/StocktakeTests.cs` | C | T17 | R4, R5 | 10, 11, 12 |
| `web/src/app/stocktakes/*`, `web/src/app/{app.routes.ts,app.html}` | C/M | List, start, count sheet, post/cancel, nav | R4–R6 | 13, 14 |
| `tests/e2e/specs/stocktake.spec.ts` | C | Playwright flow | R6 | 15 |

## Blast radius
**Risk:** high. The plan touches `Domain/**`, `Features/Posting/**`, `Persistence/**`, migrations and Kernel (Shared code).

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `PostingLine` constructors (7 sites: `DocumentPoster`, `LedgerHarness`, unit tests) | Direct | positional record | No | `UnitFactor` is an optional last parameter, defaulting to 1 |
| Vector sets V, R, A, N, X (unit and API) | Direct | `PostingRules.Post` inflow path | No | Factor 1 gives `qty / 1 = qty`, the same arithmetic. All five sets rerun in 05 and 07 |
| Reversals, revaluation, `LastUnitCost`, INV-5, rebuild | Transitive | Cost on movements | No | They read `TotalCost`; no factor is involved (D2) |
| `ValidatedLine` constructors (14 sites) | Direct | positional record | No | `string? Unit = null` is an optional last parameter |
| RequestHash golden JSON and stored hashes | Non-graph | Hidden coupling | No | `unit` is written only when present (D6). Existing goldens stay, and a new test asserts that null and absent give the old bytes |
| `DocumentPoster.PostAsync` (4 posting handlers) | Direct | Signature | No | Same signature. Its body moves into `PostValidatedAsync`, which it calls |
| `PostingValidation.IdempotencyKey` | Direct | New `stocktake:` reservation | Only keys with that prefix | A UUID never starts with it. The e2e and Angular clients use `crypto.randomUUID()` |
| `insufficient-stock` UI message | Non-graph | `available`/`requested` in base units | Display only | 08 shows the base unit in the message |
| `PUT /items/{id}` base-unit edit | Direct | New 400 | Behaviour change by design (R3) | `item-dialog.ts:82` already shows a `baseUnit` error. Covered in 03 |
| `ItemDetailResponse` / Angular `ItemDetail` | Direct | Additive `units` | No | Additive JSON field |
| T5 `Every_api_route_is_probed_or_exempt` | Non-graph | 8 new routes | Yes, if any route is unprobed | Each prompt that adds a route adds its probe |
| T6 catalog (`RowLevelSecurityTests`) | Non-graph | 3 new tenant tables | Yes, without RLS | RLS lands in each creating migration (01, 09) |
| T10 `No_request_type_carries_a_date` | Non-graph | Types ending `Request`/`Line` | Yes, if a dated type matches | Request types carry no dates. Dated DTOs are named `*Response` |
| T12 architecture (single public type) | Non-graph | New types | Yes, if any is public | Everything `internal`. Migrations are public by the existing exemption |
| `InventoryDbContextModelSnapshot` | Non-graph | Two migrations in different units | Merge conflict, if out of order | IMS-28 → IMS-30 is ordered by `depends` |
| Kernel `ProblemTypes` ↔ `PROBLEM_MESSAGES` ↔ spec §9 | Non-graph | Hidden coupling | Yes, if split across PRs | All three in prompt 10, in IMS-30's PR |
| `inventory_app` grants | Non-graph | Schema-level `SELECT, INSERT, UPDATE` | No | The new tables are covered, and no `DELETE` is granted ("no hard deletes") |
| Seed (`SeedTests`, e2e expectations) | Non-graph | New `ItemUnits` rows | No | No stock changes. The insert is idempotent by PK; a renamed SKU is skipped (IMS-9 pattern) |
| Rolling back IMS-29 (any client still sending `unit`) | Non-graph | System.Text.Json skips unknown members | Yes, with a plain revert: silent misposting | Fix-forward rollback that keeps `unit` and rejects anything but the base unit with 400, after IMS-30 is rolled back (D16, Rollback) |
| `deploy/` compose and Bicep | Non-graph | AppHost, env, ports | No | Untouched |
| Ledger lock / `LedgerLock` Shared | Transitive | Stocktake post via engine | No | It goes through `PostingEngine`, which takes the lock |

**Known risks in the touched area:**
- **SKU / base-unit edit vs first posting deadlock (IMS-6).** D7 adds a second range check. Both sides retry under the strategy.
- **The `seed:` key prefix isn't reserved (IMS-9).** A client could send a `seed:` key. This is out of scope here and noted for the audit.
- **The ledger tables refuse `DELETE` (T7).** Stocktake tests must create their own tenants.
- **Testcontainers start-up crash with parallel modules (IMS-6).** Use `--max-parallel-test-modules 1` if it happens.
- **IMS-23 Downstream:** grid columns use flex. New AG Grid screens follow `grid-theme.ts`, or use the row-list pattern from IMS-24.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-28 | Item units | Added | 01, 02, 03, 04 | — | yes | R1: an Admin adds `case` = 12 to a `pc` item, and `GET /items/{id}` lists it. Staff gets 403. A duplicate code, the base unit as a code, or a factor ≤ 0 or with more than 4 dp gets 400. Deactivate is idempotent. T5 probes and T6 pass. R3: a base-unit change after a movement, or with a unit, gets 400 `baseUnit`. The seed adds 3 units idempotently. The item detail screen lists units, with Admin add and deactivate. |
| IMS-29 | Posting in units | Added | 05, 06, 07, 08 | IMS-28 | yes | R2: vector set U passes in unit tests and through the API (1 case @ ₱500 = 500.0000). V/R/A/N/X are unchanged. An unknown or inactive unit gets 400 `lines[i].unit`. A non-exact conversion gets 400 on the quantity. Existing RequestHash goldens are unchanged, and a replay with `unit` is 200. A `stocktake:` client key gets 400. The post-stock form offers each item's units. |
| IMS-30 | Stocktakes API | Added | 09, 10, 11, 12 | IMS-29 | yes | R4: start (prefilled), count with unit conversion and a snapshot, list, detail, cancel. One open stocktake per location (409). R5: post makes one CountCorrection adjustment of the snapshot deltas. A sale posted after a count is kept. Parallel and repeated posts make one document. A rejection returns the stocktake to Draft. Staff gets 403 on post and cancel. T5 probes. Slugs in spec §9, Kernel and Angular. |
| IMS-31 | Stocktake screens | Added | 13, 14, 15 | IMS-30 | no | R6: `/stocktakes` list and start, and a `/stocktakes/:id` count sheet at 375 px with unit choice, a "moved since count" flag, Admin Post/Cancel, and a link to the posted document. Vitest. Playwright flow: Staff counts, Admin posts, stock shows the count. Spec §10/§11/§15 updated. |

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Item units schema | IMS-28 | opus | high | — | A | ItemUnit.cs, InventoryDbContext.cs, migration, LedgerSchemaTests.cs |
| 02 | Item units API | IMS-28 | sonnet | high | 01 | B | Features/Items/Units/*, ItemUnitResponse.cs, ItemEndpoints.cs, ItemDetailResponse.cs, GetItem/Handler.cs, InventoryModuleExtensions.cs, ItemUnitsTests.cs, TenantIsolationTests.cs |
| 03 | Base-unit immutability and demo units | IMS-28 | opus | high | 02 | C | UpdateItem/Handler.cs, ItemsTests.cs, InventorySeeder.cs, SeedTests.cs, spec |
| 04 | Item detail: units section | IMS-28 | sonnet | medium | 02 | D | items.service.ts(+spec), item-detail.*, item-unit-dialog.* |
| 05 | Domain UnitFactor and vector set U | IMS-29 | opus | high | — | E | PostingRules.cs, CostingVectorTests.cs, PostingRulesTests.cs, spec |
| 06 | Unit on request lines, hash and key reservation | IMS-29 | opus | high | 05 | F | 4× Request.cs, PostingValidation.cs, RequestHash.cs, RequestHashTests.cs, PostingValidationTests.cs |
| 07 | DocumentPoster unit conversion and T16 API run | IMS-29 | opus | high | 06 | G | UnitConversion.cs, DocumentPoster.cs, UnitPostingTests.cs, spec |
| 08 | Post-stock form: unit per line | IMS-29 | sonnet | high | 07 | H | posting-form.ts(+spec), post-stock.ts/.html(+spec) |
| 09 | Stocktake schema | IMS-30 | opus | high | — | I | Stocktake.cs, StocktakeLine.cs, StocktakeStatus.cs, InventoryDbContext.cs, migration, LedgerSchemaTests.cs |
| 10 | Stocktake start, list, detail and slugs | IMS-30 | opus | high | 09 | J | ProblemTypes.cs, Problems.cs, problem-messages(.spec).ts, Features/Stocktakes/{Start,List,Get}/*, StocktakeEndpoints.cs, StocktakeResponses.cs, InventoryModuleExtensions.cs, StocktakeTests.cs, TenantIsolationTests.cs |
| 11 | Stocktake count and cancel | IMS-30 | sonnet | high | 10 | K | Features/Stocktakes/{CountStocktakeLine,CancelStocktake}/*, StocktakeEndpoints.cs, InventoryModuleExtensions.cs, StocktakeTests.cs, TenantIsolationTests.cs |
| 12 | Stocktake post (exactly-once) | IMS-30 | opus | xhigh | 11 | L | DocumentPoster.cs, Features/Stocktakes/PostStocktake/*, StocktakeEndpoints.cs, InventoryModuleExtensions.cs, StocktakeTests.cs, TenantIsolationTests.cs, spec |
| 13 | Stocktakes list and start screen | IMS-31 | sonnet | medium | — | M | stocktakes.service.ts(+spec), stocktakes.ts/.html/.scss(+spec), start dialog, app.routes.ts, app.html |
| 14 | Count sheet screen | IMS-31 | sonnet | high | 13 | N | stocktake-detail.ts/.html/.scss(+spec), count dialog, app.routes.ts |
| 15 | Playwright stocktake flow and docs | IMS-31 | sonnet | medium | 14 | O | tests/e2e/specs/stocktake.spec.ts, spec §10/§11/§15 |

Why each `opus`:
- **01, 09:** new tenant tables with RLS in `**/Migrations/**`, a High-stakes path. T6 and the rollback order depend on getting them exactly right.
- **03:** the lock order between the base-unit check and unit creation (D7). It adds a deadlock path that has to be reasoned through, plus the seed.
- **05:** `Domain/**` costing, a High-stakes path. It's the §7 change.
- **06:** `Features/Posting/**`, a High-stakes path. The canonical-hash hidden coupling has to stay byte-stable.
- **07:** `DocumentPoster` is the base of every posting route. The 400-before-422 ordering and the exact conversion matter.
- **10:** Kernel `ProblemTypes` is Shared code, and the start handler has to map the unique-index race to 409.
- **12:** an exactly-once state machine around `PostingEngine` (D10), with concurrency and error remapping. It's the riskiest prompt in the sprint.

## End-to-end verification
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false && cd ..
# R1/R3
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*ItemUnitsTests' --filter-class '*ItemsTests' --filter-class '*SeedTests'
# R2 (vector set U unit + API; V/R/A/N/X unchanged; hash goldens)
dotnet test --project tests/Inventory.UnitTests
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*UnitPostingTests' --filter-class '*PostingVectorTests'
# R4/R5
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*StocktakeTests' --filter-class '*TenantIsolationTests' --filter-class '*RowLevelSecurityTests' --filter-class '*PostingClockTests'
dotnet test --project tests/Architecture.Tests
# R6 (stack running, E2E_PASSWORD set; README "End-to-end tests")
cd tests/e2e && npx playwright test specs/stocktake.spec.ts
```

## Rollback
- Each unit is one PR. Revert the PR on `develop`, except IMS-29 (below).
- **IMS-28 and IMS-30 add migrations with RLS** (IMS-4 Downstream): roll the schema back on that build first (`dotnet ef database update <previous>`, whose `Down` drops the predicates before the tables), then revert the code.
- No ledger row is created or changed by anything except normal postings. A stocktake's adjustment is reversed like any document (`POST /documents/{id}/reversal`, latest-only).
- **IMS-29 is never rolled back with `git revert`** (D16). The API sets no `UnmappedMemberHandling`, so a reverted API would silently skip `unit` and post `quantity: 1, unit: "sack"` as 1 base unit. IMS-29's rollback is a fix-forward PR, in this order:
  1. If IMS-30 has merged, roll IMS-30 back first: its schema per the bullet above, then its code. The count endpoint converts units through IMS-29.
  2. In the fix-forward PR, keep `string? Unit` on the four posting request line types and in `PostingValidation`. Return `400 validation` on `lines[i].unit` when `unit` is not null and not the item's base unit. Remove only the conversion (`UnitConversion`, the `PostingLine.UnitFactor` use in `DocumentPoster`). Keep D6's hash rule, so `unit` stays in the RequestHash.
  3. Verify before merging: an integration test posts with `unit: "<alternate>"`, expects 400 `lines[0].unit` and checks that no movement was written. A post with no `unit`, or with the base unit, still returns 200.
  4. Any document already posted in units stays valid, because the ledger is in base units.

## Log
- 2026-11-18: planned. The user chose input-only units, a snapshot-at-count variance, Staff counts / Admin posts, and approved the §6/§7 changes D2 and D10.
- 2026-11-18: audited, 1 fix applied (Codex BLOCK: IMS-29's rollback now fails closed instead of reverting, D16).
- 2026-11-18: applied Codex audit (1 applied, 0 disputed)
