# Audit: Inventory System — Sprint 1 Spec

Target: [[Inventory System — Sprint 1 Spec]]. It is a human-written spec, not a `/plan-feature` plan, so it has
no Change list, prompts, `files` or models. The repo is greenfield (LICENSE and README only), so the audit
reviews the design against itself, its acceptance vectors and the stack's real defaults. `L<n>` means a line
in the spec.

## Audit 2026-11-04: BLOCK
Graph built: none (greenfield, no source) · Symbols traversed: 0 · Missed blast-radius items: n/a

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|
| BLOCK | Breakage | **Idempotency breaks under the exact race T4 tests.** The document row (which holds the unique key) is inserted at step 7, after the stock check at step 6. Two parallel requests with the same key serialize on the balance locks. The second then sees the stock the first consumed and returns `422 insufficient-stock` (or `409 reversal-not-latest` for a reversal) instead of a `200` replay. A UI double-click on the last unit shows an error for a post that succeeded. | L257–263 (step order); L513 (UI reuses the key on double-click); L536 (T4 parallel) | Insert the `StockDocuments` row immediately after BEGIN, before step 4. A parallel duplicate then blocks on the unique-index key until the first commits, gets 2627/2601, and replays. If the first rolls back, the duplicate proceeds. The row must be final when inserted, because the trigger forbids UPDATE. Add a T4 case: two parallel same-key issues of the last unit → one `201` and one `200` replay. Add the same case for reversals. |
| BLOCK | Breakage | **Negative-stock costing produces negative movement costs.** With the toggle on, an estimate higher than the next receipt's price leaves stock on hand with negative value (1 / −70). Every later outflow costs below zero, which violates `CHECK (TotalCost >= 0)`. The result is a 500, and the item can't leave that location until a larger receipt arrives. The positive-adjustment default cost (the "current average") is also negative. | Ref script, counterexample X: `X4 issue 1 -> cost -70.0000 -> VIOLATES CHECK`; L208, L306–308, L350 | Decide before S5, because the fix touches the `StockMovements` CHECK. **Recommended:** when an inflow lifts `OnHand` from below zero to zero or above, set the value to `Round4(newOnHand × inflowUnitCost)` and post the estimate variance as a zero-quantity revaluation movement (physical ↔ Adjustment) in the same document. Relax the CHECK to `Quantity > 0 OR (Quantity = 0 AND TotalCost > 0)`. INV-1, INV-2 and INV-4 still hold, and the N7 residual disappears. Rewrite set N (N4 becomes 7 / 392 with an ₱18 revaluation) and add X as a vector. **Alternative:** move S10 to sprint 2, keep the columns, and redesign it there. |
| BLOCK | Notes & constraints | **PostingEngine can't be both pure Domain and the transaction runner.** §4 puts `PostingEngine` in `Domain/` with "no EF, no SQL", and T12 enforces that. §6 has it run EF's execution strategy, transactions, `UPDLOCK` SQL and sequences. Following the spec literally either fails T12 or leaves the S5 executor to improvise the structure of the most important file. | L128, L144, L148 vs L254–263 | Split it in two. `Domain/PostingRules` stays pure: validation, `CostingPolicy`, the balance math and the reversal rules. `Persistence/PostingEngine` (or a `Features/Posting` handler) owns the transaction, the locks and the SQL, and calls the pure rules. T12 asserts that `Domain` references nothing outside the BCL. |
| FIX | Breakage | **Rebuild races live postings under RCSI.** The `GROUP BY` reads a snapshot. A posting that commits between that read and the `UPDATE` gets overwritten, so the tool meant to repair the projection corrupts it and reports a false diff. | L239 (RCSI on), L287 | Inside the rebuild transaction, take a per-tenant `sp_getapplock` in Exclusive mode, with posting taking it in Shared mode. Alternatively, lock the tenant's balances `WITH (UPDLOCK, HOLDLOCK)` first and read movements `WITH (READCOMMITTEDLOCK)`. Extend T9: run the rebuild concurrently with postings, then health must pass. |
| FIX | Breakage | **`LastUnitCost` is history-dependent state that the ledger can't reproduce.** (a) The claim "no costing replay needed" is false for LUC. (b) Reversals restore `OnHand`/`StockValue` but not LUC: the spec's own V7 leaves B at LUC 123, not 120, and a reversed typo receipt (6 @ 1,250) keeps estimates at ₱798. (c) A movement row can't recover "that inflow's unit cost" (0.5 @ 0.0001 → 0.0002). (d) No invariant checks LUC. | Ref script: V7 `LUC=123.0000`; L `estimated cost 798.0000`; `TotalCost/Quantity=0.0002`; L222, L287, L311 | Define LUC in ledger terms: after an inflow, `OnHand > 0 ? Round4(StockValue/OnHand) : Round4(TotalCost/Quantity)`. Every existing vector stays the same. A reversal recomputes LUC from the latest surviving inflow, rebuild recomputes it with a windowed replay, and health adds an INV-5 check for LUC. |
| FIX | Breakage | **Stale snapshot reads defeat the toggle and deactivation guards.** Step 1 checks `IsActive` before the transaction starts, and step 4 copies `AllowNegative` from a snapshot read. A concurrent toggle-off or deactivation can therefore leave a new balance row marked `AllowNegative = 1` at a location whose toggle is now off. That disables the CHECK backstop for the row, and it lets stock post to an inactive item or location. | L219, L236, L256–259, L271–272 | After step 5, re-read the document's Items and Locations rows `WITH (READCOMMITTEDLOCK)` and validate again. Toggle-off and deactivation lock the location's balance rows `WITH (UPDLOCK, HOLDLOCK)` before checking them, which needs `IX (TenantId, LocationId)`. |
| FIX | Notes & constraints | **The app connects as `sa`, so both database guarantees are bypassable.** Aspire's SQL resource defaults to `User ID=sa`. `sa` can `ALTER SECURITY POLICY … STATE = OFF`, `DISABLE TRIGGER` and `TRUNCATE`. That makes "triggers enforce this even against hand-written SQL" and "neither layer trusts the other" false. | Aspire `SqlServerServerResource.cs:13,50` (`DefaultUserName = "sa"`); L270, L374 | Use two principals. The migrator runs as owner. The app user gets DML on `inventory`/`platform`/`identity`, `EXECUTE` and `SELECT` on the sequence, and no `ALTER`, `CONTROL` or `TRUNCATE`. The seed creates the app user. T6, T7 and the API tests connect as the app user, and T7 also tries `DISABLE TRIGGER` and `TRUNCATE`. |
| FIX | Breakage | **RLS covers only the tables that exist in S3.** Items arrive in S4 and the ledger tables in S5, but neither slice says to add predicates. Without them, the ledger ships without layer 2. | L409, L568–570 | Every migration that creates a tenant table runs `ALTER SECURITY POLICY … ADD FILTER/BLOCK` in the same migration. Add a catalog test: every table with a `TenantId` column has one FILTER and two BLOCK predicates in `sys.security_predicates`. |
| FIX | Breakage | **DbContext pooling can leak tenants.** Aspire's `AddSqlServerDbContext` registers a pooled context, so a scoped `ITenantContext` injected into it, or into a singleton interceptor, keeps the first request's tenant. T5 only tests A probing B's ids. It doesn't detect B seeing A's data. | Aspire `AspireSqlServerEFCoreSqlClientExtensions.cs:61` (`AddDbContextPool`); L392, L413, L538 | Use `AddDbContext` plus `EnrichSqlServerDbContext`, or set the tenant on each lease. Interceptors resolve the tenant per connection open from the request's services. Make T5 symmetric (each tenant lists every collection and sees zero foreign rows), and alternate A, B, A in one host. |
| FIX | Breakage | **Raw SQL and Dapper bypass two stated mechanisms.** EF's `DbConnectionInterceptor` fires only on connections EF opens, so Dapper opening its own connection sets no session context and gets empty results. The `SaveChanges` interceptor doesn't run on raw-SQL inserts either, so the posting engine's inserts have no `TenantId`. Dapper isn't on the allowed package list. | L152, L394, L416, L42 | Give every tenant column `DEFAULT CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)`, with the BLOCK predicate still guarding it. Raw SQL runs only after `Database.OpenConnectionAsync()`, on EF's connection. Either name Dapper in §2 or drop it. |
| FIX | Breakage | **Reversal and deactivation guards are incomplete.** (a) The latest-only check isn't placed under the step 5 locks, so a racing post breaks exactness. (b) If the toggle was turned off after the original, a reversal can restore a negative balance and hit the CHECK, returning a 500 instead of a 422. (c) Reversals into inactive locations aren't covered. (d) Deactivation checks only `OnHand`, so the N7 state (0 / 2.8572) is orphaned value. | L271, L276–278, L353, L366 | Run the reversal checks after step 5 and through step 6's stock check. State that reversals obey the inactive rules. Deactivation requires `OnHand = 0 AND StockValue = 0`. |
| FIX | Usability | **Validation gaps return 500s, and adjustment cost is ambiguous.** No from ≠ to check exists above the DB CHECK. There's no upper bound on quantity or cost, so `decimal(19,4)` overflow (error 8115) returns a 500. "Holds none" is undefined when `OnHand < 0`. The default adjustment cost can be computed two ways (30.0200 vs 30.0201), and no vector covers adjustments. | Ref script: `30.0200` vs `30.0201`; L211, L250, L256 | Add to step 1: from ≠ to; `0 < qty ≤ 999,999,999.9999`; `0 ≤ unitCost ≤ 999,999,999.9999` (product fits too). Unit cost is required when `OnHand ≤ 0`. The default cost is `Round4(qty × StockValue / OnHand)`. Add vector set A (positive and negative adjustment lines, including the default-cost case). |
| FIX | Usability | **Idempotency edges are undefined.** (a) The "canonical body" for `RequestHash` isn't specified (property order, `10` vs `10.0`, null vs missing). (b) If the first post succeeds but the response is lost and the user edits the form, the reused key returns `409` on every retry, and the UI only rotates the key after a success. | L194, L513 | Hash the validated DTO, re-serialized with fixed property order and normalized decimals, plus method and route. On `idempotency-key-reused`, the UI links `originalDocumentId` and rotates the key. State that only successful posts consume a key. |
| FIX | Usability | **The stock grid totals only the current page.** Lists are paged (max 100), so a client-side totals row is wrong on any later page and drifts from the API in floating point. | L426, L499, L598 | `GET /stock` returns decimal totals for the whole filter. The grid shows the server's totals. |
| FIX | Unit shape | **The slices don't each deploy on their own.** (a) S2 seeds virtual locations, but the `inventory.Locations` table arrives in S4. (b) `GET /stock`, `GET /documents`, `GET /documents/{id}` and the balance part of `GET /items/{id}` belong to no slice. (c) The CI `test` job belongs to no slice (S1 adds `build`, S12 adds `e2e`), so S2–S11 tests never run in CI. (d) Branch protection waits until S12. (e) S5 is roughly 40 files: schema, triggers, sequence, CostingPolicy, engine, two endpoints, seed and five named tests. | L566–577 | (a) S2 creates the Inventory DbContext and the Locations table with virtual kinds only, or virtual-location seeding moves to S4. (b) Assign those endpoints to S7 (and S8). (c) S1 ships the `build` and `test` jobs. (d) Protect `main` right after S1. (e) Split S5 into S5a schema + triggers + RLS predicates (T7), S5b CostingPolicy + vectors V/R (pure, can start early), S5c engine + receipts/issues + idempotency (T1 API run, T2, T4, T10) and S5d seed opening stock. |
| FIX | Usability | **Tenant provisioning has no defined path.** Platform creates tenants, but `IInventoryModule` exposes only `PostDocument` and `GetBalance`. The seed and tests have no request, so nothing fills `ITenantContext`. Because the session context is `@read_only = 1`, the seed can't switch tenants on one connection. "Running the seed twice changes nothing" has no mechanism behind it. | L126, L238, L413–414, L588 | Add `IInventoryModule.ProvisionTenant(tenantId)` and an `ITenantScopeFactory.BeginScope(tenantId)` that opens a new DI scope and connection. Seed opening stock with deterministic idempotency keys (`seed:<tenant>:<sku>:<loc>`), so a re-run is a replay. Look tenants up by a fixed id. |
| FIX | Notes & constraints | **Conflicts with the plan → audit → build workflow.** (a) §2 has no profile headings: no `## Commands` (build-unit stops without them), Jira key, Delivery, High-stakes paths or Hidden coupling. (b) "Update TASKS.md/CHANGELOG.md in every PR" contradicts workflow §8 (bookkeeping never goes in a unit PR). (c) Tagging `v0.1.0` at S9 contradicts the sprint ending at `0.1.0` after S12 and §8's roll-up. (d) The code-guardian list omits S2 (auth), S6 (lock ordering), S9 (ledger writes) and S11 (overwrites balances). (e) No slice has prompts or a model. | L32–70, L69, L581, L602; workflow.md §1, §6, §8 | Add the profile in Appendix B to §2. Drop the per-PR ledger rule and tag `0.1.0` when the sprint closes. Use the High-stakes globs, which cover S2, S3, S5, S6 and S9–S11. Run `/plan-feature` per slice with this spec as its notes. |
| FIX | Notes & constraints | **The stack implies unnamed packages and wrong defaults.** `ng lint` needs angular-eslint, which isn't named, so S1 stops at "ask before you add". Also unnamed: Scalar.AspNetCore, Aspire.Hosting.JavaScript, Aspire.Microsoft.EntityFrameworkCore.SqlServer, Identity.EntityFrameworkCore and Dapper. Aspire defaults to the SQL Server `2022-latest` image, but the spec mandates 2025, and Testcontainers needs an explicit image too. | Aspire `SqlServerContainerImageTags.cs` (`Tag = "2022-latest"`); L46–51, L554 | List the exact allowed packages in §2. Use `.WithImageTag("2025-latest")` in the AppHost and pin the same image in Testcontainers. Use `npx ng` or npm scripts in CI, since there's no global `ng`. |
| NOTE | Optimization | `ORDER BY` doesn't control lock acquisition order. The access path does: the clustered key `(TenantId, ItemId, LocationId)` makes it work. Step 4's first-touch inserts take X locks before the ordered lock, so A→B and B→A can still deadlock the first time a balance is touched. | L259–260 | Run ensure-rows in key order (or in its own short transaction once the earlier race fix is in). Keep the clustered PK as the lock path, and add no index that changes it. |
| NOTE | Usability | A replay can't return "the resulting balances" as of the original post, because balances have moved since. | L474 | Define the replay body as the document and its movements, plus current balances labelled as current. |
| NOTE | Breakage | Setting `READ_COMMITTED_SNAPSHOT ON` waits for every other connection to the database, including idle pooled ones. | L239 | Use `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE`. |
| NOTE | Breakage | `PostedBy`/`CreatedBy` are `uniqueidentifier`, but the default Identity key is `string`. | L163, L195 | Use `IdentityUser<Guid>` and `IdentityRole<Guid>`. |
| NOTE | Optimization | Two new lookups have no index: documents filtered by date, and stock filtered by location or updated by the toggle. | L198, L225, L446, L448 | Add `IX StockDocuments (TenantId, PostedAt)` and `IX StockBalances (TenantId, LocationId)`. |
| NOTE | Notes & constraints | The bans are confirmed: FluentAssertions 8.11, MediatR 14.2 and AutoMapper 16.2 ship file licences, MassTransit 9.2 a massient.com licence, and PrimeNG 22 "SEE LICENSE". The CI check misses transitive dependencies. | registry queries; L557 | Use `dotnet list package --include-transitive --format json` and `npm ls --all --json`. |
| NOTE | Notes & constraints | "The food pack needs nothing but new virtual kinds" is optimistic. Production rolls input costs up into the output line (a cross-line dependency the per-line engine lacks) and adds values to the `Type`/`Kind` CHECKs. | L28, L188, L262 | Record both changes as known sprint 2 contract work. |
| NOTE | Usability | Migration files EF generates often fail `dotnet format --verify-no-changes`. | L554 | Mark `**/Migrations/*.cs` as `generated_code = true` in `.editorconfig`. |
| NOTE | Notes & constraints | Showing a lockout message only for real accounts reveals which emails exist. Another tenant's ids in request bodies (not routes) have no stated behaviour. | L388, L498 | Show the same message for every failure. Treat foreign body ids as non-existent (`400 validation`) and cover them in T5. |

