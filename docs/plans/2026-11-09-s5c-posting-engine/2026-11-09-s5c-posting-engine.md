---
type: plan
kind: feature
status: audited
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-09
risk: high
prompts: 6
graph_built: none
tags: [plan]
---
# S5c Posting engine (IMS-8)

## Request
> IMS-8

This plans the existing unit `IMS-8 — S5c Posting engine · Added · spec: §12 S5c · depends: IMS-6, IMS-7 · model: opus` (TASKS.md, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S5c: *"`PostingEngine` in `Persistence` (section 6, steps 1–10); receipts and issues with idempotency. Tests: T1 API run of R, T2, T4, T10 · code-guardian review."*

The scope also includes the S5c items that earlier units locked:
- IMS-3: the posting transaction runs inside `Database.CreateExecutionStrategy()`, and each new slug lands in spec §9, Kernel `ProblemTypes` and Angular `PROBLEM_MESSAGES` in the same PR.
- IMS-4: T5 probes for every new route; raw SQL only through EF.
- IMS-5: the `Features/<Area>/<Endpoint>/{Endpoint,Handler,Request}.cs` layout.
- IMS-6: the sequence, `LineNo` quoting, lock hints and deadlock notes.
- IMS-7: call `PostingRules.Post` at step 8 and map `PostingRejectedException` subtypes (S5b plan, "Downstream").

| # | Requirement |
|---|---|
| R1 | `POST /api/documents/receipts` (Staff, Admin). Each line moves Supplier → the physical `locationId` at `Round4(quantity × unitCost)`. A success returns `201 Created` with `Location: /api/documents/{id}` and the document body (R8). |
| R2 | `POST /api/documents/issues` (Staff, Admin). Each line moves the physical `locationId` → Customer at the moving-average outflow cost (spec §7, `CostingPolicy.OutflowCost`). A short balance returns `422 insufficient-stock` with `itemId`, `locationId`, `available` and `requested`, and nothing is written. |
| R3 | Step 1 validation (spec §6), returning `400 validation` with per-field `errors`. Lines: 1–100, each item once. Quantity `> 0` and unit cost `≥ 0`, each with at most 4 dp and at most 999,999,999.9999. **New (Decision D1):** a line total `Round4(qty × unitCost)` above 999,999,999,999,999.9999 is rejected on `lines[i].unitCost`. Items and locations must exist and be the right kind: a foreign or unknown id gives `400`, the same as a missing one. Inactive ones give `422 inactive-item` / `inactive-location`. |
| R4 | Idempotency (spec §6 step 2). The `Idempotency-Key` header is required; without it the post returns `400 idempotency-key-missing`. The SHA-256 `RequestHash` is over the method, the route and the canonical validated request. The same key with the same hash replays the stored document with `200` and `Idempotent-Replayed: true`. The same key with a different hash returns `409 idempotency-key-reused` with `originalDocumentId`. A parallel duplicate that hits 2627/2601 at step 4 replays. Only a successful post consumes a key. **New (Decision D2):** the replay lookup runs before the database checks of step 1. |
| R5 | `Persistence/PostingEngine` runs steps 3–10 inside the execution strategy, with 3 retries on 1205 (spec §6). Exhausted retries return `409 concurrency-conflict`. The steps: (3) `sp_getapplock` on `ledger:<TenantId>`, Shared, owned by the transaction. (4) The document row, inserted final. (5) Ensure-balance inserts in key order, swallowing 2627/2601. (6) One `UPDLOCK, ROWLOCK` statement that seeks on the clustered key. (7) Items and locations re-read `WITH (READCOMMITTEDLOCK)`. (8) `PostingRules.Post`. **New (D1):** a resulting `OnHand` or `StockValue` outside `decimal(19,4)` returns `400 validation` on `lines[i].quantity`. (9) Ids from `MovementIds`, movement inserts and balance updates (`AllowNegative`, `LastMovementId`, `LastUnitCost`). (10) Commit. |
| R6 | `PostedAt` comes from the injected `TimeProvider` and `PostedBy` from `ICurrentUser`. No request carries a date (T10). |
| R7 | T5: both routes have probes. Tenant B's ids in a tenant A body give `400 validation`, and tenant B is unchanged. |
| R8 | The response (Decision D3) has the same shape for `201` and for a replay: the document fields, `movements[]` in `LineNo` order, and `balances[]` for the touched physical (item, location) pairs, plus `balancesAsOf`. On a `201` the balances are as committed. On a replay they're re-read at replay time. |
| R9 | The slugs `idempotency-key-missing`, `idempotency-key-reused`, `insufficient-stock`, `inactive-item` and `inactive-location` are in Kernel `ProblemTypes` and Angular `PROBLEM_MESSAGES` (§9 already lists them). Spec §6 is amended for D1 and D2 in the same PR. |
| R10 | Named tests: T1 API run of set R; T2 (20 parallel issues of the last unit); T4 (sequential, parallel, deterministic race, last-unit same-key pair, different body); T10 (`FakeTimeProvider`). Unit tests for validation and the canonical hash. |
| R11 | Every `## Commands` line and CI `build` and `test` pass, and code-guardian has reviewed. |

## Context used
- Spec `docs/Inventory System — Sprint 1 Spec.md`:
  - §5 `:200-289`: the table contract; `IdempotencyKey varchar(64)`, `RequestHash binary(32)`.
  - §6 `:291-343`: posting steps 1–10, hard rules, invariants.
  - §7 `:345-468`: costing and vector set R.
  - §8 `:469-523`: raw SQL through EF, `inventory_app` grants.
  - §9 `:524-591`: routes, request bodies, problem types.
  - §11 `:625-659`: T1, T2, T4, T5, T10.
  - §12 `:660-685`: S5c scope.
- Spec audit `docs/Inventory System — Sprint 1 Spec.audit.md`:
  - `:13`: the step-4 document-first BLOCK fix.
  - `:25`: the canonical form; only a successful post consumes a key.
  - `:31`: the access path, not `ORDER BY`, sets the lock order.
  - `:32`: a replay can't reproduce its balances; return current ones labelled as current.
- TASKS.md Downstream:
  - IMS-3: the execution strategy, slugs in three places, the fixture pattern.
  - IMS-4: T5 probes, raw SQL via EF.
  - IMS-5: the Features layout, `Problems.cs`, the trailing slash in T5 raw text.
  - IMS-6:
    - The S5c notes: `NEXT VALUE FOR` 11719, `[LineNo]`, `UPDLOCK` works under the deny, shared ledger reads.
    - The lock-order notes: deactivation/posting 1205, SKU-edit/first-posting 1205, `HOLDLOCK` next-key.
    - The test notes: `LedgerRows.WaitUntilBlockedAsync`, `--max-parallel-test-modules 1`.
- S5b plan `docs/plans/2026-11-09-s5b-costing-rules/2026-11-09-s5b-costing-rules.md`:
  - `:80-122` Decisions: step-1 pure checks belong to S5c's handlers; the rejection exceptions.
  - `:124-133` Downstream for S5c. These aren't in TASKS.md yet; see Open items.
  - `:209` log: restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1` on this machine.
- Code:
  - `src/Modules/Inventory/Inventory/Domain/PostingRules.cs:22` `Post(kind, lines, balances)`, `:226` `CheckDirection` (receipt: virtual→physical with cost; issue: physical→virtual without cost).
  - `Domain/PostingRejections.cs:4-44`: `InsufficientStockException(ItemId, LocationId, Available, Requested)`.
  - `Domain/Balance.cs:11`, `Domain/LedgerMovement.cs:17`: `Balance`, `PlannedMovement`.
  - `Persistence/InventoryDbContext.cs:78-144`: document, movement and balance mapping. `Id` is `ValueGeneratedOnAdd` on documents and `ValueGeneratedNever` on movements; `HasTrigger` is declared; balance `RowVersion` is a concurrency token.
  - `Persistence/StockDocument.cs`, `StockMovement.cs`, `StockBalance.cs`: the entities.
  - `Features/Items/DeactivateItem/Handler.cs:13-68`: the pattern for the strategy, `ChangeTracker.Clear()`, the transaction and `FromSql` with lock hints plus `FORCESEEK` under RLS.
  - `Features/Items/CreateItem/{Endpoint,Handler,Request}.cs`: the endpoint and handler pattern; `Features/Items/ItemEndpoints.cs:15`: the group pattern.
  - `Features/Problems.cs:13-38`: the ProblemDetails factories and `IsUniqueViolation`.
  - `Features/InventoryPolicies.cs:10`: only an Admin policy exists.
  - `InventoryModuleExtensions.cs:31-35`: `AddDbContext` + `EnrichSqlServerDbContext` (Aspire 13.6.4 `EnableRetryOnFailure()` defaults, i.e. 6 retries; `DisableRetry` must be set for a custom strategy).
  - `src/Api/Program.cs:21`: `TimeProvider.System` singleton; `:60`: the `/api` group `RequireAuthorization()`.
  - `src/Api/Setup/DatabasePrincipals.cs:29-34`: schema DML, `DENY UPDATE, DELETE` on the ledger, `GRANT UPDATE` on `MovementIds`.
  - `src/Shared/Kernel/Problems/ProblemTypes.cs`: the slug constants.
  - `web/src/app/core/problems/problem-messages.ts:2-10` and `.spec.ts`: the Angular map.
  - `tests/Inventory.IntegrationTests/`:
    - `DatabaseFixture.cs:90-110`: tenant, user and sign-in helpers.
    - `ApiFactory.cs`: no service override yet.
    - `TenantIsolationTests.cs:23-31`: `Exempt` and the probes; `:325-348`: the route guard.
    - `LedgerRows.cs:20-60, 119-147`: raw-connection helpers and `WaitUntilBlockedAsync`.
  - `Inventory.csproj:24-26`: `InternalsVisibleTo` for both Inventory test projects.
- Graph: none (`graphify-out/` is absent); used grep instead.

## Decisions
- **D1 Overflow guards (user, 2026-11-09, amends spec §6):** step 1 rejects a line whose `Round4(qty × unitCost)` exceeds `999,999,999,999,999.9999` (`decimal(19,4)` max), with `400 validation` on `lines[i].unitCost`. After `PostingRules.Post`, the engine rejects any resulting balance whose `|OnHand|` or `|StockValue|` exceeds the same bound, with `400 validation` on `lines[i].quantity` for the first line of that item. Without these, SQL 8115 surfaces as a `500`.
- **D2 Replay before database checks (user, 2026-11-09, amends spec §6):** the order is pure checks → canonical hash → step 2 lookup → database existence, kind and active checks → step 3. A retry of a successful post replays even after master data changed. The hash still covers the validated (normalized) request, so it is unaffected.
- **D3 Response shape (user, 2026-11-09):** `{ id, type, reference, notes, postedAt, postedBy, movements[], balances[], balancesAsOf }` for both `201` and `200` replay.
  - Movement fields: `id`, `lineNo`, `itemId`, `fromLocationId`, `toLocationId`, `quantity`, `totalCost`, `reversesMovementId`.
  - Balance fields: `itemId`, `locationId`, `onHand`, `stockValue`, `lastUnitCost`.
  - Balances cover the physical (item, location) pairs the document touched. Virtual balances are left out: Supplier and Customer quantities mean nothing to a user.
  - `balancesAsOf` is the commit's `PostedAt` on a `201`, and `TimeProvider.GetUtcNow()` at read time on a replay.
- **One approach, spec-mandated.** §4, §6 and the IMS-7 Downstream fix the structure: `Persistence/PostingEngine` owns the transaction, the locks and the SQL; `Domain/PostingRules` decides; `Features/Posting/*` handles HTTP. There's no Options section.
- **Engine API.** The engine exposes two methods, and the shared `Features/Posting/DocumentPoster` handles HTTP, runs step 1's database part between them, and maps outcomes to results:
  - `FindExistingAsync(key, hash)` is step 2. It returns a replay, a reused key or nothing.
  - `PostAsync(PostingCommand)` runs steps 3–10, plus the race replay.
- **Retries = 3.** `EnrichSqlServerDbContext<InventoryDbContext>(s => s.DisableRetry = true)` plus `UseSqlServer(…, sql => sql.EnableRetryOnFailure(maxRetryCount: 3))`, matching spec §6. This applies to every `InventoryDbContext` strategy, including the S4/S5a handlers; see the Blast radius. `RetryLimitExceededException` → `409 concurrency-conflict`.
- **Commit ambiguity is safe.** If a commit succeeds but the connection drops, the strategy retries. Step 4 then hits the unique key (2627), and the request replays its own document. The key makes the retry idempotent.
- **Canonical form (RequestHash, Hidden coupling).** The pre-image is UTF-8 of `"{METHOD} {route}\n{json}"`:
  - METHOD is uppercase, and the route is the literal lowercase path, for example `POST /api/documents/receipts`.
  - `json` is written with `Utf8JsonWriter`: no whitespace, every property always present and written in a fixed order, null for absent.
  - Guids are written in `"D"` lowercase form.
  - Strings are the normalized (trimmed, empty → null) values.
  - Decimals are written as raw JSON numbers with trailing zeros stripped and the invariant culture, with no exponent (`10.0000` → `10`, `0.50` → `0.5`).
  - Lines keep request order.
  - Receipt order: `locationId, reference, notes, lines[{itemId, quantity, unitCost}]`. Issue order: `locationId, reference, notes, lines[{itemId, quantity}]`.
  - The server canonicalizes, so client formatting can't change the hash. S7's form only has to resend the same values.
- **Idempotency-Key header:** missing, empty or whitespace → `400 idempotency-key-missing`. More than 64 characters, or any character outside printable ASCII `0x21–0x7E` → `400 validation` on `errors["Idempotency-Key"]`, because the column is `varchar(64)`. Keys are stored as sent but compared case-insensitively: the column has the database's default (case-insensitive) collation, and both `UX_StockDocuments_IdempotencyKey` and the step-2 lookup follow it (audit 2026-11-09). Changing that would need a column change, so it stays.
- **Error keys** are camelCase JSON paths with zero-based indexes, as in S4's `errors.sku`: `locationId`, `lines`, `lines[0].itemId`, `lines[0].quantity`, `lines[0].unitCost`, `reference`, `notes`. Reference ≤ 100 and notes ≤ 500 characters after trimming (spec §5).
- **Step 1 database checks in `DocumentPoster`:**
  - Items are loaded by id through the tenant filter. Unknown or foreign → `400` on `lines[i].itemId`; inactive → `422 inactive-item { itemId }`.
  - `locationId` must be a physical location. Unknown, foreign or virtual → `400` on `locationId`; inactive → `422 inactive-location { locationId }`.
  - The tenant's Supplier or Customer location is resolved by `Kind`. A missing one is a provisioning bug (`InvalidOperationException` → 500).
  - Step 7 repeats the active checks under `READCOMMITTEDLOCK`, so a deactivation that commits between the two is caught with the same `422`. Its reads are raw SQL with the filter inside and `FORCESEEK`: `SELECT * FROM [inventory].[Items] WITH (READCOMMITTEDLOCK, FORCESEEK) WHERE [Id] IN (@i0, …)`, and the same for `Locations`. A scan would S-lock every row, other tenants' included (IMS-6: master-data reads carry `FORCESEEK`; audit 2026-11-09). 06 proves it with a deterministic mid-posting deactivation test.
- **Step 6 lock statement:** `FromSqlRaw` with one parameter per id: `SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, ROWLOCK, FORCESEEK, INDEX(PK_StockBalances)) WHERE [LocationId] IN (@l0, @l1) AND [ItemId] IN (@i0, …)`.
  - `INDEX(PK_StockBalances)` pins the clustered key: `FORCESEEK` alone could pick `IX_Balances_Location`, which also seeks this predicate and would lock in (LocationId, ItemId) order (audit 2026-11-09).
  - A receipt or issue touches exactly items × {physical, virtual}, so the cross product is exactly the touched set; S6 transfers and S9 reversals keep that shape.
  - The engine asserts that the row count equals `items × locations`.
  - The parameterized `IN` seek on the clustered key `(TenantId, ItemId, LocationId)` sorts its seek ranges, which gives key order (spec audit `:31`). The ids are also sorted in SQL Server `uniqueidentifier` order (`SqlGuid`) before binding.
  - RLS supplies `TenantId` to the seek, as in `DeactivateItem/Handler.cs:38-43`.
  - Rows are tracked, and step 9 updates them through EF. `UPDATE` is granted on `StockBalances`, and the `RowVersion` check can't fail under the `UPDLOCK`.
- **Step 5:** one `ExecuteSqlAsync` per touched key, in `SqlGuid` (ItemId, LocationId) order: `INSERT … SELECT {item}, l.Id, IIF(l.Kind <> 'Physical',1,0), l.AllowNegativeStock, 0, 0 FROM [inventory].[Locations] l WHERE l.Id = {loc} AND NOT EXISTS (…)`. `TenantId` comes from the column default. A `SqlException` 2601/2627 is caught and ignored: it's a statement-level error, so the transaction stays usable.
- **Step 9 ids:** a single `EXEC sys.sp_sequence_get_range @sequence_name = N'inventory.MovementIds', @range_size = @n, @range_first_value = @first OUTPUT` with `@first` as `SqlDbType.Variant` gives contiguous ids, assigned in `LineNo` order. It needs `UPDATE` on the sequence, which is granted (`DatabasePrincipals.cs:34`). If it fails as `inventory_app`, fall back to one `SqlQueryRaw<long>("SELECT NEXT VALUE FOR [inventory].[MovementIds] AS [Value]").ToListAsync()` per movement (IMS-6 Downstream), and say so in the report. Contiguity isn't a spec requirement: `MovementIds` is database-wide, so the fallback can interleave with other tenants' postings. Tests assert ids that strictly ascend in `LineNo` order.
- **Document insert at step 4** goes through EF (`Add` + `SaveChangesAsync`), because the triggers are declared. Movements are inserted through EF at step 9, in the same `SaveChangesAsync` as the balance updates. The engine never updates or deletes a document or movement (review constraint).
- **`PostedBy` is part of `PostingCommand`,** not read inside the engine, so S5d's seeder can pass its own principal. The handlers pass `ICurrentUser.UserId`.
- **Roles:** receipts and issues need only the `/api` group's sign-in. Every user has `Admin` or `Staff` (`TenantProvisioner.EnsureUserAsync`), and §9 allows both. No new policy.

**Assumptions:**
- `sp_getapplock` with `@LockTimeout` left at the session default (wait), `@DbPrincipal = 'public'`. A return value `< 0` throws `InvalidOperationException` (500). Only S11's rebuild takes it Exclusive, and S11 doesn't exist yet.
- `Location: /api/documents/{id}` points at a route that S7 adds (`GET /documents/{id}`). That's a forward reference, and harmless.
- An issue body that carries `unitCost` binds to a DTO without that property, so the value is ignored rather than rejected. This matches S4's handling of unknown fields.
- Malformed JSON (a non-number in `quantity`, or a decimal overflow during deserialization) returns the framework's `400` without the `validation` type, as S4 endpoints do today.
- The replay body's `postedBy` and the movements are the stored ones. Its balances are current (D3).
- The engine maps only `InsufficientStockException` from `PostingRejectedException`. `UnitCostRequiredException` (S6) and `ReversalNotLatestException` (S9) are unreachable from receipts and issues, so `DocumentPoster` rethrows them as a bug, which surfaces as a 500.
- T2's 20 requests serialize on one balance row. Under the 3-retry strategy, a 1205 is only possible against an unrelated deactivation, and none runs in T2.
- `IX_Balances_Location` and `IX_Movements_Item` stay as they are; no index is added (spec §6 step 6).

**Non-goals:**
- Transfers, adjustments, the clustered-key A↔B lock test T3 and the T1 API run of A (S6). Reversals, T8, and parallel same-key reversals (S9). The negative-stock estimate and revaluation (S10).
- `GET /documents`, `GET /documents/{id}` and `GET /stock` (S7). Opening stock and the seed (S5d). Health and rebuild, and INV SQL (S11).
- `IInventoryModule.PostDocument`/`GetBalance` (spec §4 Contracts). Nothing outside Inventory posts yet, and S5d's seeder is inside the module.
- Any UI beyond the five `PROBLEM_MESSAGES` entries.
- Any schema, migration, grant or index change. Any change to `Domain/*`.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/Modules/Inventory/Inventory/Features/Posting/PostReceipt/Request.cs` | C | Receipt DTO (nullable fields, so missing → `400 validation`) | R1, R3 | 01 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostIssue/Request.cs` | C | Issue DTO | R2, R3 | 01 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostingValidation.cs` | C | Pure step-1 checks, normalization, D1 line-total guard, `Idempotency-Key` header check; returns `ValidatedPosting` | R3, R4 | 01 |
| `src/Modules/Inventory/Inventory/Features/Posting/RequestHash.cs` | C | Canonical JSON + SHA-256 | R4 | 01 |
| `tests/Inventory.UnitTests/PostingValidationTests.cs` | C | Every step-1 rule and error key | R3, R10 | 01 |
| `tests/Inventory.UnitTests/RequestHashTests.cs` | C | Canonical form: decimals, null vs absent, trimming, order, route | R4, R10 | 01 |
| `web/src/app/core/problems/problem-messages.ts` | M | Five slugs | R9 | 02 |
| `web/src/app/core/problems/problem-messages.spec.ts` | M | Map test for the five | R9 | 02 |
| `docs/Inventory System — Sprint 1 Spec.md` | M | §6 step 1 (D1 line total and step 8 balance range), step 1/2 order (D2), §15 Log line | R9 | 02 |
| `src/Shared/Kernel/Problems/ProblemTypes.cs` | M | Five slug constants | R9 | 03 |
| `src/Modules/Inventory/Inventory/Features/Problems.cs` | M | Factories: `IdempotencyKeyMissing`, `IdempotencyKeyReused(id)`, `InsufficientStock(…)`, `InactiveItem(id)`, `InactiveLocation(id)` | R2–R4, R9 | 03 |
| `src/Modules/Inventory/Inventory/Persistence/PostingCommand.cs` | C | `PostingCommand` input and the `PostingOutcome` hierarchy | R5 | 03 |
| `src/Modules/Inventory/Inventory/Persistence/PostingEngine.cs` | C | Step 2 lookup, steps 3–10, the race replay, D1 balance guard, 3-retry exhaustion | R4, R5, R6 | 03 |
| `src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs` | M | Retry config (3, `DisableRetry`); register `PostingEngine` (03). Register handlers and map `/documents` (04, 05) | R5, R1, R2 | 03, 04, 05 |
| `src/Modules/Inventory/Inventory/Features/Posting/DocumentResponse.cs` | C | D3 response records and their mapping | R8 | 04 |
| `src/Modules/Inventory/Inventory/Features/Posting/DocumentPoster.cs` | C | Shared flow: header → validate → hash → step 2 → DB step-1 → engine → HTTP result | R1–R5, R8 | 04 |
| `src/Modules/Inventory/Inventory/Features/Posting/DocumentEndpoints.cs` | C | `/documents` group | R1, R2 | 04, 05 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostReceipt/Endpoint.cs`, `Handler.cs` | C | Receipt route and handler | R1 | 04 |
| `src/Modules/Inventory/Inventory/Features/Posting/PostIssue/Endpoint.cs`, `Handler.cs` | C | Issue route and handler | R2 | 05 |
| `tests/Inventory.IntegrationTests/TenantIsolationTests.cs` | M | T5 probes for both routes | R7 | 04, 05 |
| `tests/Inventory.IntegrationTests/PostingTests.cs` | C | Receipt and issue behaviour: shapes, validation, roles, inactive, replay, reused key, insufficient stock, key not consumed | R1–R4, R8 | 04, 05 |
| `tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj` | M | `Microsoft.Extensions.TimeProvider.Testing` (allowed list) | R10 | 06 |
| `tests/Inventory.IntegrationTests/ApiFactory.cs` | M | Optional `Clock` (`TimeProvider`) override through `ConfigureTestServices` | R6, R10 | 06 |
| `tests/Inventory.IntegrationTests/PostingVectorTests.cs` | C | T1 API run of set R | R10 | 06 |
| `tests/Inventory.IntegrationTests/PostingConcurrencyTests.cs` | C | T2, T4, step-7 deactivation race (commit → `422`, rollback → `201`) | R4, R5, R10 | 06 |
| `tests/Inventory.IntegrationTests/PostingClockTests.cs` | C | T10 | R6, R10 | 06 |

Review constraints check:
- Decimal everywhere, `Round4` from `CostingPolicy`. ✓
- The engine only `INSERT`s documents and movements. ✓
- The ledger lock resource uses `ITenantContext.TenantId`, and no request carries a tenant. No `IgnoreQueryFilters`. ✓
- `PostedAt` = `TimeProvider`. ✓
- Runs as `inventory_app`, using existing grants only. ✓
- One package, on the allowed list. ✓
- §6 changed with the user's approval (D1, D2). No new columns. ✓

## Blast radius
**Risk:** high. This is the first code that writes the ledger. It touches `Features/Posting/**`, `Persistence/**` and Kernel (Shared code), and it changes the retry count for every Inventory transaction.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| Items/Locations handlers using `CreateExecutionStrategy` (`UpdateItem`, `DeactivateItem`, `DeactivateLocation`) | Direct | Retry config in `InventoryModuleExtensions` | No. They get 3 retries instead of 6. Their tests don't count retries | `dotnet test` in 03 |
| `InventoryMigrator`, `InventorySeeder`, `InventoryModule.ProvisionTenantAsync` | Direct | Same context options | No. Migrate and seed run once; fewer retries only matters on a flaky start-up | AppHost run unchanged; existing `SetupTests` |
| `Platform` DbContexts | Non-graph | Their own `Enrich…` | No. Only the `InventoryDbContext` is reconfigured | — |
| `Kernel.Problems.ProblemTypes` (Shared code) | Direct | Five new constants | No, additive. Platform and Api read only existing ones | — |
| `Features/Problems.cs` | Direct | New factories | No, additive | — |
| T5 route guard `TenantIsolationTests.cs:325` | Non-graph | New `/api` routes | It would fail without probes | Probes land in the same prompt as each route (04, 05) |
| Angular error interceptor map | Hidden coupling | Slugs | It would show the default message | 02 adds all five |
| RequestHash ↔ client | Hidden coupling | S7 post-stock form | No client yet. Server-side canonicalization makes formatting irrelevant | Decision recorded; S7 resends the same values |
| `StockBalances.IsVirtual`/`AllowNegative` ↔ `Locations` | Hidden coupling | Step 5 copies, step 7 re-reads, step 9 writes `AllowNegative` | No: it follows spec §6 | 03 sets both from step 7 |
| Deactivation guards (`HOLDLOCK, FORCESEEK` on balances) | Non-graph | Lock interplay | It can deadlock with a posting that inserts a new balance (IMS-6 Downstream). Both sides retry. With 3 retries, sustained contention could exhaust them → `409` | Accepted per spec; 06 runs the concurrency tests; flag for the audit |
| SKU edit (`HOLDLOCK` on `IX_Movements_Item`) vs first posting's FK S-lock | Non-graph | Lock interplay | Same 1205 pattern, retried | Same |
| `security.TenantPolicy`, triggers, `HasTrigger` | Hidden coupling | No new tables | No | — |
| `DatabasePrincipals` | Non-graph | `sp_getapplock` (public), `sp_sequence_get_range` (UPDATE on sequence, granted) | No | 03 falls back to `NEXT VALUE FOR` if the range call is refused |
| T6/T7/T11 tests, `LedgerRows` | Non-graph | Unchanged tables | No | — |
| T12 architecture tests | Non-graph | New types are `internal`; Domain is untouched; no new public type | No | `dotnet test` |
| `Platform.IntegrationTests` | Transitive | Hosts the same API, including Inventory | No: the routes are additive | `dotnet test` |
| Spec §6 text | Notes | D1, D2 | No code impact | 02 |
| `IX_Balances_Location` (audit) | Non-graph | An alternate seekable path for step 6 | It could change the lock order | `INDEX(PK_StockBalances)` in 03 |
| `inventory.MovementIds` (audit) | Non-graph | One sequence for the database; test classes run in parallel on one container | A "consecutive ids" assertion would be flaky on the fallback path | Assert strictly ascending ids (04) |
| `UX_StockDocuments_IdempotencyKey` collation (audit) | Non-graph | Default case-insensitive collation | Keys differing only by case collide | Decision corrected; no case-sensitivity test |
| `ItemsTests.CreateAsync`, `LocationsTests.CreateAsync` (audit) | Non-graph | Existing shared test helpers | Copying them is bloat | 04 reuses them; `PostingTests`' post helper is `internal static` for 05, 06 |

**Known risks in the touched area:**
- 11719 when `NEXT VALUE FOR` is composed (`SingleAsync`). `LineNo` is reserved.
- `UPDLOCK` works for `inventory_app` despite the deny (IMS-6 Downstream).
- Testcontainers start-up crash: use `dotnet test --max-parallel-test-modules 1`.
- Restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1` on this machine (S5b log).
- The access path, not `ORDER BY`, sets the lock order (spec audit `:31`). The audit should challenge whether the parameterized `IN` seek really acquires locks in key order. T3 (S6) is the real proof.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-8 | S5c Posting engine | Added | 01–06 | IMS-6, IMS-7 | yes: `Features/Posting/**`, `Persistence/**`, Kernel (code-guardian) | See below |

Acceptance criteria for IMS-8:
- **R1:** a receipt of 10 @ 100.00 returns `201`, `Location: /api/documents/{id}`, one movement SUP→L with `totalCost` 1000.0000, and balance L at 10 / 1000.0000 / `lastUnitCost` 100.0000.
- **R2:** after R1, issuing 15 returns `422 /problems/insufficient-stock` with `available` 10 and `requested` 15, and no document is written. Issuing 4 returns `201` with cost 400.0000.
- **R3:** each rule in R3 has a unit test and at least one API test returning the stated key. D1's line total returns `400 errors["lines[0].unitCost"]`.
- **R4:**
  - A missing header → `400 /problems/idempotency-key-missing`.
  - Sequential replay → `200` with `Idempotent-Replayed: true` and the same `id`.
  - A different body → `409` with `originalDocumentId`.
  - The same key on the other endpoint → `409`.
  - A `422` doesn't consume the key: the same key succeeds after stock arrives.
  - A replay after the item is deactivated → `200` (D2).
- **R5:**
  - T2 passes.
  - T4's deterministic race returns `200` when a raw-held same-key document commits, and `201` when it rolls back.
  - Movement ids strictly ascend in `LineNo` order (contiguous only on the `sp_sequence_get_range` path).
  - A deactivation committed while a posting waits at step 7 returns `422 inactive-item` with no document; a rolled-back one returns `201`.
  - Balances carry `LastMovementId`.
- **R6:** T10: with the `FakeTimeProvider` fixed, `postedAt` and the stored `PostedAt` of the document and its movements equal the clock. A body with `postedAt` is ignored. No `Request` type in `Inventory.Features` has a date or time property.
- **R7:** T5 probes pass, and the route guard is green.
- **R8:** a replay body has the same shape as the `201`, with `balancesAsOf` set.
- **R9:** `ProblemTypes` has the five constants, the Vitest map test covers them, and spec §6 carries D1 and D2.
- **R10:** T1 set R through the API matches the costs and balances to 4 dp, including R4's midpoint 10.0067.
- **R11:** every `## Commands` line and CI `build` and `test` are green, and code-guardian has reviewed.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Request contract, step-1 validation, RequestHash | IMS-8 | opus | high | — | A | `PostReceipt/Request.cs`, `PostIssue/Request.cs`, `PostingValidation.cs`, `RequestHash.cs`, `PostingValidationTests.cs`, `RequestHashTests.cs` |
| 02 | Web slugs and spec §6 amendment | IMS-8 | haiku | low | — | A | `problem-messages.ts`, `problem-messages.spec.ts`, spec |
| 03 | PostingEngine, slugs, retry config | IMS-8 | opus | xhigh | 01 | B | `PostingCommand.cs`, `PostingEngine.cs`, `InventoryModuleExtensions.cs`, `ProblemTypes.cs`, `Features/Problems.cs` |
| 04 | Receipts endpoint and shared poster | IMS-8 | opus | high | 03 | C | `DocumentResponse.cs`, `DocumentPoster.cs`, `DocumentEndpoints.cs`, `PostReceipt/{Endpoint,Handler}.cs`, `InventoryModuleExtensions.cs`, `TenantIsolationTests.cs`, `PostingTests.cs` |
| 05 | Issues endpoint | IMS-8 | opus | high | 04 | D | `PostIssue/{Endpoint,Handler}.cs`, `DocumentEndpoints.cs`, `InventoryModuleExtensions.cs`, `TenantIsolationTests.cs`, `PostingTests.cs` |
| 06 | T1 set R, T2, T4, T10, step-7 race | IMS-8 | opus | high | 05 | E | `Inventory.IntegrationTests.csproj`, `ApiFactory.cs`, `PostingVectorTests.cs`, `PostingConcurrencyTests.cs`, `PostingClockTests.cs` |

- **01 opus:** in `Features/Posting/**`. The canonical hash is Hidden coupling, and a canonicalization bug silently breaks idempotency (false `409`s or false replays).
- **03 opus (xhigh):** the core of the ledger: lock order, the applock, document-first idempotency, swallowing 2627 inside a transaction, the sequence range, and the strategy and retry semantics. It also touches Kernel (Shared code).
- **04 opus:** in `Features/Posting/**`. It sets the outcome → HTTP mapping and the D2 ordering that every later document type reuses, and it adds the first T5 probes for posting.
- **05 opus:** in `Features/Posting/**`. The cost-at-average path and the insufficient-stock rollback with the key not consumed. It mirrors 04 closely; the audit may downgrade it to sonnet.
- **06 opus:** the concurrency tests (T2, T4's deterministic race with `WaitUntilBlockedAsync`). Getting them deterministic, rather than flaky, needs SQL Server locking semantics.
- **02 haiku:** two map entries' worth of mechanical edits plus spec prose dictated verbatim. `web/` and `docs/` share no build with .NET, so it runs in group A beside 01.
- Groups B → E run in order, because 01 and 03–06 share the .NET build and test tree (workflow §5).

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
cd web && npm run lint && npm run build && npm test -- --watch=false
cd ..

# R3, R4 (unit): PostingValidationTests, RequestHashTests
dotnet test --project tests/Inventory.UnitTests
# R1–R8, R10: PostingTests, TenantIsolationTests (T5), PostingVectorTests (T1 R), PostingConcurrencyTests (T2, T4, step-7 race), PostingClockTests (T10)
dotnet test --project tests/Inventory.IntegrationTests
dotnet test --project tests/Platform.IntegrationTests
dotnet test --project tests/Architecture.Tests

# Review constraints and module surface
! grep -rnE --include='*.cs' 'UPDATE\s+\[?inventory\]?\.\[?Stock(Documents|Movements)|DELETE\s+(FROM\s+)?\[?inventory\]?\.\[?Stock' src
! grep -rnE --include='*.cs' '\.IgnoreQueryFilters\s*\(\s*\)' src
! grep -rnE --include='*.cs' '\b(double|float)\b' src/Modules/Inventory/Inventory
! grep -rn --include='*.cs' 'TenantId' src/Modules/Inventory/Inventory/Features
! grep -rnE --include='*.cs' '^\s*public (sealed |static )?(class|record|interface|enum)' src/Modules/Inventory/Inventory/Features src/Modules/Inventory/Inventory/Persistence --exclude-dir=Migrations
! grep -rnE --include='*.cs' 'Guid\.CreateVersion7|DateTime(Offset)?\.(Utc)?Now' src/Modules/Inventory/Inventory
for s in idempotency-key-missing idempotency-key-reused insufficient-stock inactive-item inactive-location; do
  grep -q "\"$s\"" src/Shared/Kernel/Problems/ProblemTypes.cs && grep -q "'$s'" web/src/app/core/problems/problem-messages.ts || echo "MISSING $s"
done                                     # prints nothing

# Full stack smoke: post a receipt and replay it against the dev volume
SCRATCH=$(mktemp -d)
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"
dotnet run --project src/AppHost &
for i in $(seq 90); do [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:4200/api/auth/me)" = 401 ] && break; sleep 2; done
# $SEED_PASSWORD: the AppHost's generated `seed-password` parameter, read into the shell and never echoed
curl -s -c "$SCRATCH/staff" -H 'Content-Type: application/json' -d "{\"email\":\"staff@demo.test\",\"password\":\"$SEED_PASSWORD\"}" \
  http://localhost:4200/api/auth/login -o /dev/null -w '%{http_code}\n'                                    # 200
# pick a physical location and an item from GET /api/locations and GET /api/items, then:
KEY=$(uuidgen)
BODY="{\"locationId\":\"$LOC\",\"reference\":\"smoke\",\"notes\":null,\"lines\":[{\"itemId\":\"$ITEM\",\"quantity\":1,\"unitCost\":1}]}"
for i in 1 2; do curl -s -b "$SCRATCH/staff" -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" -d "$BODY" \
  -D - -o /dev/null http://localhost:4200/api/documents/receipts | grep -iE '^HTTP|Idempotent-Replayed'; done  # 201, then 200 + replayed
kill %1

# R11: after build-unit opens the PR
gh pr checks IMS-8                       # build ✓  test ✓
```

## Rollback
Revert the PR. There's no schema change. Documents and movements posted while it was live stay in the append-only tables. They're valid ledger rows, and S5c's re-landing reads them as they are.

## Open items for the user (bookkeeping, not in this PR)
- TASKS.md still shows IMS-7 as `[~]`, and `_index.md` shows the S5b plan as `in-progress`, though PR #6 merged (`da95a481`). Its Downstream block, at S5b plan `:124-133`, isn't in TASKS.md yet. Mark IMS-7 `[x]`, set S5b to `done` and copy the block before IMS-8 starts.

## Log
- 2026-11-09: planned (D1 overflow guards, D2 replay before database checks, D3 response shape; all three were the user's picks).
- 2026-11-09: audited, 5 fixes applied (step-7 race test, pinned lock paths, ascending ids, key collation, helper reuse) plus 4 notes.
