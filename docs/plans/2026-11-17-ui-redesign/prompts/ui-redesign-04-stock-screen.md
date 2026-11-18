---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 04
unit: IMS-23
status: done
model: sonnet
effort: high
depends_on: [IMS-22]
parallel_group: A
files:
  create: []
  modify:
    - web/src/app/stock/stock.ts
    - web/src/app/stock/stock.html
    - web/src/app/stock/stock.scss
    - web/src/app/stock/stock.spec.ts
  delete: []
---
# 04 · Stock on hand screen

## Task
Restyle `/stock` to the handoff:
- A page header with a subtitle and a "+ Receive stock" action.
- One card holding the filter bar (the Location select and a "Total value" headline), the themed AG Grid on desktop or stacked rows below 760 px, and the pagination row.

The stock status is derived per row. Data, filters, paging, row click and error snackbars keep their current behaviour.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`. IMS-22 is merged into `develop` and provides the following:
  - Global classes in `web/src/styles.scss`: `.page`, `.page-header`, `.page-title`, `.page-subtitle`, `.page-actions`, `.card`, `.card__footer`, `.filter-bar`, `.field`, `.field-label`, `.num`, `.mono`, `.muted`, `.secondary`, `.strong`, `.tone-bad`, `.stacked-list`, `.stacked-row`, `.skeleton`, `.skeleton-row`, `.empty-state`, and the AG Grid helpers `.ag-row.clickable`, `.ag-row-pinned`, `.ag-cell.cell-strong`, `.ag-cell.stock-negative`. **Don't edit `styles.scss`** (plan D7).
  - `appGridTheme` (`shared/grid-theme.ts`), `StatusPill` (`shared/status-pill.ts`), `StatusPillCell` (`shared/status-pill-cell.ts`), `injectIsNarrow()` (`shared/layout.ts`), and `stockStatus(onHand)` → `{ tone, label }` (`shared/format.ts`).
  - Shell: the main area already has the page padding, so drop this component's `:host { padding }`.
  - `mat-icon` uses Material Symbols by default, and form fields default to `outline` with dynamic subscript.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md` §1 "Stock on hand", "Tables", "Inputs and selects", "Responsive". Screens: `screenshots/desktop-01-stock.png` and `mobile-stock.png`. Prototype: `Inventory App.dc.html?screen=stock`.
- Current code: `stock.ts:35-68` (column defs and `NEGATIVE_CELL`), `stock.ts:81-83` (the pinned totals row from `result.totals.stockValue`), `stock.ts:96-100` (row click goes to `/items/:id`), and `stock.html`.

### Hard contracts (e2e: `tests/e2e/support/app.ts:57-65, 98-104`, `t14-stock-flow.spec.ts:36-58`)
- The location filter stays a `mat-select` whose accessible name contains **"Location"**, and its options read `CODE · Name` (the helper picks `/^CODE ·/`). Use the label-above pattern (plan D6): `<label class="field-label" id="stock-location-label">Location</label>` above a `mat-form-field` with **no** `mat-label`, and `<mat-select aria-labelledby="stock-location-label" …>`.
- On desktop, keep AG Grid (spec §10, plan D4). Keep the column `field`s `onHand`, `averageCost` and `stockValue` (e2e reads `[col-id="onHand"]` etc. and expects `7`, `₱100.00`, `₱700.00`), with no pinned columns, so each data row is one `role=row`. The SKU text must be inside the row, because e2e filters rows with `hasText: sku`.
- Render the grid **or** the stacked list with `@if (isNarrow())`, never both (plan D5).