**Verified correct:** vector sets V, R and N reproduce to the fourth decimal, and R4 under banker's rounding
gives 10.0066 as the spec says. The posting path's lock-then-compute shape, the latest-only reversal rule, the
composite tenant keys, the filtered unique indexes and the T-test list hold up.

**Bloat estimate:** planned ~220 files / ~12k LOC across 14 slices (≈60 Angular, ≈120 .NET, ≈40 tests);
minimal viable ~210 / ~11.5k. The scope fits the goal. Cuttable: `IPlatformModule` (no consumer) and
`StockBalances.RowVersion` (no reader; posting uses `UPDLOCK`).

**Model cost (per slice, once planned):** `opus` for S2 (auth), S3 (tenancy), S5a/S5c/S5d (ledger), S6 (locking), S9
(reversal), S10 (costing), S11 (rebuild). `opus` for S5b too, since it is the money math, even with vectors.
`sonnet` for S1, S4, S7, S8, S12, S14. `haiku` for S13 (compose output), and for doc-only prompts.

**Applied 2026-11-04.** The user approved the fixes with option A (revaluation movement) for the
negative-stock BLOCK. All 3 BLOCK and 15 FIX findings are resolved in the spec (see its section 15). Applied
from the NOTEs: lock order (step 6), `IX_Balances_Location`, foreign ids in bodies. Still open: RCSI
`WITH ROLLBACK IMMEDIATE`, `IdentityUser<Guid>`, `IX (TenantId, PostedAt)`, the replay body, transitive
banned-package checks, the food-pack contract work, migration formatting, the lockout message. The revised
vectors (N rewritten; A and X new) were recomputed with a second reference script: value is conserved, and no
balance or movement CHECK fires.

