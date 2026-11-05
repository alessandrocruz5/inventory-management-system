---
type: plan
kind: feature
status: draft
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-05
risk: high
prompts: 4
graph_built: none
tags: [plan]
---
# S5a Ledger schema (IMS-6)

## Request
> IMS-6

This plans the existing unit `IMS-6 — S5a Ledger schema · Added · spec: §12 S5a · depends: IMS-5 · model: opus` (TASKS.md, Sprint 1). It keeps the key, sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units").

Spec §12 S5a: *"Documents, movements, balances (with `AllowNegative`, `LastUnitCost` and `IX_Balances_Location` from the start), sequence, triggers, checks, RLS predicates, `inventory_app` grants. Tests: T7 · code-guardian review."*

The scope also includes the S5a items that IMS-3, IMS-4 and IMS-5 locked in TASKS.md Downstream:
- `DENY UPDATE` and the sequence grant.
- RLS on every new tenant table.
- SKU immutability, the `stock-not-zero` deactivation guards and the slug.

| # | Requirement |
|---|---|
| R1 | `inventory.StockDocuments` exactly as in spec §5. It has a `Type` CHECK, `ReversesDocumentId` set if and only if `Type = 'Reversal'`, and `ReasonCode` on adjustments only. Its keys are a unique `(TenantId, IdempotencyKey)`, a filtered unique `(TenantId, ReversesDocumentId)` and a composite self-FK. |
| R2 | The `inventory.MovementIds` bigint sequence (start 1, increment 1). `inventory.StockMovements` exactly as in spec §5, with `From <> To`, `Quantity > 0 OR (Quantity = 0 AND TotalCost > 0)` and `TotalCost >= 0`. It has a unique `(TenantId, DocumentId, LineNo)`, `IX_Movements_Item (TenantId, ItemId, Id)` and composite FKs to documents, items, locations and itself. |
| R3 | `inventory.StockBalances` exactly as in spec §5. It has PK `(TenantId, ItemId, LocationId)`, `IsVirtual`, `AllowNegative`, `OnHand`/`StockValue` defaulting to 0, `LastUnitCost`, `LastMovementId`, `RowVersion`, `IX_Balances_Location (TenantId, LocationId)`, the two CHECKs and composite FKs to items and locations. |
| R4 | `StockDocuments` and `StockMovements` have `INSTEAD OF UPDATE, DELETE` triggers that `THROW`. EF declares them with `HasTrigger`, and the declared names match the database (Hidden coupling). |
| R5 | The migration that creates the three tables adds each one's FILTER and both BLOCK predicates to `security.TenantPolicy`. `Down` drops the predicates before the tables. Each entity implements `ITenantOwned` through `ConfigureTenantOwned` (IMS-4 Downstream). |
| R6 | Grants: `inventory_app` gets `DENY UPDATE` (and `DELETE`) on `StockDocuments` and `StockMovements`, and `UPDATE` on `inventory.MovementIds`. It keeps no `ALTER` or `CONTROL` (spec §8, IMS-3 Downstream). |
| R7 | T7 (spec §11): the owner's `UPDATE`/`DELETE` on documents and movements hit the triggers. As `inventory_app`, those statements and `DISABLE TRIGGER`, `TRUNCATE TABLE` and `ALTER SECURITY POLICY` are all refused, and the triggers and policy stay enabled. |
| R8 | `PUT /items/{id}` rejects a SKU change once the item has a `StockMovements` row, with `400 validation` and `errors.sku`. A first posting can't slip in between the check and the commit (IMS-5 Downstream). |
| R9 | `POST /items/{id}/deactivate` and `POST /locations/{id}/deactivate` require `OnHand = 0 AND StockValue = 0` on every physical balance. Otherwise they return `422 stock-not-zero` with the blocking balances. Per spec §6 they lock the balance rows `WITH (UPDLOCK, HOLDLOCK)` first (a location's through `IX_Balances_Location`), then check them, then update the row. A racing deactivate still gets `200` (IMS-5 Downstream). |
| R10 | `stock-not-zero` is in Kernel `ProblemTypes` and the Angular `PROBLEM_MESSAGES` map (spec §9 already has it). |
| R11 | Done: every `## Commands` line is green, CI `build` and `test` are green, and code-guardian has reviewed the PR (it touches high-stakes paths). |

## Context used
- Spec `docs/Inventory System — Sprint 1 Spec.md`:
  - §5 `:232-277`: the DDL for documents, the sequence, movements and balances.
  - §5 `:280-289`: the rules. Composite FKs with `TenantId`; the session default on `TenantId`; average cost is never stored; virtual balances track quantity only; `AllowNegative` mirrors the toggle; triggers plus `HasTrigger`.
  - §6 `:305-326`: step 5 inserts balance rows and step 6 locks them on the clustered key ("Add no index that changes this access path"). The hard rule at `:326` says deactivation locks `WITH (UPDLOCK, HOLDLOCK)`, then checks, then updates.
  - §8 `:494-515` and `:522`: RLS, and the grants (no `UPDATE` on the ledger tables; `UPDATE` on the sequence).
  - §9 `:578-590`: `stock-not-zero` is 422, with "balances that block deactivation".
  - §11 `:629` and `:639`: test isolation (the ledger refuses `DELETE`, so every test makes its own tenant) and T7.
  - §12 `:670`: the S5a row.
- Spec audit `docs/Inventory System — Sprint 1 Spec.audit.md`:
  - `:18` and `:23`: deactivation locks `UPDLOCK, HOLDLOCK` and needs `OnHand = 0 AND StockValue = 0`.
  - `:19`: the two principals.
  - `:31`: the clustered key is the lock path, so add no index that changes it.
  - `:35`: the `IX (TenantId, PostedAt)` NOTE, which is still open.
- TASKS.md Downstream:
  - IMS-3 `:39`: **S5a must `DENY UPDATE`** and grant the sequence.
  - IMS-3 `:43`: `EnrichSqlServerDbContext` turns on retries, so a user transaction runs inside `CreateExecutionStrategy()`.
  - IMS-3 `:44`: each new slug lands in §9, `ProblemTypes` and `PROBLEM_MESSAGES` in the same PR.
  - IMS-4 `:50-62`: `ITenantOwned`, `ConfigureTenantOwned`, the predicates in the creating migration, T6 coverage, raw SQL through EF, and the policy being schema-bound.
  - IMS-5 `:65-69`: the S5a guards, the slug, and the racing deactivate that still returns `200`.
- Code:
  - `src/Modules/Inventory/Inventory/Persistence/InventoryDbContext.cs`: `:24-43` is the `Location` config with `HasCheckConstraint` and enum-to-string, `:45-58` is `Item`, and `:61-65` is `ConfigureTenantOwned`. It has no `ConfigureConventions` and no decimals yet.
  - `Persistence/Location.cs` and `Persistence/LocationKind.cs` show the entity and enum style.
  - `Persistence/Migrations/20261105132515_Items.cs:43-60` is the predicate Up/Down pattern.
  - `.editorconfig:391-393` marks migrations `generated_code`.
- Code:
  - `src/Api/Setup/DatabasePrincipals.cs:17-31`: the `EnsureSql` grants, with a comment that S5a adds the rest.
  - `src/Api/Setup/MigrateCommand.cs:22-31`: principals are ensured *after* every migration, so the ledger tables exist when `DENY` runs.
- Code:
  - `Features/Items/UpdateItem/Handler.cs:38` and `DeactivateItem/Handler.cs:21-41`, and `Features/Locations/DeactivateLocation/Handler.cs:21-46`, hold the `S5a` placeholders and the reload-on-conflict path.
  - `Features/Problems.cs:13-23` and `src/Shared/Kernel/Problems/ProblemTypes.cs` are where the new problem goes.
  - `InventoryModuleExtensions.cs:31-35` (`EnrichSqlServerDbContext`, so retries are on). `grep BeginTransaction|ExecutionStrategy src tests` finds nothing, so these are the repo's first user transactions.
- Tests:
  - `tests/Inventory.IntegrationTests/RowLevelSecurityTests.cs`: `:29-32` (`TenantTables` picks up any table with `TenantId`), the three catalog tests at `:141-142`, `:174-175` and `:200-201`, and `:227-237` (the raw `inventory_app` connection to copy).
  - `MigrationRollbackTests.cs:32-59` rolls back to `Initial`, so it runs the new `Down`.
  - `TenantInterceptorTests.cs:81-98` requires every `TenantId` entity to be `ITenantOwned` with the filter and default.
  - `ItemsTests.cs:358-402` holds the concurrent-deactivate test and the `AddItemsAsync` helper. `LocationsTests.cs:250-266` holds the concurrent-deactivate test.
  - `tests/Platform.IntegrationTests/SetupTests.cs:63-105` checks schema-level grants (`class = 3`) only.
- Web: `web/src/app/core/problems/problem-messages.ts:2-8` and `problem-messages.spec.ts:36-43`.
- Graph: none (`graphify-out/` is absent). The blast radius below comes from grep.

## Decisions
- **One unit, IMS-6.** The guards (R8–R9) ride with the schema, because IMS-5 locked them to S5a and they need the tables. Spec §12 allows one PR per slice.
- **Tables are EF entities**, configured like `Location`: `HasCheckConstraint`, enum-to-string and `ConfigureTenantOwned`. The sequence is `HasSequence`. Triggers and RLS go in `migrationBuilder.Sql`. This keeps `has-pending-model-changes` honest, and S5c+ can read them through EF.
- **Enums live in `Persistence`:** `StockDocumentType` and `AdjustmentReason`, like `LocationKind`. S5b (IMS-7) builds `Domain/**` in parallel, and S5a stays out of it.
- **Every FK is `DeleteBehavior.NoAction`.** SQL Server refuses an `INSTEAD OF DELETE/UPDATE` trigger on a table that has a cascading FK, and EF cascades required FKs by default. Nothing is hard-deleted anyway.
- **FKs** (all composite, leading with `TenantId`, spec §5):
  - Movements → Documents, Items, Locations (From and To) and Movements (`ReversesMovementId`).
  - Documents → Documents (`ReversesDocumentId`).
  - Balances → Items and Locations.
  - There's no FK for `LastMovementId` or `PostedBy`. The first isn't in the sketch, and the second would cross into another module's schema.
- **`ConfigureConventions`:** `Properties<decimal>().HavePrecision(19, 4)` sets the review constraint once. `Conventions.Remove<ForeignKeyIndexConvention>()` keeps EF from adding three unread NC indexes (From, To, `ReversesMovementId`) to the hottest insert table. The §5 sketch is the full index list. `Items` and `Locations` have no FKs, so their model doesn't change.
- **Grants go in `DatabasePrincipals`**, not the migration. The user doesn't exist yet on a fresh database's first migration, and `MigrateCommand` ensures principals after migrating. It uses `DENY UPDATE, DELETE` on both ledger tables. `DELETE` is belt and braces: a later schema-level `DELETE` grant must not reach the ledger. It adds an explicit `GRANT UPDATE` on the sequence, as §8 says, even though the schema grant already covers it.
- **The trigger error is `THROW 51000`**, one trigger per table, named `TR_StockDocuments_AppendOnly` and `TR_StockMovements_AppendOnly`. A test compares EF's declared triggers with `sys.triggers`, which turns the Hidden-coupling line into a check.
- **SKU check (R8) takes `WITH (HOLDLOCK, INDEX([IX_Movements_Item]))` inside a transaction**, only when the SKU actually changes (ordinal). The serializable range lock blocks a first movement insert for that item until the edit commits. It deliberately omits `UPDLOCK`, because `inventory_app` is denied `UPDATE` on `StockMovements`, and a shared range lock is enough to block inserts.
- **Deactivation (R9)** runs in `CreateExecutionStrategy().ExecuteAsync` with `ChangeTracker.Clear()` and a transaction. The order is: read the row (404 and already-inactive checks), lock the balances, check, update, commit. Items lock through the clustered PK prefix `(TenantId, ItemId)`. Locations lock `WITH (UPDLOCK, HOLDLOCK, INDEX([IX_Balances_Location]))`. The S4 reload-on-conflict path stays as it is.
- **The `stock-not-zero` body** has a `balances` extension: a list of `{ itemId, locationId, onHand, stockValue }`, ordered by the clustered key.
- **Lock tests are deterministic.** A raw `inventory_app` transaction holds an uncommitted row. The test polls `sys.dm_exec_requests` (as `sa`) until the API request is blocked by that session, then commits. Without the lock hint, RCSI would let the request finish early with the wrong answer, so the test fails.

**Assumptions** (for `/audit-plan` to challenge):
- `ReasonCode` is **required** on an `Adjustment` and `NULL` on every other type, including the reversal of an adjustment. The CHECK is `([Type] = 'Adjustment' AND [ReasonCode] IS NOT NULL AND [ReasonCode] IN (…)) OR ([Type] <> 'Adjustment' AND [ReasonCode] IS NULL)`. `IS NOT NULL` is needed because a CHECK passes on UNKNOWN. §5 says "adjustments only", and §9/§10 always send a reason. This is stricter than the bare sketch, but it adds no column.
- `stock-not-zero` lists every blocking balance with no cap. Sprint-1 tenants are small shops, so a location's list is at most a few hundred rows.
- A case-only SKU edit (`ab-1` → `AB-1`) counts as a change and is refused after the first movement. The stored label changes.
- `INSTEAD OF` triggers and RLS BLOCK predicates can coexist on one table. Prompt 01 stops and reports if the migration refuses it.
- `StockDocuments.Id` stays `ValueGeneratedOnAdd`, a client-side sequential GUID. S5c's raw-SQL path must supply it (see Downstream).

**Non-goals:**
- `PostingEngine` and the document endpoints (S5c).
- `IInventoryModule.PostDocument`/`GetBalance` (S5c).
- Costing (S5b).
- `IX StockDocuments (TenantId, PostedAt)` (S7, if `GET /documents` needs it).
- An FK or index on `LastMovementId`.
- A unique index on `ReversesMovementId`. The filtered unique index on documents already guarantees one reversal per document.
- Showing the blocking balances in the UI (the screens show `problemMessage()`).
- Disabling the SKU field in the item dialog.
- Health and rebuild (S11).
- The toggle (S10).

## Downstream (to add to TASKS.md under "locked by IMS-6" when it merges)
- **S5c (posting engine):**
  - The tables, the sequence and `HasTrigger` exist. Every document and movement column is final at insert: `inventory_app` is denied `UPDATE`/`DELETE` on both tables, and the triggers throw for the owner.
  - Take movement ids with `NEXT VALUE FOR [inventory].[MovementIds]`; `StockMovement.Id` is `ValueGeneratedNever`.
  - A raw-SQL document insert must supply `Id`, or insert the document through EF inside the strategy's transaction. EF inserts work because the triggers are declared.
  - Don't put `UPDLOCK` on `StockDocuments`/`StockMovements` (the `UPDATE` deny).
- **S5c (lock order):**
  - Deactivation takes the balance locks, then the item or location row.
  - A SKU edit takes a `HOLDLOCK` range on `IX_Movements_Item`, then the item row.
  - A posting's FK check on `Items` takes a shared lock during the movement insert. A SKU edit racing a first posting can therefore deadlock (1205), and the execution strategy retries it. Both sides are already under the strategy. Cover this in S5c's audit.
- **S7:** decide on `IX StockDocuments (TenantId, PostedAt)` for the `GET /documents` date filter (spec audit NOTE `:35`).
- **Tests:** `tests/Inventory.IntegrationTests/LedgerRows.cs` inserts documents, movements and balances as `inventory_app` on a raw connection. It also has `WaitUntilBlockedAsync` for lock tests. S5c+ tests post through the API instead, but can reuse the lock helper.

## Change list
All paths are relative to the repo root. `Inv` = `src/Modules/Inventory/Inventory`, `IT` = `tests/Inventory.IntegrationTests`.

| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `Inv/Persistence/StockDocumentType.cs`, `AdjustmentReason.cs` | C | `Type` and `ReasonCode` enums, stored as strings | R1 | 01 |
| `Inv/Persistence/StockDocument.cs`, `StockMovement.cs`, `StockBalance.cs` | C | Entities, `ITenantOwned` | R1–R3, R5 | 01 |
| `Inv/Persistence/InventoryDbContext.cs` | M | 3 sets, sequence, configs, CHECKs, `HasTrigger`, FKs, conventions | R1–R5 | 01 |
| `Inv/Persistence/Migrations/<ts>_Ledger.cs` (+ `.Designer.cs`) | C | Generated tables, then hand-added predicates, triggers, and `Down` order | R1–R5 | 01 |
| `Inv/Persistence/Migrations/InventoryDbContextModelSnapshot.cs` | M | generated | R1–R3 | 01 |
| `IT/RowLevelSecurityTests.cs` | M | Catalog tests name the 3 tables | R5 | 01 |
| `IT/MigrationRollbackTests.cs` | M | The ledger objects go on rollback and come back on re-up | R5 | 01 |
| `src/Api/Setup/DatabasePrincipals.cs` | M | `DENY UPDATE, DELETE` on the ledger; sequence grant | R6 | 02 |
| `tests/Platform.IntegrationTests/SetupTests.cs` | M | Grants test also asserts the object-level permissions | R6 | 02 |
| `IT/LedgerRows.cs` | C | Raw `inventory_app` helpers: insert rows, wait until blocked | R7–R9 | 02 |
| `IT/LedgerImmutabilityTests.cs` | C | T7 | R4, R6, R7 | 02 |
| `IT/LedgerSchemaTests.cs` | C | CHECKs, unique keys, composite FKs, decimals, sequence, triggers vs `HasTrigger`, EF insert path | R1–R4 | 02 |
| `src/Shared/Kernel/Problems/ProblemTypes.cs` | M | `StockNotZero` | R10 | 03 |
| `Inv/Features/Problems.cs` | M | `StockNotZero(...)` and the `BlockingBalance` record | R9 | 03 |
| `Inv/Features/Items/UpdateItem/Handler.cs` | M | Locked SKU-immutability check, run in the strategy and a transaction | R8 | 03 |
| `Inv/Features/Items/DeactivateItem/Handler.cs` | M | Lock, check, update | R9 | 03 |
| `Inv/Features/Locations/DeactivateLocation/Handler.cs` | M | Lock through `IX_Balances_Location`, check, update | R9 | 03 |
| `IT/ItemsTests.cs` | M | SKU and deactivation guard tests, including the lock tests | R8, R9 | 03 |
| `IT/LocationsTests.cs` | M | Deactivation guard tests, including the lock test | R9 | 03 |
| `web/src/app/core/problems/problem-messages.ts` (+ `.spec.ts`) | M | `stock-not-zero` message | R10 | 04 |

**Review constraints checked:**
- **Decimals:** every decimal column is `decimal(19,4)`, set by convention and asserted by a catalog test. There's no `double` or `float`.
- **No `UPDATE`/`DELETE` on the ledger:** the triggers, the `DENY` and T7 enforce it. Handlers only read the ledger tables.
- **No hard deletes:** `Down` drops only what this migration creates. It's valid only while the ledger holds no real postings (see Rollback).
- **`TenantId` never comes from a request:** the session default and the interceptor set it, and no Features code mentions `TenantId`.
- **No bare `IgnoreQueryFilters()`.**
- **No request carries a date:** `PostedAt` is a column only, and S5c sets it from `TimeProvider`.
- **The API connects as `inventory_app`:** the new grants narrow it.
- **No new packages.**
- **No §6 change, and no column beyond the §5 sketch.** The `ReasonCode` CHECK is stricter but adds no column (Assumption).

## Blast radius
**Risk:** high. It touches `**/Migrations/**`, `Inventory/Persistence/**` and `Shared/Kernel/**`. It extends the schema-bound RLS policy, narrows `inventory_app`'s permissions, and adds the repo's first user transactions and lock hints.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `security.TenantPolicy` | Non-graph | `ALTER SECURITY POLICY … ADD` on 3 tables | No, if Up adds the predicates after `CreateTable`, and Down drops them before `DropTable` (schema-bound) | Prompt 01 orders it, and `MigrationRollbackTests` runs this `Down` |
| T6 catalog tests (3) | Test | `TenantTables` now returns the 3 ledger tables | They fail without the predicates, the session default or `TenantId`-first PK/unique keys | `ConfigureTenantOwned` on each table, all keys lead with `TenantId`, and prompt 01 names the tables in the tests |
| `MigrationRollbackTests` | Test | Rollback to `Initial` runs `Ledger.Down` first | It fails if `Down` drops a table before its predicates | Prompt 01 extends it to assert the ledger is gone, then back |
| `TenantInterceptorTests.Every_tenant_entity_has_the_tenant_filter_and_default` | Test | 3 new `TenantId` entities | Only if one isn't `ITenantOwned` or skips `ConfigureTenantOwned` | Prompt 01 |
| `TenantSaveChangesInterceptor` | Direct | Stamps the new entities on add | No: it's generic over `ITenantOwned`, and every key includes `TenantId` | `LedgerSchemaTests` EF-insert test |
| `TenantSessionInterceptor` / RLS | Direct | Raw SQL in handlers runs on EF's connection (`FromSql`) | No | — |
| `InventoryDbContext` model and snapshot | Direct | New entities, the sequence and `ConfigureConventions` | The FK-index convention removal could change `Items`/`Locations`, but they have no FKs | `has-pending-model-changes` returns none, and the generated migration must touch only the ledger |
| `DatabasePrincipals.EnsureSql` | Direct | `DENY` on objects that must exist | Fails if it runs before the migration, but `MigrateCommand.cs:22-31` migrates first. On a DB rolled back below `Ledger`, the old code has no `DENY` lines | Rollback order (below) |
| `SetupTests.Inventory_app_has_exactly_the_s2_grants` | Test | Checks `class = 3` only | No, the new permissions are `class = 1` | Prompt 02 adds the object-level assertion and renames it `…_exactly_its_grants` |
| `SetupTests` migrate-twice and seed-twice | Test | Migrate reruns `DENY`/`GRANT` | No: both are idempotent | — |
| AppHost `migrate` on the dev volume | Non-graph | Applies `Ledger`, then the new grants | No | E2E run below |
| `UpdateItemHandler` | Direct | Now runs inside the strategy and a transaction | It would throw `InvalidOperationException` if it began a transaction outside the retrying strategy | Prompt 03 wraps it and clears the tracker per attempt |
| `DeactivateItemHandler`, `DeactivateLocationHandler` | Direct | Same, plus lock hints | The S4 `Concurrent_deactivations_all_succeed` tests must still get 8× `200` | They serialize on the locks, then the rowversion reload returns `200`. Prompt 03 keeps both tests unchanged |
| T5 `TenantIsolationTests` route probes | Test | No new routes; deactivate or update with a foreign id returns 404 before any lock | No | — |
| `ItemsTests`/`LocationsTests` (S4) | Test | Unchanged behaviour with no ledger rows | No | Run in full |
| `ProblemTypes` (Kernel, Shared code) | Shared | An additive constant | No | — |
| Angular `PROBLEM_MESSAGES` | Hidden coupling | Must match the Kernel slug | No, if both land in this PR | Prompts 03 and 04 are in the same unit |
| Items and Locations screens | Direct | Show `problemMessage()` on a deactivate error | No | — |
| Item dialog | Direct | `errors.sku` already maps to the field (`problemFieldErrors`) | No | — |
| T12 `Module_implementations_expose_only_their_registration_class` | Test | New Inventory types | Fails on a public type | Every prompt says `internal sealed` / `internal enum`, and migrations stay public under `.Migrations` |
| T12 `Inventory.Domain` rules | Test | Nothing is added to `Domain/` | No | Enums stay in `Persistence` |
| S5c `PostingEngine` (future) | Transitive | Lock order against the guards; FK S-locks | A deadlock is possible on a SKU edit racing a first posting | The execution strategy retries 1205, and the Downstream note puts this in S5c's audit |

**Known risks in the touched area:**
- The policy is schema-bound, so altering a `TenantId` column later means dropping its predicates in the same migration (IMS-4).
- Roll the schema back on this build before reverting the code (IMS-4).
- `HasDefaultValue(0m)` on `OnHand`/`StockValue` makes `0` EF's sentinel. An insert of 0 gets the database default, which is also 0, so this is harmless.
- `FromSql` composed with `Where` puts the hinted `SELECT` in a derived table, and the lock hint applies to the inner table access. `HOLDLOCK` range-locks the whole seek range whatever the outer filter.
- Testcontainers start-up flakes (S4 Log: 2 of about 12 full runs). Prompt 02/03's lock tests have a 10 s ceiling, so they don't add timing flakes.
- Machine prerequisites: `DOTNET_SYSTEM_NET_DISABLEIPV6=1`, the trusted dev cert and `SSL_CERT_DIR`.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-6 | S5a Ledger schema | Added | 01–04 | IMS-5 | yes: `Inventory/Persistence/**` and `**/Migrations/**` (code-guardian) | See below |

Acceptance criteria for IMS-6:
- **R1–R3:** `migrations script Items Ledger` creates the three tables and the sequence with every §5 column, type and CHECK. `LedgerSchemaTests` proves each CHECK, both unique keys on documents, the line key on movements, and a composite FK refusing another tenant's item. `has-pending-model-changes` reports none, and every decimal column is `decimal(19,4)`.
- **R4:** `LedgerSchemaTests` shows that EF's declared triggers equal `sys.triggers` (`INSTEAD OF UPDATE, DELETE`), and that an EF insert of a document, its movements and a balance succeeds as `inventory_app`.
- **R5:** the T6 catalog tests list all three tables with 1 FILTER and 2 BLOCK predicates, a session default and `TenantId`-first keys. `MigrationRollbackTests` shows the ledger dropped and re-created.
- **R6:** the grants test lists `DENY` `UPDATE`/`DELETE` on both ledger tables and `GRANT UPDATE` on `MovementIds`, with no `ALTER`/`CONTROL`.
- **R7:** `LedgerImmutabilityTests` shows the owner's `UPDATE`/`DELETE` → 51000, and `inventory_app`'s `UPDATE`/`DELETE` → 229. `DISABLE TRIGGER`, `TRUNCATE` and `ALTER SECURITY POLICY` are refused with permission errors, and the triggers, the policy and the rows are intact afterwards.
- **R8:** `ItemsTests`:
  - A SKU change after a movement → `400 errors.sku`, with the SKU unchanged.
  - A name edit after a movement → `200`.
  - A SKU change that races an uncommitted first movement blocks, then → `400`.
- **R9:**
  - An item with a non-zero physical balance → `422 /problems/stock-not-zero` with exactly that balance, and the item stays active. Virtual balances and zero physical balances don't block.
  - A negative physical balance blocks.
  - The location equivalents hold.
  - A deactivate that races an uncommitted balance blocks, then → `422` (item and location).
  - The S4 concurrent-deactivate tests still pass.
- **R10:** `ProblemTypes.StockNotZero == "stock-not-zero"`, and the Vitest spec maps it.
- **R11:** every `## Commands` line and CI `build` and `test` are green, and code-guardian has reviewed.

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Ledger tables, sequence, triggers, RLS predicates | IMS-6 | opus | high | — | A | `Persistence/{StockDocumentType,AdjustmentReason,StockDocument,StockMovement,StockBalance}.cs`, `InventoryDbContext.cs`, `Migrations/*Ledger*` (2 + snapshot), `RowLevelSecurityTests.cs`, `MigrationRollbackTests.cs` |
| 02 | Grants, T7 and schema tests | IMS-6 | opus | high | 01 | B | `DatabasePrincipals.cs`, `SetupTests.cs`, `LedgerRows.cs`, `LedgerImmutabilityTests.cs`, `LedgerSchemaTests.cs` |
| 03 | Master-data guards and `stock-not-zero` | IMS-6 | opus | high | 01, 02 | C | `ProblemTypes.cs`, `Features/Problems.cs`, `UpdateItem/Handler.cs`, `DeactivateItem/Handler.cs`, `DeactivateLocation/Handler.cs`, `ItemsTests.cs`, `LocationsTests.cs` |
| 04 | Web: `stock-not-zero` message | IMS-6 | haiku | low | — | A | `problem-messages.ts`, `problem-messages.spec.ts` |

- **01 opus:** it's in `**/Migrations/**` and `Inventory/Persistence/**`. It extends the schema-bound policy, and its triggers, CHECKs and FK delete behaviour are the ledger's database backstop. A wrong `Down` order or a cascading FK breaks the migration or rollback.
- **02 opus:** the `inventory_app` permission boundary and T7. `DENY` vs schema grants, error numbers (229, 1088 vs 4712 for `TRUNCATE` on an FK-referenced table) and the catalog queries need precise SQL Server semantics.
- **03 opus:** `Shared/Kernel/**`, plus concurrency. These are the first user transactions under the retrying strategy, with lock hints that must match spec §6, and deterministic lock tests.
- **04 haiku:** one map entry and one spec case. It runs in group A beside 01, because `web/` shares no build, lockfile or test tree with .NET.
- Groups A → B → C run in order: 01–03 share the .NET build and test tree (workflow §5).

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false
cd ..

# R1–R5: the migration's SQL and model sync
SCRATCH=$(mktemp -d)
dotnet ef migrations script Items Ledger --project src/Modules/Inventory/Inventory --startup-project src/Api \
  --context InventoryDbContext -o "$SCRATCH/s5a.sql"
grep -c 'CREATE TABLE \[inventory\].\[Stock\(Documents\|Movements\|Balances\)\]' "$SCRATCH/s5a.sql"        # 3
grep -n 'CREATE SEQUENCE \[inventory\].\[MovementIds\] AS bigint' "$SCRATCH/s5a.sql"
grep -c 'ADD .*PREDICATE \[security\].\[fn_tenant\](\[TenantId\]) ON \[inventory\].\[Stock' "$SCRATCH/s5a.sql"  # 9
grep -c 'INSTEAD OF UPDATE, DELETE' "$SCRATCH/s5a.sql"                                                       # 2
grep -n 'IX_Movements_Item\|IX_Balances_Location\|UX_StockDocuments_IdempotencyKey\|UX_StockDocuments_Reverses\|UX_StockMovements_DocumentLine' "$SCRATCH/s5a.sql"
! grep -n 'ON DELETE CASCADE' "$SCRATCH/s5a.sql"
! grep -niE '\b(float|real)\b' "$SCRATCH/s5a.sql"
grep -c 'decimal(19,4)' "$SCRATCH/s5a.sql"                                                                   # 5 (Quantity, TotalCost, OnHand, StockValue, LastUnitCost)
dotnet ef migrations has-pending-model-changes --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext   # none

# R1–R10: named tests
dotnet test --project tests/Inventory.IntegrationTests
dotnet test --project tests/Platform.IntegrationTests

# Review constraints and module surface
! grep -rnE --include='*.cs' '\.IgnoreQueryFilters\s*\(\s*\)' src
! grep -rn --include='*.cs' 'TenantId' src/Modules/Inventory/Inventory/Features src/Modules/Inventory/Inventory/Seeding
! grep -rnE --include='*.cs' '^\s*public (sealed |static )?(class|record|interface|enum)' src/Modules/Inventory/Inventory/Features src/Modules/Inventory/Inventory/Persistence --exclude-dir=Migrations
! grep -rnE --include='*.cs' '\b(double|float)\b' src/Modules/Inventory/Inventory
! grep -rnE --include='*.cs' 'UPDATE\s+\[?inventory\]?\.\[?Stock(Documents|Movements)|DELETE\s+(FROM\s+)?\[?inventory\]?\.\[?Stock' src
grep -n 'stock-not-zero' src/Shared/Kernel/Problems/ProblemTypes.cs web/src/app/core/problems/problem-messages.ts   # 2 lines

# Full stack on the existing dev volume: migrate applies Ledger and the grants, and the API still serves
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"
dotnet run --project src/AppHost &      # dashboard: migrate Finished(0), seed Finished(0), api Healthy, web Running
for i in $(seq 90); do [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:4200/api/auth/me)" = 401 ] && break; sleep 2; done
# $SEED_PASSWORD: the AppHost's generated `seed-password` parameter, read into the shell and never echoed
curl -s -c "$SCRATCH/admin" -H 'Content-Type: application/json' -d "{\"email\":\"admin@demo.test\",\"password\":\"$SEED_PASSWORD\"}" \
  http://localhost:4200/api/auth/login -o /dev/null -w '%{http_code}\n'                                    # 200
curl -s -b "$SCRATCH/admin" 'http://localhost:4200/api/items?pageSize=1' -o /dev/null -w '%{http_code}\n'  # 200
kill %1

# R11: after build-unit opens the PR
gh pr checks IMS-6                       # build ✓  test ✓
```

## Rollback
The order matters, as in IMS-4 and IMS-5. `Ledger.Down` exists only in the IMS-6 build.
1. On the IMS-6 build, roll the dev database back first. As `sa`, run `dotnet ef database update Items --project src/Modules/Inventory/Inventory --startup-project src/Api --context InventoryDbContext --connection "<sa connection string>"`. `Down` drops the nine predicates, then the three tables (with their triggers and object permissions), then the sequence. Alternatively, drop the AppHost's SQL volume.
2. Then revert the IMS-6 merge commit on `develop`. The old `DatabasePrincipals` has no `DENY` lines, so `migrate` still succeeds.
- This path is valid only while the ledger holds no real postings. S5a ships no posting path, so the tables hold test rows at most. After S5c merges, a rollback is a forward fix, never a `Down` (no hard deletes).

## Open questions
- None blocking. The Assumptions above are for the audit.

## Log
- 2026-11-05: planned (re-plan of IMS-6 from spec §12 S5a and the IMS-3/4/5 Downstream notes; base `develop` @ `f1ef4512`, clean). No clarification round: no gap changed the design. Gaps are under Assumptions.