### Layout
- `host: { class: 'page' }` in the component metadata. Header: `<h1 class="page-title">Stock on hand</h1>` and the subtitle `{n} balances across {m} locations`, where `n = total()` and `m = locations().length`, with singular forms "1 balance" and "1 location". On desktop only, a `page-actions` link `<a mat-flat-button routerLink="/documents/new/receipt"><mat-icon>add</mat-icon>Receive stock</a>`; hide it when `isNarrow()` (README: the top bar covers it).
- `.card` containing:
  - `.filter-bar`: on the left, the Location `.field` (`flex: 1 1 220px; max-width: 320px`) with "All locations" plus `CODE · Name` options, unchanged. On the right (`margin-left: auto; text-align: right`), "Total value" (12/500 muted) above `formatMoney(stockValue())` (20/28 600, tabular). On mobile the headline wraps under the select, right-aligned, as in `mobile-stock.png`.
  - **Desktop grid:** `[theme]="theme"` (= `appGridTheme`), `domLayout="autoHeight"`, `rowHeight` 64 (two-line Item cell), `[rowClass]="'clickable'"`, the existing `[pinnedBottomRowData]`, `[loading]` and `(rowClicked)`. Columns:
    1. **Item**, `colId: 'item'`, `flex: 2`, `minWidth: 220`: an Angular **component** cell renderer (plan D11, never an HTML string), declared in `stock.ts` and not exported, that shows `itemName` (14/500) above `sku` (`.mono`, 12/16, muted). On the pinned row (`params.node.rowPinned`) it shows "Total". Data is user input; render it with interpolation only.
    2. **Location**: `field: 'locationCode'`, `cellClass: 'mono secondary'`, 13 px.
    3. **Status**: `colId: 'status'`, `valueGetter` → `stockStatus(data.onHand)` (undefined for the pinned row), `cellRenderer: StatusPillCell`, `sortable: false`.
    4. **Qty**: `field: 'onHand'`, `type: 'rightAligned'`, `cellClass: 'cell-strong'`, the existing `NEGATIVE_CELL` and `valueFormatter`.
    5. **Unit**: `field: 'baseUnit'`, `cellClass: 'muted'`.
    6. **Avg cost**: `field: 'averageCost'`, right-aligned, `cellClass: 'secondary'`, the existing formatter ("—" when null).
    7. **Value**: `field: 'stockValue'`, right-aligned, `NEGATIVE_CELL`, the existing formatter.
    Keep `NEGATIVE_CELL` (`stock-negative` class, now bad fg) on Qty and Value; spec §10 calls for red negatives.
  - **Mobile list,** when `isNarrow()`: `<ul class="stacked-list">`, each row `<li><a class="stacked-row" [routerLink]="['/items', r.itemId]">`.
    - Left (`min-width: 0`): `itemName` (500, ellipsis), then `SKU · LOC` (`.mono`, 12/16, muted), then `formatMoney(stockValue)` (12, tabular, `.tone-bad` when negative).
    - Right (`text-align: right`, flex column, end-aligned, gap 6): qty (600, `.tone-bad` when negative) with the unit in muted, then `<app-status-pill>` from `stockStatus`.
    - While `loading()` with no rows, show 5 `.skeleton-row`s, each with two `.skeleton` bars (60% and 35% wide). When it isn't loading and there are no rows, show `<p class="empty-state">No stock to show.</p>`.
  - `.card__footer` holding the existing `mat-paginator` (same inputs and outputs).
- `stock.scss`: only what the globals don't cover (the filter-bar total block, the mobile row internals). Remove the old `:host` padding, `h1`, `.filters`, `.location` and `.grid { height: 480px }` rules. Keep it under 2 kB.

### `stock.spec.ts`
Keep every existing test passing. It overrides `AgGridAngular` with `CUSTOM_ELEMENTS_SCHEMA`; keep that pattern. Add:
- The subtitle reads "2 balances across 1 location" for a fixture with total 2 and one physical location.
- The `Status` column's `valueGetter` returns `In stock`/`Out of stock`/`Negative` for 5 / 0 / -1. Reach `columnDefs` through a typed internals cast, as other specs do.
- With `BreakpointObserver` mocked to match (see `shared/layout.ts` JSDoc), the stacked list renders one `a.stacked-row` per row with `href` `/items/<id>`, and `ag-grid-angular` is absent.
- The location `mat-select` has `aria-labelledby="stock-location-label"`, and that label's text is "Location".

## Do not
- Touch files outside the `files` list. If you must, stop and report why. In particular, not `styles.scss`, `stock.service.ts` or the e2e suite.
- Change the data calls, query params, paging, or the error and snackbar handling.
- Use AG Grid `cellRenderer` functions that return HTML strings, or `innerHTML`.
- Add dependencies, or create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)/web"
npm run lint && npm run build && npm test -- --watch=false
```
- With the AppHost running, `/stock` at 1280 px matches `desktop-01-stock.png` and at 390 px matches `mobile-stock.png`. At 375 px, `document.documentElement.scrollWidth <= 375`.
- CI `e2e` passes (T14 reads the stock row's `col-id` cells).
