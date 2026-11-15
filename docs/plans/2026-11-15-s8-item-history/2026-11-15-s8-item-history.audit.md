## Audit · 2026-11-15-s8-item-history · 2026-11-15 · Codex (GPT-6) · PASS
Status: open

Graph built: none · Symbols traversed: 12 · Missed blast-radius items: 0

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|

**Bloat estimate:** planned ~19 files / ~500 LOC; minimal viable ~19 / ~450 LOC. The API contract, integration coverage, UI and route tests account for the scope; no speculative dependencies or helpers.
**Model cost:** 01/04 → sonnet (API and screen design); 02 → sonnet, high effort (tenant-scoped composable ledger SQL); 03/05 → haiku (mechanical service and row-click changes).

Checks: no affected callers are omitted; both API additions retain existing route contracts and tenant scoping; prompt ordering and files are consistent; the movement query's signed unpivot, window-before-pagination, and deterministic order match the ledger schema and serialized per-balance posting order; test coverage exercises V1–V6, page boundaries, zero-quantity revaluation, tenant isolation, permissions and UI behavior. The end-to-end commands include every project verification command. The rollback is sufficient because there is no schema or persisted-data change.

### Prompt for Claude
Nothing to apply.

## Audit 2026-11-15: PASS
Claude (Opus 5.5), `/audit-plan` fallback. Graph built: none (`graphify-out/` absent; grep per workflow §4) · Symbols traversed: 19 · Missed blast-radius items: 3 (rows 2, 4, 6)

Independently confirmed: `GetItemHandler` has one caller (`GetItem/Endpoint.cs:11`) and `ItemsService.get` none outside its spec; `ItemsTests:104-120` asserts named fields only; the T5 guard keys on `RoutePattern.RawText` (`TenantIsolationTests.cs:621-626`), so `GET /api/items/{id:guid}/movements` is the right probe string; T10 scans only `*Request`/`*Line` (`PostingClockTests.cs:82`); NetArchTest lets Features depend on `Kernel` (`ModuleBoundaryTests.cs:56`); RCSI is on (`EnableReadCommittedSnapshot` migration), so history reads don't block postings; the SQL's column names, `[LineNo]`, `Kind`/`Type` stored as strings, and the CHECKs used by the revaluation fact (`InventoryDbContext.cs:52,95,113-115`) match; LUC after an inflow is `Round4(StockValue/OnHand)` (spec §7 `:387`, `PostingRules.cs:139`), so R1's `lastUnitCost` 120 at A after V4 is right; the V1–V6 running rows match spec §7; ids are taken after the balance `UPDLOCK` (`PostingEngine.cs:172` → `:255`), so D2's partition order holds. No review constraint is touched.

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|
| NOTE | Optimization | `UNION ALL` seeks `IX_Movements_Item` twice and key-looks-up each movement twice. `ToPageAsync` runs the statement twice (count, then page), so that's 4 lookups per movement per request. The index isn't covering. | prompt 02 `:71-82`; `Paging.cs:34-35`; `InventoryDbContext.cs:121` | Optional: unpivot with one seek, `FROM [inventory].[StockMovements] AS m CROSS APPLY (VALUES ('In', m.ToLocationId, m.FromLocationId, m.Quantity, m.TotalCost), ('Out', m.FromLocationId, m.ToLocationId, -m.Quantity, -m.TotalCost)) AS v(Direction, LocationId, CounterpartLocationId, QuantityChange, ValueChange)`. Same rows, still no `ORDER BY`/CTE/`;`. Fine to leave inside the plan's "low thousands" assumption. |
| NOTE | Notes & constraints | `PostedAt` is taken **before** the ledger applock and the balance `UPDLOCK`; ids are taken after. Running values ordered by id are right (D2 holds), but under concurrent postings at one location the Posted column can be non-monotonic by milliseconds. | `PostingEngine.cs:96` (`now`) vs `:103`, `:172`, `:255` | Add one line to Assumptions; no code change. |
| NOTE | Usability | The T5 probe's "same SKU, no B rows leak" assertion can't fail. The SQL keys on `m.ItemId = {id}` with A's own item id, so B's rows never match, with or without the `TenantId` predicates, and RLS hides them anyway. Blast radius `:110` cites it as evidence for raw-SQL isolation. | prompt 02 `:76`, `:81`, `:103`; plan `:110` | Keep the cheap assertion, but re-word the mitigation: the probe tests layer 1 (EF `404`); layers 2–3 are defence in depth, checked by code-guardian reading the predicates (D8 already recommends that pass). |
| NOTE | Breakage | `ItemResponse.cs:5` says "An item as **every** Items endpoint returns it". That's false once `GET /items/{id}` returns `ItemDetailResponse`, and prompt 01 forbids touching the file. | `ItemResponse.cs:5`; prompt 01 `:41`, `:72` | Let 01 re-word that one summary ("…as the Items list and write endpoints return it"), or accept the stale comment. |
| NOTE | Requirements coverage | R5's negative money (`-₱480.00`) isn't asserted. Prompt 04 test 1 checks `+₱750.00` and `-4` only. Its second fixture omits `postedAt`, `movementId` and `locationCode`, so a literal transcription throws in `formatManilaDateTime`. | plan `:139`; prompt 04 `:41-42`; `format.ts:28-30` | Prompt 04: add `-₱480.00` to test 1. Build fixtures with a `movement(over: Partial<ItemMovement>)` factory with full defaults, as `document-detail.spec.ts:14` does. |
| NOTE | Unit shape | IMS-12 `depends: IMS-10` only. Prompts 04–05 build on IMS-11's `stock/stock.*`, `shared/format.ts` and `document-detail` (S7 D4 hands the row click to S8). IMS-11 is merged, so there's no ordering risk. | TASKS.md `:22`; plan `:132`; prompts 04 `:28-29`, 05 `:36-46` | `depends: IMS-10, IMS-11` in the Units table and TASKS line. |

**Bloat estimate:** planned 19 files / ~900 LOC (about 60% tests); minimal viable ~19 / ~850. Every file traces to R1–R7. No new dependency, helper, flag or index.
**Model cost:** 01 sonnet, 02 sonnet high (the SQL is dictated in the prompt, so opus isn't needed), 03 haiku, 04 sonnet, 05 haiku. All fit; no change.
