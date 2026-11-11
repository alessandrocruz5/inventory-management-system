# Inventory System — Sprint 1 Spec

Oct 30, 2026 · @Alessandro Cruz · audited 2026-11-04 (section 15)

## 1. Read this first

Sprint 1 ships the stock movement ledger and the skeleton around it: no AI, no vertical pack, no purchase orders. The research report's "MVP" tier is roughly 3–4 sprints for one person learning C#, EF Core, SQL Server, Angular and Aspire at the same time **\[Likely\]**.

The ledger goes first because everything later posts through it or reads from it. The AI tools query it, the food-service and retail packs write to it, and the job-hunt pitch (payments discipline applied to inventory) rests on it. A correctness bug here poisons every layer above **\[Likely\]**.

| Item | Lands in | Why |
| --- | --- | --- |
| Tenants, users, roles | Sprint 1 | Tenancy is expensive to retrofit |
| Items (base unit only) | Sprint 1 | The ledger needs something to move |
| Flat locations + virtual locations | Sprint 1 | Double-sided movements need Supplier, Customer and Adjustment |
| Append-only ledger + balance projection | Sprint 1 | The core |
| Moving average cost | Sprint 1 | Cost is written on the movement; adding it later means backfilling an append-only table |
| Idempotent posting, reversals, no backdating | Sprint 1 | The payments-discipline proof |
| Rebuild-from-ledger + variance check | Sprint 1 | The research said ship it from day one |
| Per-location negative-stock toggle | Sprint 1 | Food service needs it, and it changes the balance checks, so it belongs in the contract before a pack builds on it |
| Food-service pack: recipes, consumption, waste, prep | Sprint 2 | Your call; it stresses the ledger hardest |
| Unit conversions, stocktakes | Sprint 2 | Recipes buy in kg and use in grams; actual-vs-theoretical needs two physical counts |
| Retail pack: variants, price lists, bins | Sprint 3 **\[Guessing\]** | Sprint 1 already demos the core as a retail shop |
| Lots, expiry, FEFO | Sprint 3+ **\[Guessing\]** | Changes the balance key and the costing grain |
| POs, reservations, reorder alerts | Sprint 3+ **\[Guessing\]** | Need the outbox and background jobs |
| AI tools + evaluation matrix | Sprint 4+ **\[Guessing\]** | Needs real ledger data and a golden question set |

Sprint 2's food pack should need nothing from the ledger except new virtual location kinds: Consumption, Scrap and Production **\[Likely\]**. If it needs more, the sprint 1 contract missed something.

Sections 2–13 are written for Claude Code. Paste section 2 into the repo's `CLAUDE.md` and commit it to `main` before S1; point Claude Code at the rest as `docs/sprint-1-spec.md`. Section 14 records your decisions; section 15 logs reviews.

## 2. Context and ground rules (paste into CLAUDE.md)

You are building a multi-tenant inventory system for Philippine small businesses. It is a portfolio project: correctness and test evidence matter more than feature count. The core is an append-only stock movement ledger with a balance projection, built with the discipline of a payments ledger.

**Stack**

| Layer | Choice | Notes |
| --- | --- | --- |
| Runtime | .NET 10 (LTS), C# latest | `Nullable` enabled, `TreatWarningsAsErrors` on, in `Directory.Build.props` |
| API | ASP.NET Core minimal APIs | Endpoint groups per module; built-in OpenAPI + Scalar UI (no Swashbuckle) |
| Data | EF Core 10 on SQL Server 2025 | One DbContext and one schema per module; raw SQL allowed for posting and reports, always through EF Core (section 8). Aspire's SQL resource defaults to the 2022 image: set `.WithImageTag("2025-latest")` |
| Orchestration | Aspire AppHost + ServiceDefaults | Resolve the current Aspire version at scaffold time; check the current API for adding the Angular app |
| Auth | ASP.NET Core Identity, cookie scheme | Hand-written login/logout/me endpoints (see section 8) |
| Frontend | Angular, latest stable | Standalone components, signals, zoneless, `@if`/`@for` control flow, typed reactive forms |
| UI kit | Angular Material + AG Grid Community | Check versions with `npm view <pkg> version` before installing |
| Tests | xUnit v3, Shouldly, Testcontainers.MsSql, NetArchTest.Rules, Vitest, Playwright |  |

**Banned packages.** Do not add MediatR, AutoMapper, MassTransit, PrimeNG or FluentAssertions 8+. Each moved to a commercial licence **\[Likely — verify FluentAssertions before relying on this\]**. Write handlers as plain classes and map by hand.

