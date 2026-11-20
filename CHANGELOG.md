# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Sprint 1 ends at version 0.1.0.

## [Unreleased]

### Added

- Inventory read contract: `IInventoryReader` and snapshot records in `Inventory.Contracts`, so other modules read stock data without touching Inventory internals (IMS-33).

## [0.3.0] - 2026-11-19

### Added

- Units: `ItemUnits` (per-item purchase units with a base-unit factor), Admin add/deactivate, base-unit change blocked once an item has movements or units, and a Units card on item detail (IMS-28).
- Posting in units: optional `unit` on posting lines converted to base quantities, inflow cost per entered unit (`PostingLine.UnitFactor`, vector set U), and a unit select on the post-stock form (IMS-29).
- Stocktakes: `Stocktakes`/`StocktakeLines` with row-level security, start (prefilled), list, detail, count, cancel and an Admin post that makes one count-correction document, exactly once, with crash healing (IMS-30).
- Stocktake screens: Count stock list and count sheet with units, moved-since-count notice and Admin Post/Cancel, and a Playwright end-to-end spec (IMS-31).

## [0.2.0] - 2026-11-18

### Changed

- UI: neutral design foundation (tokens, fonts, Material overrides, `appGridTheme`), shared `StatusPill` primitives, and a responsive app shell with sidebar, top bar and mobile drawer (IMS-22).
- UI: Stock and Documents lists restyled with a themed AG Grid, status pills and stacked rows below 760 px (IMS-23).
- UI: Items is a row list with per-row Admin actions (no AG Grid), and Item detail and Locations are restyled (IMS-24).
- UI: Document detail and Ledger health restyled (IMS-25).
- UI: Post-stock form restyled (IMS-26).

## [0.1.0] - 2026-11-17

### Added

- Skeleton: Aspire AppHost (`sql`, `api`, `web`), ServiceDefaults, five module projects, Angular app, and architecture tests (T12) (IMS-2).
- Platform: Identity cookie auth (login, logout, me), tenants, `Api migrate` and `Api seed` modes, tenant middleware, and Angular login, guard and shell (IMS-3).
- Platform: AppHost ordering `sql` → `migrate` → `seed` → `api` → `web`, and a T12 rule against `Migrate*` calls outside the migrate step (IMS-3).
- Inventory: `Locations` and `ProvisionTenantAsync` (IMS-3).
- Tenancy: `TenantId` column default and save interceptor, named `Tenant` query filter, and row-level security on `security.TenantPolicy` with a session-context interceptor (IMS-4).
- Tenancy: `Locations` is tenant-owned, with route guard (T5), catalog (T6), isolation (T11) and rollback tests, plus a T12 rule against bare `IgnoreQueryFilters()` (IMS-4).
- Master data: `inventory.Items` with row-level security, Items API (Admin writes, per-tenant SKU, `version` concurrency, idempotent deactivate), physical Locations API (system rows read-only), and a demo seed of two tenants with 20 items (IMS-5).
- Master data: Angular `/items` (AG Grid) and `/locations` screens (IMS-5).
- Ledger: `StockDocuments`, `StockMovements`, `StockBalances` and the `MovementIds` sequence with row-level security, append-only triggers and `DENY UPDATE, DELETE` for `inventory_app` (IMS-6).
- Ledger: SKU immutability and `stock-not-zero` deactivation guards for items and locations, under `HOLDLOCK, FORCESEEK` locks (IMS-6).
- Costing: pure `Inventory.Domain` model (`CostingPolicy`, `PostingRules.Post` and `.Reverse`, typed `PostingRejectedException`s), with the V, R and A vector sets as unit tests (IMS-7).
- Posting: `POST /documents/receipts` and `/issues` on a shared `PostingEngine`, with `Idempotency-Key` replay, overflow guards and retries set to 3 (IMS-8).
- Seed: `Api seed` posts 31 opening-stock receipts through the posting engine, idempotent on re-run (IMS-9).
- Posting: `POST /documents/transfers` (Staff, Admin) and `/adjustments` (Admin, with `reasonCode`), costed at the source's moving average, with a concurrent transfer test (T3) and the set A vectors through the API (IMS-10).
- Stock screens: `GET /stock` with totals, `GET /documents` and `GET /documents/{id}`, and Angular stock-on-hand, post-stock form, and documents list and detail screens (IMS-11).
- Item history: `GET /items/{id}` returns the balance per physical location, `GET /items/{id}/movements` returns a paged stock card with a windowed running balance, and an Angular `/items/:id` screen opened from a `/stock` row (IMS-12).
- Reversals: `POST /documents/{id}/reversal` (Admin, idempotent) with latest-only, already-reversed and cannot-reverse-reversal checks under the balance locks, `LastUnitCost` recomputed from the ledger, and a Reverse button on the document detail (IMS-13).
- Negative stock: estimate costing with revaluation of negative balances, a per-location `allowNegativeStock` toggle (`PUT /locations/{id}`, `422 negative-balances-exist`), red negatives on `/stock`, and an "Allow negative stock" switch on `/locations` (IMS-14).
- Ledger health: Admin `GET /ledger/health` (invariants INV-1 to INV-5) and `POST /ledger/rebuild` under an exclusive ledger lock, a shared `LedgerLock`, a seeded random invariant test (T9), and an `/admin/ledger` screen (IMS-15).
- End to end: Playwright suite in `tests/e2e` (T14), a CI `e2e` job, a banned-package check in `build`, and a README section (IMS-16).
- Deploy: `aspire publish` output in `deploy/` that runs with `docker compose` alone, with an nginx `web` image (SPA fallback, `/api` proxy) and a README section (IMS-17).
- Azure: hand-written Bicep and `deploy.sh` deploy `web`, an internal-only `api`, `migrate` and `seed` jobs and Azure SQL (free offer) to Container Apps, with nginx Host forwarding and a Playwright `E2E_BASE_URL` override (IMS-18).
- Platform: Data Protection keys persisted in `platform.DataProtectionKeys`, and a contained `inventory_app` user on Azure SQL with a guarded RCSI migration (IMS-20).

### Fixed

- Shell navigation no longer overflows at 375 px (found by the first end-to-end CI run) (IMS-16).
- Aspire packages bumped to 13.6.5 for GHSA-f7vh-h5g3-7jrh (IMS-19).