### Appendix A: reference replay output (scratchpad `costing_ref.py`, Python `Decimal`, `ROUND_HALF_UP`)

```text
V7 750.0000 4 / 480.0000  LUC=123.0000          <- value restored, LUC not (pre-V6 was 120)
N1..N9 match the spec exactly; R1..R5 match; R4 banker's = 10.0066
X1 receive 4 @ 50   | 4 / 200.0000
X2 issue 6          | -2 / -100.0000  LUC=50
X3 receive 3 @ 10   | 1 / -70.0000    LUC=-70
X4 issue 1  -> cost -70.0000 -> VIOLATES CHECK (TotalCost >= 0)
L  receive 4 @ 120; receive 6 @ 1250; reverse -> 4 / 480 LUC=798; later estimate 798.0000
A  3 / 30.02: Round4(3*SV/OH)=30.0200 vs Round4(3*Round4(avg))=30.0201
LUC 0.5 @ 0.0001: TotalCost/Quantity = 0.0002 != 0.0001
```

### Appendix B: profile headings to add to §2 / CLAUDE.md

```markdown
## Project
Jira key: IS
Multi-tenant inventory ledger (portfolio). .NET 10 + Aspire + SQL Server 2025 + Angular.

## Workflow
Delivery: pr
Base branch: main
Shared code: src/Shared/Kernel/**, src/Modules/*/*.Contracts/**

## Commands
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false

## Review constraints
- Money and quantities are decimal / decimal(19,4); never double or float. Rounding is AwayFromZero, 4 dp.
- No UPDATE/DELETE on StockDocuments/StockMovements; no hard deletes anywhere.
- TenantId never comes from a request; unnamed IgnoreQueryFilters() is banned outside tests.
- PostedAt only from TimeProvider; no request carries a date.
- No banned packages (MediatR, AutoMapper, MassTransit, PrimeNG, FluentAssertions 8+).
- Ask before changing §6 or adding columns to StockMovements/StockDocuments.

## High-stakes paths
src/Api/**/Auth*/**, src/Modules/Platform/**/Identity/**
src/Modules/Inventory/Inventory/Domain/**
src/Modules/Inventory/Inventory/Persistence/**
src/Modules/Inventory/Inventory/Features/{Posting,Reversals,Ledger}/**
**/Migrations/**

## Hidden coupling
- StockBalances.IsVirtual/AllowNegative mirror Locations; the toggle updates both in one transaction.
- Every tenant table must be in security.TenantPolicy (FILTER + 2 BLOCK) in the migration that creates it.
- HasTrigger() declarations ↔ trigger SQL in migrations.
- ProblemDetails slugs (§9) ↔ the Angular error interceptor map.
- RequestHash canonical form ↔ client serialization.
- Spec vector tables ↔ CostingPolicy unit tests.
```