**Allowed packages.** What the official templates add (Aspire's AppHost and ServiceDefaults, `ng new`), plus: `Aspire.Hosting.SqlServer`, the Aspire JavaScript hosting package current at scaffold time, `Aspire.Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.SqlServer` and `.Design`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `xunit.v3`, `Shouldly`, `Testcontainers.MsSql`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.TimeProvider.Testing`, `NetArchTest.Rules`, `NSubstitute`; npm: `@angular/material`, `@angular/cdk`, `ag-grid-community`, `ag-grid-angular`, `angular-eslint` (it provides `ng lint`), `@playwright/test`. No Dapper.

**Ask before you:** add any NuGet or npm package not named in this spec; change anything in section 6 (the ledger contract); add a column to `StockMovements` or `StockDocuments`; disable a test.

**Conventions**

- Quantities and money are `decimal` in C# and `decimal(19,4)` in SQL. Never `double` or `float`.
- Currency is PHP only. Store 4 decimal places; display 2.
- Rounding is `MidpointRounding.AwayFromZero` everywhere, to 4 decimal places.
- Time comes from an injected `TimeProvider`, stored as `datetimeoffset` in UTC. Display in Asia/Manila.
- Guid keys are generated by EF Core's default SQL Server generator. Do not use `Guid.CreateVersion7()` for clustered keys: SQL Server sorts `uniqueidentifier` by its last bytes, so v7 GUIDs fragment the index **\[Likely\]**.
- Every tenant-owned table has `TenantId` as the first column of its primary key and of every unique index.
- No hard deletes anywhere. Master data is deactivated; ledger rows are reversed.
- Errors are RFC 9457 ProblemDetails with a stable `type` slug (section 9).
- Module internals are `internal`. Other projects reference only a module's `.Contracts` project.

**Workflow**

- One slice from section 12 per branch and pull request. Name each branch with its Jira key alone (`IMS-2`) and start PR titles with it. Tests for the slice land in the same PR.
- `TASKS.md` (merge ledger) and `CHANGELOG.md` (Keep a Changelog) are bookkeeping: they're updated on `main` after each merge, never inside a slice's PR. Sprint 1 ends at version `0.1.0`.
- Request a code-guardian review on every slice that touches a High-stakes path: S2–S4, S5a–S5c, S6 and S9–S11.
- Run every `## Commands` line below before declaring a slice done.

**Workflow profile.** The plan, audit and build tooling reads these headings from `CLAUDE.md`. Paste them without the code fence, so they're real headings.

```markdown
## Project
Jira key: IMS
Multi-tenant inventory ledger for Philippine small businesses (portfolio). .NET 10, Aspire, SQL Server 2025, Angular.

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
- Money and quantities are decimal / decimal(19,4), never double or float; rounding is AwayFromZero to 4 dp.
- No UPDATE or DELETE on StockDocuments or StockMovements; no hard deletes anywhere.
- TenantId never comes from a request; IgnoreQueryFilters() without names is banned outside tests.
- PostedAt comes only from TimeProvider; no request carries a date.
- The API connects as inventory_app, never as the server admin.
- No banned packages, and no package outside the allowed list without asking.
- Ask before changing section 6 or adding a column to StockMovements or StockDocuments.

## High-stakes paths
src/Api/**/{Auth,Tenant}*/**
src/Modules/Platform/**/Identity/**
src/Modules/Inventory/Inventory/Domain/**
src/Modules/Inventory/Inventory/Persistence/**
src/Modules/Inventory/Inventory/Features/{Posting,Reversals,Ledger}/**
**/Migrations/**

## Hidden coupling
- StockBalances.IsVirtual and AllowNegative mirror Locations; toggles and postings follow the lock order in section 6.
- Every tenant table is in security.TenantPolicy (FILTER + two BLOCK predicates), added in the migration that creates it.
- HasTrigger() declarations match the trigger SQL in the migrations.
- ProblemDetails slugs (section 9) match the Angular error interceptor's map.
- The RequestHash canonical form (section 6, step 2) matches what the client sends.
- The vector tables in section 7 match the CostingPolicy unit tests.
```

## 3. Sprint goal and scope

**Goal:** a signed-in user can set up items and locations, receive, issue, transfer and adjust stock, reverse a mistake, and see on-hand quantities and costs the system can prove correct. Another tenant's data stays invisible at both the API and the database layer.

**Assumptions**

- No fixed sprint length. The sprint ends when section 13 is met; section 12 sets what slips first.
- One developer.
- Runs locally only: Aspire with SQL Server in Docker on the Manjaro desktop. SQL Server has no ARM64 image, so not on the MacBook **\[Likely\]**.
- Tenants and users come from a seed command, not sign-up. The demo tenant is a retail shop with two branches; a second tenant exists for isolation tests. The seed uses fixed tenant ids, so a second run finds what the first created.
- Two roles: `Admin` (everything) and `Staff` (receive, issue, transfer, view).
- Negative stock is off by default. An Admin can allow it per physical location (section 7).

**In scope**

- Aspire solution skeleton, GitHub Actions pipeline, seed command
- Login, logout, current user, tenant context from the auth cookie
- Tenant isolation enforced twice: EF Core named query filters and SQL Server row-level security
- Items: SKU, name, base unit, active flag
- Locations: flat physical locations plus three system virtual locations per tenant (Supplier, Customer, Adjustment)
- Stock documents: receipt, issue, transfer, adjustment, reversal
- Append-only movement ledger, balance projection, moving average cost
- Per-location negative-stock toggle with its costing rule
- Idempotency keys on every posting
- Ledger health check and rebuild-from-ledger
- Angular screens for all of the above (section 10)

**Out of scope: do not build, stub or scaffold these**

- AI assistant, MCP tools, embeddings, NL2SQL
- Food-service or retail packs, feature flags, custom fields
- Variants, unit-of-measure conversions, bins or location hierarchy
- Lots, expiry, FEFO, serial numbers
- Purchase orders, sales orders, reservations, stocktakes, reorder points
- Outbox, background jobs, SignalR, caching
- Receipts or invoices for customers (BIR accreditation applies to software that issues them)
- Entra External ID, Azure deployment (stretch only, section 12), Taglish or Filipino UI text

## 4. Solution structure

One deployable API, two modules in sprint 1 (Platform and Inventory), one schema per module. Later modules (packs, AI) plug in beside Inventory and call it only through `Inventory.Contracts`.

```text
inventory/
  src/
    AppHost/                  Aspire AppHost: sql, api, web
    ServiceDefaults/          OpenTelemetry, health checks, resilience
    Api/                      Program.cs, auth, tenant middleware, module registration
    Shared/Kernel/            ITenantContext, ITenantScopeFactory, ICurrentUser, ProblemDetails types, Money/Quantity helpers
    Modules/
      Platform/
        Platform.Contracts/   IPlatformModule, DTOs
        Platform/             Identity, tenants, seed command, schema "platform" + "identity"
      Inventory/
        Inventory.Contracts/  IInventoryModule (ProvisionTenant, PostDocument, GetBalance), DTOs
        Inventory/
          Domain/             CostingPolicy, PostingRules, invariants (no EF, no ASP.NET, no SQL)
          Features/           one folder per endpoint: Endpoint.cs, Handler.cs, Request.cs, Response.cs
          Persistence/        InventoryDbContext, configurations, migrations, PostingEngine (transaction, locks, raw SQL)
  web/                        Angular app
  tests/
    Inventory.UnitTests/
    Inventory.IntegrationTests/
    Platform.IntegrationTests/
    Architecture.Tests/
    e2e/                      Playwright
  deploy/                     `aspire publish` output (stretch)
  CLAUDE.md  TASKS.md  CHANGELOG.md  Directory.Build.props  .editorconfig  .github/workflows/ci.yml
```

**Rules**

- `Inventory/Domain` holds the only rich domain code. It is pure C#: no EF Core, no `HttpContext`, no SQL. `PostingEngine` in `Persistence` owns a posting's transaction, locks and SQL, and calls `PostingRules` and `CostingPolicy` for every decision. Everything else is a thin vertical slice.
- Each module registers itself with one extension method (`AddInventoryModule`, `MapInventoryEndpoints`). `Api` never touches a module's internals.
- Tenant and user reach modules through `ITenantContext` and `ICurrentUser` from `Shared/Kernel`, filled by middleware from the auth cookie, or outside a request (seed, tests) by `ITenantScopeFactory.BeginScope(tenantId)`. No module reads claims directly.
- Migrations run from a dedicated step in the AppHost (or a `migrate` command on the API) before the API starts serving, as the server admin. The API connects as `inventory_app` (section 8) and never auto-migrates on request.
- Architecture tests in `Architecture.Tests` enforce every rule above.

&#91;embedded content: request path · two tenant checks\]

A bug that skips the EF filter, such as a raw SQL report or a stray `IgnoreQueryFilters()`, still hits row-level security in the database.

## 5. Data model

Five inventory tables, one sequence, and Identity. The sketch below is the contract; turn it into EF Core configurations and migrations, using `migrationBuilder.Sql` for triggers, the sequence, check constraints and RLS.

```sql
-- schema platform
platform.Tenants (Id uniqueidentifier PK, Name nvarchar(200) NOT NULL, CreatedAt datetimeoffset NOT NULL)

-- schema identity: standard ASP.NET Core Identity tables.
-- AspNetUsers gets TenantId uniqueidentifier NOT NULL, FK -> platform.Tenants. One tenant per user.

-- schema inventory: every table below is under row-level security (section 8)
inventory.Items (
  TenantId uniqueidentifier, Id uniqueidentifier,
  Sku nvarchar(64) NOT NULL,              -- immutable once the item has a movement
  Name nvarchar(200) NOT NULL,
  BaseUnit nvarchar(16) NOT NULL,         -- free text, lowercase: pc, kg, l, box
  IsActive bit NOT NULL DEFAULT 1,
  CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion rowversion,
  PRIMARY KEY (TenantId, Id), UNIQUE (TenantId, Sku))

inventory.Locations (
  TenantId, Id,
  Code nvarchar(32) NOT NULL, Name nvarchar(200) NOT NULL,
  Kind varchar(16) NOT NULL CHECK (Kind IN ('Physical','Supplier','Customer','Adjustment')),
  AllowNegativeStock bit NOT NULL DEFAULT 0,  -- physical only; Admin toggle (section 7)
  IsActive bit NOT NULL DEFAULT 1,
  CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion rowversion,
  PRIMARY KEY (TenantId, Id), UNIQUE (TenantId, Code),
  UNIQUE (TenantId, Kind) WHERE Kind <> 'Physical',   -- exactly one of each virtual kind
  CHECK (Kind = 'Physical' OR AllowNegativeStock = 0))

inventory.StockDocuments (
  TenantId, Id uniqueidentifier,
  Type varchar(16) NOT NULL CHECK (Type IN ('Receipt','Issue','Transfer','Adjustment','Reversal')),
  ReversesDocumentId uniqueidentifier NULL,   -- set if and only if Type = 'Reversal'
  ReasonCode varchar(24) NULL,                -- adjustments only: Damaged, Lost, Found, CountCorrection, Other
  Reference nvarchar(100) NULL,               -- e.g. supplier delivery receipt number
  Notes nvarchar(500) NULL,
  IdempotencyKey varchar(64) NOT NULL,
  RequestHash binary(32) NOT NULL,            -- SHA-256 of the canonical request (section 6, step 2)
  PostedAt datetimeoffset NOT NULL, PostedBy uniqueidentifier NOT NULL,
  PRIMARY KEY (TenantId, Id),
  UNIQUE (TenantId, IdempotencyKey),
  UNIQUE (TenantId, ReversesDocumentId) WHERE ReversesDocumentId IS NOT NULL)

CREATE SEQUENCE inventory.MovementIds AS bigint START WITH 1 INCREMENT BY 1;

inventory.StockMovements (                    -- the ledger
  TenantId, Id bigint NOT NULL,               -- NEXT VALUE FOR inventory.MovementIds, taken before insert
  DocumentId uniqueidentifier NOT NULL, LineNo int NOT NULL,   -- request line n; its revaluation, if any, is 1000 + n
  ItemId uniqueidentifier NOT NULL,
  FromLocationId uniqueidentifier NOT NULL, ToLocationId uniqueidentifier NOT NULL,
  Quantity decimal(19,4) NOT NULL,            -- 0 only on a revaluation movement (section 7)
  TotalCost decimal(19,4) NOT NULL CHECK (TotalCost >= 0),
  ReversesMovementId bigint NULL,
  PostedAt datetimeoffset NOT NULL, PostedBy uniqueidentifier NOT NULL,
  CHECK (FromLocationId <> ToLocationId),
  CHECK (Quantity > 0 OR (Quantity = 0 AND TotalCost > 0)),
  PRIMARY KEY (TenantId, Id),
  UNIQUE (TenantId, DocumentId, LineNo),
  INDEX IX_Movements_Item (TenantId, ItemId, Id))

inventory.StockBalances (                     -- projection of the ledger
  TenantId, ItemId, LocationId,
  IsVirtual bit NOT NULL,                     -- copied from Locations.Kind <> 'Physical'
  AllowNegative bit NOT NULL,                 -- copied from Locations.AllowNegativeStock
  OnHand decimal(19,4) NOT NULL DEFAULT 0,
  StockValue decimal(19,4) NOT NULL DEFAULT 0,
  LastUnitCost decimal(19,4) NULL,            -- set by inflows, derived from the ledger (section 7); costs outflows when OnHand <= 0
  LastMovementId bigint NULL,
  RowVersion rowversion,
  PRIMARY KEY (TenantId, ItemId, LocationId),
  INDEX IX_Balances_Location (TenantId, LocationId),
  CHECK (IsVirtual = 1
         OR (OnHand >= 0 AND StockValue >= 0 AND (OnHand <> 0 OR StockValue = 0))
         OR (OnHand < 0 AND AllowNegative = 1)),
  CHECK (IsVirtual = 0 OR StockValue = 0))
```

**Rules**

- Every foreign key is composite and includes `TenantId`, so a row can never point at another tenant's row.
- Every `TenantId` column defaults to `CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)`, so raw-SQL inserts get the tenant without passing it. The BLOCK predicate still checks it.
- Average cost is derived (`StockValue / OnHand`), never stored. It is undefined when `OnHand <= 0`; outflows then use `LastUnitCost`.
- Virtual locations track quantity only. Their `OnHand` goes negative by design (Supplier ends at minus everything ever received) and `StockValue` stays 0.
- `StockBalances.AllowNegative` mirrors the location's toggle so the `CHECK` can enforce it per row. Changing the toggle updates the location and all its balance rows in one transaction, in the lock order of section 6.
- `StockDocuments` and `StockMovements` get `INSTEAD OF UPDATE, DELETE` triggers that `THROW`. Declare them in EF with `ToTable(t => t.HasTrigger(...))`, or SaveChanges' `OUTPUT` clause fails on those tables **\[Likely\]**.
- Creating a tenant calls `IInventoryModule.ProvisionTenant` inside that tenant's scope. It inserts the three virtual locations with reserved codes `SUP`, `CUS`, `ADJ`, and does nothing if they already exist. Users cannot create, edit or deactivate them.
- Set `READ_COMMITTED_SNAPSHOT ON` in the first migration (with `suppressTransaction: true`) so local SQL Server matches Azure SQL's default **\[Likely\]**.

## 6. Posting engine: the ledger contract

Every stock change is a document posted in one transaction. A posted document is never edited or deleted; mistakes are undone by a reversal document.

| Document | From → To | Cost on each movement | Roles |
| --- | --- | --- | --- |
| Receipt | Supplier → physical | `quantity × unitCost` from the request | Staff, Admin |
| Issue | physical → Customer | Moving average at the source (section 7) | Staff, Admin |
| Transfer | physical A → physical B | Moving average at A | Staff, Admin |
| Adjustment, positive line | Adjustment → physical | Request `unitCost`; when omitted, `Round4(qty × StockValue / OnHand)`; required when `OnHand <= 0` | Admin |
| Adjustment, negative line | physical → Adjustment | Moving average at the source | Admin |
| Reversal | Each original movement, revaluations included, with From and To swapped | The original movement's `TotalCost`, exactly | Admin |
| Revaluation (inside an inflow's document) | physical ↔ Adjustment, quantity 0 | The estimate variance the inflow settles (section 7) | system |

**Posting steps** (inside `PostingEngine`, wrapped in EF's execution strategy so deadlock error 1205 retries up to 3 times)

1. Validate the request: 1–100 lines; each item at most once per document; quantity > 0 and unit cost ≥ 0, each with at most 4 decimals and at most 999,999,999.9999; a transfer's from and to locations differ; items and locations exist, are active and have the right kind. Ids from another tenant behave exactly like ids that don't exist. A line whose Round4(quantity × unitCost) exceeds 999,999,999,999,999.9999 (the decimal(19,4) maximum) fails on `lines[i].unitCost`. The checks that items and locations exist and are active run after step 2, so a replay of a successful post returns the original even if master data has changed since.
2. Idempotency fast path: look up `(TenantId, IdempotencyKey)`. Same `RequestHash` → return the stored document with `200` and header `Idempotent-Replayed: true`. Different hash → `409 idempotency-key-reused`. The hash is SHA-256 over the HTTP method, the route and the validated request re-serialized canonically: fixed property order, decimals without trailing zeros, absent and null treated alike. Only a successful post consumes a key. The hash covers the validated request, so the lookup runs right after the pure checks of step 1.
3. Begin the transaction (READ COMMITTED with snapshot reads on). Take the tenant's ledger lock: `sp_getapplock` on `ledger:<TenantId>` in `Shared` mode, owned by the transaction. Rebuild takes it in `Exclusive` mode.
4. Insert the `StockDocuments` row, complete and final, because the trigger forbids a later UPDATE. This claims the key: a parallel duplicate blocks on the unique index until this transaction ends, then gets 2627/2601, rolls back and answers as a replay (step 2). If this transaction rolls back, the duplicate proceeds.
5. Make sure every balance row the document touches exists: `INSERT … WHERE NOT EXISTS` in `(ItemId, LocationId)` order, copying `IsVirtual` and `AllowNegative` from `Locations`, and swallow unique-key errors 2627/2601 from a concurrent insert.
6. Lock all touched balance rows in one statement, `WITH (UPDLOCK, ROWLOCK)`, seeking on the clustered key. The clustered key `(TenantId, ItemId, LocationId)`, not the `ORDER BY`, is what makes every transaction lock in the same order and keeps A→B and B→A transfers from deadlocking **\[Likely\]**. Add no index that changes this access path.
7. Re-read the document's items and locations `WITH (READCOMMITTEDLOCK)`. Reject inactive ones (`422 inactive-item` / `inactive-location`), and take each balance's `IsVirtual` and `AllowNegative` from this read, not from the row. A reversal runs its checks here (below).
8. Compute each line's cost, any revaluation and the new balances in C# with `PostingRules` and `CostingPolicy` (pure functions). Reject with `422 insufficient-stock` if a physical balance would go below zero at a location whose toggle is off. Reject with 400 validation on `lines[i].quantity` if any new OnHand or StockValue would fall outside decimal(19,4).
9. Take movement ids from `inventory.MovementIds`. Insert movements with their final `TotalCost`. Update balances, setting `AllowNegative` (from step 7), `LastMovementId` and `LastUnitCost`.
10. Commit.

The `CHECK` constraints on `StockBalances` are the backstop. If application code ever lets a balance go negative where the toggle is off, the database refuses the commit.

**Hard rules**

- No backdating. `PostedAt` is always `TimeProvider.GetUtcNow()`. No request carries a date.
- No edits and no deletes. The triggers enforce this even against hand-written SQL.
- Deactivating an item or location requires zero on-hand and zero value in every physical location. Inactive items and locations accept no new postings, reversals included.
- Turning a location's negative-stock toggle off fails with `422 negative-balances-exist` while any of its balances has `OnHand < 0`.
- Deactivation and toggle changes first lock the affected balance rows `WITH (UPDLOCK, HOLDLOCK)` (through `IX_Balances_Location` for a location), then check them, then update the item or location row. A posting that started earlier finishes first; one that starts later sees the change at step 7.

**Reversal rules**

- Only `Admin`. The original must not be a reversal and must not already be reversed (the filtered unique index enforces the second).
- Only the latest movement can be undone. For every physical balance the original touched, `StockBalances.LastMovementId` must equal the id of the original document's last movement on that balance (its revaluation, if it has one). Otherwise answer `409 reversal-not-latest` and tell the user to post a correcting document instead.
- These checks run at step 7, under the balance locks. A reversal then goes through step 8 like any posting, so it gets `422 insufficient-stock` if a location's toggle was turned off since the original and the reversal would take it below zero.
- Each reversal movement copies the original's `Quantity` and `TotalCost`, swaps the locations and sets `ReversesMovementId`. Because nothing was posted in between, this restores `OnHand` and `StockValue` exactly. The engine then recomputes `LastUnitCost` from the ledger (section 7), which restores it too. That exactness is why the latest-only rule exists.

**Invariants** (checked by tests and by `GET /api/ledger/health`)

- **INV-1 Projection.** For every item and location, `OnHand` = Σ quantity in − Σ quantity out. For physical locations, `StockValue` = Σ cost in − Σ cost out.
- **INV-2 Conservation.** For every item, Σ `OnHand` across all locations, virtual included, is 0.
- **INV-3 Physical sanity.** At every physical location, `OnHand` ≥ 0 implies `StockValue` ≥ 0, and `OnHand` = 0 implies `StockValue` = 0. Where the toggle is off, `OnHand` ≥ 0 as well.
- **INV-4 Reversal mirror.** Every reversal movement mirrors its original's item, quantity and cost with From and To swapped.
- **INV-5 Last unit cost.** Every physical balance's `LastUnitCost` equals the value derived from the ledger (section 7).

**Rebuild.** `POST /api/ledger/rebuild` takes the tenant's ledger lock in `Exclusive` mode, so no posting runs while it reads and writes. It recomputes `OnHand`, `StockValue` and `LastMovementId` for every balance of the current tenant as a `GROUP BY` over `StockMovements`. Costs are stored on movements, so those need no costing replay. `LastUnitCost` comes from the same windowed query that checks INV-5. It returns the differences it found, then overwrites them in the same transaction. Health takes no lock: each invariant is a single statement, so snapshot reads give it a consistent view while postings continue.

## 7. Moving average costing

Each physical balance carries `OnHand` and `StockValue`. Inflows add their stated cost; outflows remove a share of the value; the last unit out takes whatever value is left, so rounding never leaves a residue.

```csharp
// Inventory/Domain/CostingPolicy.cs: pure, no I/O. All rounding: 4 dp, MidpointRounding.AwayFromZero.

// Receipt or positive adjustment line with a unit cost
decimal InflowCost(decimal qty, decimal unitCost) => Round4(qty * unitCost);

// Positive adjustment line without a unit cost; only when b.OnHand > 0, otherwise unitCost is required
decimal DefaultAdjustmentCost(decimal qty, Balance b) => Round4(qty * b.StockValue / b.OnHand);

// Issue, transfer-out, negative adjustment line. b = OnHand, StockValue, LastUnitCost, AllowNegative
decimal OutflowCost(decimal qty, Balance b)
{
    if (qty > b.OnHand && !b.AllowNegative) throw new InsufficientStock(...);
    if (qty == b.OnHand) return b.StockValue;           // empties the balance: take the remainder
    if (qty < b.OnHand)  return Round4(qty * b.StockValue / b.OnHand);
    // Toggle on and qty > OnHand: drain what is there, estimate the rest
    return b.OnHand > 0
        ? b.StockValue + Round4((qty - b.OnHand) * (b.StockValue / b.OnHand))
        : Round4(qty * (b.LastUnitCost ?? 0m));
}

// Any inflow except a reversal: when it lifts a negative balance (toggle on) to zero or above, revalue the
// stock on hand at the inflow's unit cost and settle the estimates. Returns the variance:
// > 0 posts a revaluation movement physical -> Adjustment, < 0 Adjustment -> physical, 0 posts nothing.
decimal Revaluation(decimal qty, decimal cost, Balance b)
{
    var onHand = b.OnHand + qty;
    if (b.OnHand >= 0 || onHand < 0) return 0m;
    return b.StockValue + cost - Round4(onHand * cost / qty);
}

// Transfer-in            -> cost = the matching transfer-out's cost
// Reversal of an inflow  -> outflow whose cost = the original movement's TotalCost (not the average)
// Reversal of an outflow -> inflow whose cost = the original movement's TotalCost
// Reversals copy the original's revaluation movement too, swapped, and never compute one of their own.
// Then: inflow  => OnHand += qty; StockValue += cost - revaluation
//       outflow => OnHand -= qty; StockValue -= cost
// After every inflow that isn't a reversal, and its revaluation:
//   LastUnitCost = OnHand > 0 ? Round4(StockValue / OnHand) : Round4(cost / qty)
// So LastUnitCost is a function of the ledger: the value set by the latest inflow into the balance,
// ignoring reversed movements and reversals, with OnHand and StockValue as running sums after it.
// Reversals, rebuild and INV-5 compute it that way.
```

The tables below are the acceptance vectors. Unit tests run them against `CostingPolicy`; one integration test runs them through the API. Results must match to the fourth decimal **\[Certain: computed with a decimal reference script\]**.

**Vector set V: two locations, A and B**

| Step | Action | Movement cost (₱) | A after: qty / value | B after: qty / value |
| --- | --- | --- | --- | --- |
| V1 | Receive 10 @ 100.00 into A | 1,000.0000 | 10 / 1,000.0000 | — |
| V2 | Receive 10 @ 120.00 into A | 1,200.0000 | 20 / 2,200.0000 (avg 110) | — |
| V3 | Issue 15 from A | 1,650.0000 | 5 / 550.0000 | — |
| V4 | Receive 5 @ 130.00 into A | 650.0000 | 10 / 1,200.0000 (avg 120) | — |
| V5 | Transfer 4 from A to B | 480.0000 | 6 / 720.0000 | 4 / 480.0000 |
| V6 | Receive 6 @ 125.00 into B | 750.0000 | 6 / 720.0000 | 10 / 1,230.0000 (avg 123) |
| V7 | Reverse V6 | 750.0000 | 6 / 720.0000 | 4 / 480.0000 (avg 120, restored) |
| V8 | Reverse V4 | rejected: `409 reversal-not-latest` (V5 touched A after V4) | unchanged | unchanged |
| V9 | Issue 7 from A | rejected: `422 insufficient-stock` (6 available) | unchanged | unchanged |
| V10 | Issue 6 from A | 720.0000 (the remainder) | 0 / 0.0000 | 4 / 480.0000 |

**Vector set R: rounding, location C**

| Step | Action | Movement cost (₱) | C after: qty / value |
| --- | --- | --- | --- |
| R1 | Receive 1 @ 10.00 | 10.0000 | 1 / 10.0000 |
| R2 | Receive 2 @ 10.01 | 20.0200 | 3 / 30.0200 |
| R3 | Issue 1 | 10.0067 (30.02 ÷ 3 = 10.00667, rounded) | 2 / 20.0133 |
| R4 | Issue 1 | 10.0067 (20.0133 ÷ 2 = 10.00665, midpoint rounds up) | 1 / 10.0066 |
| R5 | Issue 1 | 10.0066 (the remainder) | 0 / 0.0000 |

Total issued in set R is 30.0200, exactly what was received. That is the point of the remainder rule. R4 fails if anyone uses C#'s default banker's rounding, which gives 10.0066.

**Vector set A: adjustments, location D (toggle off)**

| Step | Action | Movement cost (₱) | D after: qty / value |
| --- | --- | --- | --- |
| A1 | Receive 1 @ 10.00 | 10.0000 | 1 / 10.0000 |
| A2 | Receive 2 @ 10.01 | 20.0200 | 3 / 30.0200 |
| A3 | Correct +3, no unit cost | 30.0200 (3 × 30.02 ÷ 3; rounding the average first gives 30.0201, which is wrong) | 6 / 60.0400 |
| A4 | Correct −1 | 10.0067 | 5 / 50.0333 |
| A5 | Correct +2 @ 12.50 | 25.0000 | 7 / 75.0333 |
| A6 | Correct −7 | 75.0333 (the remainder) | 0 / 0.0000 |
| A7 | Correct +1, no unit cost | rejected: `400 validation` (D holds none, so `unitCost` is required) | unchanged |

**Negative stock (toggle on)**

An Admin can allow negative stock on a physical location, such as a restaurant kitchen whose sales post before the delivery is logged. Outflows past zero are costed at an estimate. The inflow that brings the balance back to zero or above settles the difference with a revaluation movement, so value is still conserved.

- The estimate is the current average while stock is positive, then `LastUnitCost`. An item never received at that location is estimated at ₱0 and shows in red on the stock screen.
- Settling revalues the stock on hand at the inflow's unit cost and posts the difference as a zero-quantity movement against the Adjustment location (N4, N7, X3). No balance is ever left with stock and negative value, or with zero stock and leftover value, so every movement cost stays ≥ 0.
- Reversals still follow the latest-only rule, so they restore negative balances exactly too.

**Vector set N: location K, toggle on**

| Step | Action | Movement cost (₱) | K after: qty / value | LastUnitCost |
| --- | --- | --- | --- | --- |
| N1 | Receive 4 @ 50.00 | 200.0000 | 4 / 200.0000 | 50.0000 |
| N2 | Issue 6 | 300.0000 (200 remainder + 2 × 50 estimate) | −2 / −100.0000 | 50.0000 |
| N3 | Issue 1 | 50.0000 (1 × LastUnitCost) | −3 / −150.0000 | 50.0000 |
| N4 | Receive 10 @ 56.00 | 560.0000; revaluation 18.0000 out to ADJ (the 7 on hand cost 7 × 56 = 392) | 7 / 392.0000 | 56.0000 |
| N5 | Issue 7 | 392.0000 (the remainder) | 0 / 0.0000 | 56.0000 |
| N6 | Issue 2 | 112.0000 (2 × LastUnitCost) | −2 / −112.0000 | 56.0000 |
| N7 | Receive 2 @ 60.00 | 120.0000; revaluation 8.0000 out to ADJ | 0 / 0.0000 | 60.0000 |
| N8 | Receive 5 @ 60.00 | 300.0000 | 5 / 300.0000 | 60.0000 |
| N9 | Issue 5 | 300.0000 (the remainder) | 0 / 0.0000 | 60.0000 |

Set N receives ₱1,180.0000. Issues take ₱1,154.0000 and revaluations ₱26.0000: the ₱18 under-costed in N2–N3 (3 units estimated at 50 that cost 56) and the ₱8 in N6 (2 units at 56 that cost 60). The variances post as revaluations instead of distorting the next average **\[Certain: same reference script\]**.

**Vector set X: location K2, toggle on, estimate above the next price**

| Step | Action | Movement cost (₱) | K2 after: qty / value | LastUnitCost |
| --- | --- | --- | --- | --- |
| X1 | Receive 4 @ 50.00 | 200.0000 | 4 / 200.0000 | 50.0000 |
| X2 | Issue 6 | 300.0000 | −2 / −100.0000 | 50.0000 |
| X3 | Receive 3 @ 10.00 | 30.0000; revaluation 80.0000 in from ADJ | 1 / 10.0000 | 10.0000 |
| X4 | Issue 1 | 10.0000 (the remainder) | 0 / 0.0000 | 10.0000 |

Without the revaluation, X3 leaves 1 / −70.0000, and X4 would post a cost of −70 that the `TotalCost >= 0` check refuses.

## 8. Tenancy, auth and security

The tenant comes only from the auth cookie, and isolation is enforced twice: EF Core filters for normal queries, SQL Server row-level security for everything else. Neither layer trusts the other.

**Authentication**

- ASP.NET Core Identity with the cookie scheme. Cookie: `HttpOnly`, `Secure`, `SameSite=Strict`, 8-hour sliding expiry. Lockout after 5 failed attempts.
- Write `POST /api/auth/login`, `POST /api/auth/logout` and `GET /api/auth/me` by hand with `SignInManager`. Do not use `MapIdentityApi`: it also maps a public `/register` endpoint, which would create users with no tenant **\[Likely\]**.
- A custom `IUserClaimsPrincipalFactory` adds a `tenant_id` claim from `AspNetUsers.TenantId`. Roles are Identity roles: `Admin`, `Staff`.
- In development the Angular dev server proxies `/api` to the API, so the browser sees one origin and the strict cookie works.
- API endpoints accept `application/json` only. With `SameSite=Strict` that blocks cross-site form posts; full antiforgery tokens are sprint 2 hardening.

**Tenant context**

- Middleware reads `tenant_id` into a scoped `ITenantContext`. A request with a valid cookie but no tenant claim gets `401`.
- No request body, query string or route ever carries a tenant id.
- Another tenant's id in a route returns `404`, never `403`, so ids from other tenants can't be probed.

**Layer 1: EF Core 10 named query filters**

- Every tenant-owned entity gets `HasQueryFilter("Tenant", e => e.TenantId == tenant.TenantId)`.
- Named filters let a later soft-delete filter be switched off without dropping the tenant filter. `IgnoreQueryFilters()` with no names is banned outside tests.
- Inserts set `TenantId` in a `SaveChanges` interceptor from `ITenantContext`. Handlers never set it. Raw-SQL inserts rely on the column default (section 5).
- Register DbContexts with `AddDbContext` and enrich them with Aspire's `EnrichSqlServerDbContext`. Don't use `AddSqlServerDbContext`: it pools contexts, and a pooled context keeps the first request's tenant. No interceptor holds a tenant beyond one scope: resolve interceptors per context from the scoped service provider, or have them read the current scope's `ITenantContext` each time they run.

**Layer 2: SQL Server row-level security**

```sql
CREATE SCHEMA security;
CREATE FUNCTION security.fn_tenant(@TenantId uniqueidentifier)
  RETURNS TABLE WITH SCHEMABINDING AS
  RETURN SELECT 1 AS ok
  WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier);

CREATE SECURITY POLICY security.TenantPolicy
  ADD FILTER PREDICATE security.fn_tenant(TenantId) ON inventory.Items,
  ADD BLOCK  PREDICATE security.fn_tenant(TenantId) ON inventory.Items AFTER INSERT,
  ADD BLOCK  PREDICATE security.fn_tenant(TenantId) ON inventory.Items AFTER UPDATE,
  -- repeat FILTER + both BLOCK predicates for Locations, StockDocuments, StockMovements, StockBalances
  WITH (STATE = ON);
```

- A `DbConnectionInterceptor` runs `EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1` on every connection open. Pooled connections are reset between uses, which clears session context **\[Likely — prove it with a test\]**.
- With no session context set, the predicate matches nothing. The seed and tests act as a tenant through `ITenantScopeFactory.BeginScope(tenantId)`, which opens a new DI scope and so a new connection. A connection never switches tenant, because the session context is read-only once set.
- Identity tables stay outside the policy, because login runs before the tenant is known. Code filters users by tenant explicitly.
- Raw SQL runs through EF Core (`Database.SqlQuery`, `ExecuteSql`, or a command on EF's connection after `Database.OpenConnectionAsync()`), so it goes through the same connection interceptor and inherits RLS. A connection opened any other way has no session context and sees nothing.
- Every migration that creates a tenant table adds its FILTER and both BLOCK predicates with `ALTER SECURITY POLICY` in the same migration.

**Other rules**

- Errors outside Development return ProblemDetails with no stack trace or SQL text.
- Never log request bodies. Log tenant id, user id, document id and the problem `type`.
- Connection strings and the seed passwords come from Aspire parameters and user-secrets. Nothing secret is committed.
- Two SQL principals. The migrate step connects as the server admin (`sa` locally), creates the `inventory_app` login and user if they're missing (password from an Aspire parameter), applies migrations and grants: `SELECT`, `INSERT` and `UPDATE` on the `platform`, `identity` and `inventory` schemas; `DELETE` on `identity` only; no `UPDATE` on `StockDocuments` or `StockMovements`; `UPDATE` on `inventory.MovementIds` for `NEXT VALUE FOR`. The API and the seed connect as `inventory_app`, which can't alter, disable or truncate anything, so the triggers and RLS hold even against its own SQL.

## 9. API surface

All routes sit under `/api`, require a signed-in user unless marked public, and return ProblemDetails on error. Lists take `page` and `pageSize` (default 25, max 100).

| Method and path | Role | Purpose |
| --- | --- | --- |
| `POST /auth/login` | public | Email + password; sets the cookie |
| `POST /auth/logout` | any | Clears the cookie |
| `GET /auth/me` | any | User id, email, role, tenant name |
| `GET /items` | any | Search by SKU or name; filter `active` |
| `POST /items` | Admin | Create |
| `GET /items/{id}` | any | Item with its balance in each physical location |
| `PUT /items/{id}` | Admin | Edit name and unit; SKU only before the first movement; body carries `version` (rowversion) |
| `POST /items/{id}/deactivate` | Admin | Only with zero physical stock |
| `GET /items/{id}/movements` | any | Ledger history, newest first, with running balance per location (window `SUM() OVER`) |
| `GET /locations` | any | Physical and virtual; virtual ones flagged `isSystem` |
| `POST /locations`, `PUT /locations/{id}`, `POST /locations/{id}/deactivate` | Admin | Physical locations only; PUT carries allowNegativeStock |
| `POST /documents/receipts` | Staff, Admin | Requires `Idempotency-Key` |
| `POST /documents/issues` | Staff, Admin | Requires `Idempotency-Key` |
| `POST /documents/transfers` | Staff, Admin | Requires `Idempotency-Key` |
| `POST /documents/adjustments` | Admin | Requires `Idempotency-Key` |
| `POST /documents/{id}/reversal` | Admin | Requires `Idempotency-Key`; empty body |
| `GET /documents` | any | Filter by type and posted-at range |
| `GET /documents/{id}` | any | Lines, movement costs, reversal link either way |
| `GET /stock` | any | Balances by item × physical location with average cost and value; filter by item or location; totals for the whole filter, not just the page |
| `GET /ledger/health` | Admin | INV-1 to INV-5 results with failing rows |
| `POST /ledger/rebuild` | Admin | Rebuild and return the diff |

**Request bodies**

```json
POST /api/documents/receipts
Idempotency-Key: 9b3c2e0a-6a77-4c1e-9d0f-2f3e1c8a4b10
{ "locationId": "…", "reference": "DR-10442", "notes": null,
  "lines": [ { "itemId": "…", "quantity": 10, "unitCost": 100.00 } ] }

POST /api/documents/issues
{ "locationId": "…", "reference": null, "notes": "sold at counter",
  "lines": [ { "itemId": "…", "quantity": 3 } ] }

POST /api/documents/transfers
{ "fromLocationId": "…", "toLocationId": "…", "notes": null,
  "lines": [ { "itemId": "…", "quantity": 4 } ] }

POST /api/documents/adjustments
{ "locationId": "…", "reasonCode": "Damaged", "notes": "water damage",
  "lines": [ { "itemId": "…", "quantityDelta": -2 },
             { "itemId": "…", "quantityDelta": 5, "unitCost": 42.50 } ] }
```

Successful posts return `201 Created` with the full document, its movements with costs, and the resulting balances. Replays return `200` with `Idempotent-Replayed: true`.

**Problem types** (a relative URI, allowed by RFC 9457: the `type` field is `/problems/<slug>`)

| Slug | Status | Extra fields |
| --- | --- | --- |
| `validation` | 400 | `errors` per field |
| `idempotency-key-missing` | 400 | — |
| `not-found` | 404 | — |
| `idempotency-key-reused` | 409 | `originalDocumentId` |
| `reversal-not-latest` | 409 | `itemId`, `locationId`, `blockingDocumentId` |
| `already-reversed` | 409 | `reversalDocumentId` |
| `concurrency-conflict` | 409 | — (after 3 retries, or a stale `version` on an edit) |
| `insufficient-stock` | 422 | `itemId`, `locationId`, `available`, `requested` |
| `inactive-item` / `inactive-location` | 422 | the id |
| `stock-not-zero` | 422 | balances that block deactivation |
| `negative-balances-exist` | 422 | balances that block turning the toggle off |

## 10. Angular UI

Eight screens, written for a shop owner who is not technical: plain verbs, peso amounts, and forms that work on a phone at 375 px wide. Stock on hand is the landing page.

| Route | Screen | Roles | Must show or do |
| --- | --- | --- | --- |
| `/login` | Sign in | public | Email, password, lockout message |
| `/stock` | Stock on hand | any | AG Grid: item, location, qty, unit, avg cost, value; filter by location; totals row from the API's totals; negative balances in red; row click opens the item |
| `/items` | Items | any (edit: Admin) | Grid with search; create/edit dialog; deactivate with the `stock-not-zero` message when blocked |
| `/items/:id` | Item detail | any | Balance per location; movement history with running balance; link to each document |
| `/locations` | Locations | any (edit: Admin) | Physical list with create/edit/deactivate and an Allow negative stock switch (Admin, with a warning); virtual ones read-only with a "System" chip |
| `/documents/new/:type` | Post stock | Staff, Admin (adjust: Admin) | One form component for all four types (below) |
| `/documents`, `/documents/:id` | Documents | any (reverse: Admin) | List with type and date filters; detail with lines, costs, Reverse button, link to the reversal |
| `/admin/ledger` | Ledger health | Admin | INV-1 to INV-5 pass/fail with failing rows; Rebuild button with a confirm step and the returned diff |

**Labels.** Use the plain verbs in the navigation and form titles: Receive stock, Use or sell stock, Move stock, Correct stock. Document types keep their technical names in the API.

**Post-stock form**

- Header fields change by type: location (receipt, issue, adjustment), from and to (transfer), reason (adjustment), reference and notes.
- Line editor: item autocomplete by SKU or name, quantity, and unit cost for receipts and positive adjustments. Show the current on-hand at the chosen location beside each line, and warn before submit if a line exceeds it.
- Generate the `Idempotency-Key` with `crypto.randomUUID()` when the form opens. Reuse it on every retry and double-click; make a new one only after a successful post or when the user starts a new form. On `idempotency-key-reused`, show a link to the original document (`originalDocumentId`) and make a new key.
- Disable Submit while a request is in flight. On `insufficient-stock`, highlight the offending line and show available vs requested.

**Conventions**

- Standalone components, signals, zoneless change detection, `@if`/`@for`, typed reactive forms, functional route guards and HTTP interceptors.
- State lives in small signal-based services per feature. No NgRx in sprint 1.
- Money shows as ₱1,234.50 (`CurrencyPipe` with `PHP`, 2 decimals). Quantities drop trailing zeros (10, not 10.0000).
- Dates show in Asia/Manila, for example 30 Oct 2026, 2:14 PM.
- One HTTP interceptor maps ProblemDetails `type` slugs to friendly messages and sends `401` to `/login`.
- Hide buttons the role can't use. The API still enforces every role check.

## 11. Tests and CI

The tests are the portfolio. Each one below must exist by name, and each proves a claim a reviewer will probe.

**Test isolation.** Every integration test creates its own tenant. Tenants can't see each other, so no database reset is needed, which matters because the ledger tables refuse `DELETE`. T5 grows with every slice that adds a route; T6's catalog check covers new tables by itself.

| Id | Test | Level | Proves |
| --- | --- | --- | --- |
| T1 | Costing vectors V, R and A | Unit + one API run each | Costing math, rounding and adjustment defaults (section 7) |
| T2 | 20 parallel issues of the last unit | Integration | Exactly one `201`, nineteen `422`, final on-hand 0 |
| T3 | 50 parallel transfers A→B and B→A | Integration | No unhandled deadlock; invariants hold afterwards |
| T4 | Same key + body twice, sequential and parallel; two parallel same-key issues of the last unit; two parallel same-key reversals; same key + different body | Integration | One document, and the duplicate gets a `200` replay, never a `422` or `409`; `409 idempotency-key-reused` |
| T5 | Tenant B ids used by tenant A on every route and in every body; each tenant lists every collection; requests alternate A, B, A in one host | Integration | `404` for route ids, `400` for body ids, zero foreign rows in any list |
| T6 | As `inventory_app`, a raw `SqlConnection` with tenant A's session context reads and inserts tenant B rows; then no session context; then the catalog | Integration | RLS blocks both without EF filters; empty result with no context; every table with a `TenantId` column has a FILTER and two BLOCK predicates in `sys.security_predicates` |
| T7 | `UPDATE` and `DELETE` on documents and movements as the owner; as `inventory_app`, also `DISABLE TRIGGER`, `TRUNCATE TABLE` and `ALTER SECURITY POLICY` | Integration | Triggers throw; `inventory_app` is refused all three |
| T8 | Reverse a receipt, an issue and a transfer; reverse twice; reverse a reversal; reverse a non-latest | Integration | Exact restore of `OnHand`, `StockValue` and `LastUnitCost` (V7: B back to 120.0000); the three rejections |
| T9 | Seeded random run: 500 operations over 3 items × 3 locations, valid and invalid; one rebuild runs while postings continue | Integration | INV-1 to INV-5 hold every 50 operations and after the concurrent rebuild; rebuild diff is empty |
| T10 | `FakeTimeProvider` set to a fixed time | Integration | `PostedAt` equals the clock; no route accepts a date |
| T11 | Session context after connection reuse | Integration | A pooled connection never carries the previous tenant |
| T12 | Module boundaries | Architecture | Rules in section 4 |
| T13 | Line editor, money and quantity formatting, idempotency key reuse | Vitest | Frontend logic |
| T14 | Staff signs in, receives 10 @ ₱100, issues 3, sees 7 and ₱700.00; Admin reverses the issue and sees 10 | Playwright | End to end |
| T15 | Vector sets N and X; toggle off with a negative balance; Staff tries to flip the toggle | Unit + integration | Estimate costing, revaluation and value conservation; 422 negative-balances-exist; 403 for Staff |

**Tooling.** xUnit v3, Shouldly, `WebApplicationFactory<Program>`, Testcontainers.MsSql pinned to `mcr.microsoft.com/mssql/server:2025-latest`, `Microsoft.Extensions.TimeProvider.Testing`, NetArchTest.Rules, Vitest, Playwright. Prefer the real database over mocks; use NSubstitute only where a fake is impossible.

**CI (GitHub Actions)**

- One workflow, `.github/workflows/ci.yml`, with jobs `build` → `test` → `e2e`. It runs on every pull request and on pushes to `main`.
- `build`: `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, then `npm run lint` and `npm run build` in `web/` (the runner has no global `ng`).
- `test`: unit, architecture and integration tests, plus Vitest. GitHub's Ubuntu runners ship with Docker, so Testcontainers needs no extra setup **\[Likely\]**.
- `e2e`: start the stack, run Playwright, upload traces as artifacts on failure.
- A job fails if any project references a banned package (section 2): parse `dotnet list package --format json` and grep `package.json`.
- Protect `main` as soon as S1 merges: merges only through a PR with `build` and `test` green. S12 adds `e2e` to the required checks.

## 12. Build order

Seventeen slices, one pull request each, in this order. If the sprint runs long, the Stretch slices go first, then S12, then S11. S10 can slip into sprint 2, since the food pack needs it there anyway. Never cut the tests inside S3, S5a–S5d, S6 or S10 to save time. S5b is pure C# and can be built any time after S1.

| Slice | Delivers | Tests in the same PR | Tier |
| --- | --- | --- | --- |
| S1 Skeleton | Aspire AppHost (sql on the 2025 image with a data volume, api, web), ServiceDefaults, module projects, Angular app with angular-eslint, `Directory.Build.props`, `.editorconfig`, GitHub Actions `build` and `test` jobs. Protect `main` once it merges | T12 (architecture rules); CI green | Must |
| S2 Platform | Identity, tenants, the migrate step (RCSI, the two SQL principals), Inventory DbContext with the `Locations` table (virtual kinds only so far), `IInventoryModule.ProvisionTenant`, `ITenantScopeFactory`, seed command (two tenants, 1 Admin + 1 Staff each, virtual locations; later slices extend it), login/logout/me, tenant middleware; Angular login, auth guard, app shell | Login, `401`, `me` · code-guardian review | Must |
| S3 Tenancy enforcement | Named query filters, `TenantId` interceptor and column defaults, RLS policy over the tables so far, session-context interceptor | T5, T6, T11 · code-guardian review | Must |
| S4 Master data | Items table (with its RLS predicates) and physical locations: API and screens. Seed adds two branches and about 20 retail items to the demo tenant | Validation, SKU immutability, deactivation rules · code-guardian review | Must |
| S5a Ledger schema | Documents, movements, balances (with `AllowNegative`, `LastUnitCost` and `IX_Balances_Location` from the start), sequence, triggers, checks, RLS predicates, `inventory_app` grants | T7 · code-guardian review | Must |
| S5b Costing rules | `CostingPolicy` (inflow, outflow, default adjustment cost, `LastUnitCost`) and `PostingRules` in `Domain`, pure. The estimate and revaluation branch lands in S10 | T1 unit part (V, R, A) · code-guardian review | Must |
| S5c Posting engine | `PostingEngine` in `Persistence` (section 6, steps 1–10); receipts and issues with idempotency | T1 API run of R, T2, T4, T10 · code-guardian review | Must |
| S5d Opening stock | Seed posts opening stock as receipts through `PostingEngine` with deterministic idempotency keys, never by writing balances | Running the seed twice changes nothing | Must |
| S6 Transfers and adjustments | Both document types; the clustered-key lock path | T3; T1 API run of A · code-guardian review | Must |
| S7 Stock screens | `GET /stock` (with totals), `GET /documents`, `GET /documents/{id}`; stock on hand, post-stock form, documents list and detail | T13 | Must |
| S8 Item history | Movements endpoint with running balance; balances in `GET /items/{id}`; item detail screen | Running balance matches V1–V6 | Must |
| S9 Reversals | Reversal endpoint (checks at step 7, `LastUnitCost` recomputed from the ledger), Reverse button | T8; T1 API run of V · code-guardian review | Should |
| S10 Negative stock | Location toggle (API and switch, with its lock order), `CostingPolicy` estimate and revaluation branch, negative balances in red | T15 · code-guardian review | Should |
| S11 Ledger health | Health (INV-1 to INV-5) and rebuild (exclusive ledger lock) endpoints, admin screen | T9 · code-guardian review | Should |
| S12 End to end | Playwright suite, CI `e2e` job (added to `main`'s required checks), banned-package check | T14 | Should |
| S13 Compose output | `aspire publish` to Docker Compose, committed under `deploy/`, verified running without Aspire | Manual smoke test | Stretch |
| S14 Azure | Container Apps (scale to zero) + Azure SQL free offer + Static Web Apps | Manual smoke test | Stretch |

Tag `v0.1.0` when the sprint closes: section 13 is met, minus anything this section lets slip to sprint 2. Each slice is one Jira story in project IMS (epic IMS-1; S1–S14 are IMS-2 to IMS-18).

## 13. Definition of done

Sprint 1 is done when every box below is ticked on a fresh clone.

- [ ] `dotnet run --project src/AppHost` starts sql, api and web, all healthy in the Aspire dashboard.
- [ ] The seed creates the retail demo tenant (two branches, about 20 items, opening stock) and the isolation tenant, each with an Admin, a Staff user and the three virtual locations. Running it twice changes nothing: opening stock uses deterministic idempotency keys (`seed:<tenant>:<sku>:<location>`), so the second run is a replay.
- [ ] Staff can receive, issue and transfer. Staff gets `403` on adjustments, reversals, the negative-stock toggle and item or location edits.
- [ ] Vector sets V, R, A, N and X reproduce to the fourth decimal, in unit tests and through the API.
- [ ] T2: twenty parallel issues of the last unit give exactly one success.
- [ ] T4: a repeated `Idempotency-Key` creates one document; a reused key with a different body gets `409`.
- [ ] T5 and T6: tenant A can't read or write tenant B's data through the API or through raw SQL.
- [ ] T7: `UPDATE` and `DELETE` on documents and movements fail at the database, and the API's own login can't disable triggers or RLS.
- [ ] T8: reversals restore balances, including `LastUnitCost`, exactly; the three illegal reversals are rejected.
- [ ] T9: invariants hold through the random run and the rebuild diff is empty.
- [ ] T15: the toggle can't be turned off over a negative balance.
- [ ] The stock screen's quantities, averages and values match `GET /api/stock`, with negative balances in red.
- [ ] The post-stock form works at 375 px wide.
- [ ] T14 passes in GitHub Actions, and `main` accepts only green PRs.
- [ ] No banned package appears anywhere; the CI check proves it.
- [ ] `CHANGELOG.md` has a `0.1.0` entry and `TASKS.md` lists every merged slice.

## 14. Decisions

Your answers, recorded so Claude Code and later sprints don't reopen them.

| Decision | Answer | Effect on this spec |
| --- | --- | --- |
| Jira project | IMS | One story per slice; the key alone is the branch name and starts the PR title |
| Sprint length | No date; the sprint is bound by scope | Ends at section 13; section 12 sets what slips first |
| Auth | Identity cookies now, Entra External ID later | The swap stays behind `ICurrentUser` and `ITenantContext` |
| Goal after the core | Hiring: build the AI evaluation matrix | Sprint 4+ measures accuracy, latency and cost across local and hosted open-weight models |
| Verticals | Retail demo first; food-service pack in sprint 2 | Seed is a retail shop; unit conversions and stocktakes move to sprint 2 with food |
| Repo host | GitHub | CI is GitHub Actions |
| Negative stock | Per-location toggle | Slice S10, vector sets N and X, test T15 |
| Negative-stock settlement | Revaluation movement (audit 2026-11-04, option A) | An inflow that lifts a negative balance to zero or above posts the estimate variance as a zero-quantity movement; `StockMovements` accepts quantity 0 only for it; set N rewritten, set X added |
| SQL principals | Migrate step as the server admin, API as `inventory_app` (DML only) | Triggers and RLS hold against the app's own SQL; T6 and T7 run as `inventory_app` |
| Bookkeeping | `TASKS.md` and `CHANGELOG.md` on `main` after each merge | Never inside a slice's PR |
| Version | `0.1.0` at sprint close | Replaces the tag at S9 |
| Ledger core slice | Split into S5a–S5d | Each PR deploys on its own; S5b is pure and can start early |

## 15. Log

- 2026-10-30: written.
- 2026-11-04: audited ([[Inventory System — Sprint 1 Spec.audit]]), 18 fixes applied (3 BLOCK, 15 FIX). Three NOTE items folded in where the fixes needed them (lock order, `IX_Balances_Location`, foreign ids in bodies); the rest stay open in the audit. Status: audited.
- 2026-11-09: S5c plan amended §6 steps 1, 2 and 8 (overflow guards; replay before the database checks of step 1).
