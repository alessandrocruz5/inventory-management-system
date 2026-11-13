---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 06
unit: IMS-11
status: todo
model: sonnet
effort: medium
depends_on: [05]
parallel_group: F
files:
  create:
    - web/src/app/stock/stock.ts
    - web/src/app/stock/stock.html
    - web/src/app/stock/stock.scss
    - web/src/app/stock/stock.spec.ts
  modify: []
  delete: []
---
# 06 · Stock on hand screen

## Task
Create the standalone `Stock` component, the "Stock on hand" screen (spec §10 `/stock`): an AG Grid of item, location, qty, unit, avg cost and value, a location filter, a pinned totals row from the API's `totals`, and a paginator. Prompt 10 routes it at `stock`, the landing page (`''` redirects there); don't touch the routes here.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11`. Angular 22 standalone, signals, zoneless, `@if`/`@for`, typed reactive forms. AG Grid Community 36.2.0 (exact-pinned; already installed). Angular Material.
- Copy `web/src/app/items/items.ts:1-181` and `items.html` closely: `ModuleRegistry.registerModules([AllCommunityModule])`, `themeQuartz`, `[rowData]="rows()"`, `[loading]="loading()"`, `[pagination]="false"` + `mat-paginator` (`pageSizeOptions` `[25, 50, 100]`), signals for `rows`/`total`/`loading`/`pageIndex`/`pageSize`, the `load()` with a `try/catch` snack bar of `problemMessage(err instanceof HttpErrorResponse ? err.error : null)`.
- Services from prompt 05: `StockService.list({ locationId?, page, pageSize }): Promise<StockPage>` (`web/src/app/stock/stock.service.ts`; `StockPage` has `items`, `total` and `totals.stockValue`); `formatMoney`, `formatQuantity` from `web/src/app/shared/format.ts`. Locations: `LocationsService.list({ page: 1, pageSize: 100 })` from `web/src/app/locations/locations.service.ts` (`Location` has `id`, `code`, `name`, `isSystem`, `isActive`).
- Screen:
  - `<h1>Stock on hand</h1>`.
  - Filter: a `mat-select` "Location" with an "All locations" option (value `null`) plus each physical location (`!isSystem`; include inactive ones, labelled `CODE · Name`). Changing it sets `pageIndex` 0 and reloads. Load the locations once in the constructor.
  - Columns (`ColDef<StockRow>[]`): Item (`valueGetter` `` `${sku} · ${itemName}` ``, flex 2, minWidth 200), Location (`locationCode`, minWidth 110), Qty (`onHand`, `valueFormatter` → `formatQuantity`, `type: 'rightAligned'`), Unit (`baseUnit`, minWidth 80), Avg cost (`averageCost`, `formatMoney`, or `—` when null, right-aligned), Value (`stockValue`, `formatMoney`, right-aligned).
  - Totals row: `[pinnedBottomRowData]="totalsRow()"`, a computed signal giving one row with the Item cell `Total` and `stockValue = totals.stockValue` from the API; the other cells are blank (leave `onHand`/`averageCost` undefined and make the formatters return `''` for `undefined`). Never sum the page on the client (spec audit `:26`).
  - No row click (plan D4; S8 adds it). No red negatives (S10).
  - Any role may view, and there are no action buttons.
- `stock.scss`: copy `items.scss`'s grid sizing. Keep it well under the 4 kB component-style budget (`angular.json:37-47`). Use one column for the filter below 600 px.
- **`stock.spec.ts`**: copy `web/src/app/items/items.spec.ts:1-45`. Override the component to remove `AgGridAngular` and add `CUSTOM_ELEMENTS_SCHEMA`; provide `StockService` and `LocationsService` as `{ list: vi.fn() }` stubs. Facts:
  1. On create, it loads page 1 with pageSize 25 and no `locationId`.
  2. Choosing a location reloads with that `locationId` and `page: 1`. Call the component's protected handler directly if driving `mat-select` is awkward, as `items.spec.ts` does.
  3. `totalsRow()` carries the API's `totals.stockValue`, not a client sum: stub two rows of 10 and 20 with `totals.stockValue` 999, and expect 999.
  4. Only physical locations are offered (an `isSystem` row is filtered out).
  5. A failing `list` shows the snack bar text from `problemMessage`, or at least doesn't throw.
  - Test the formatters on the column defs directly, by calling `valueFormatter` with `{ value: 10 }` → `'10'` and `{ value: 1234.5 }` → `'₱1,234.50'`.

## Do not
- Add routes or navigation (prompt 10). Add a row click or negative-stock styling (S8/S10).
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `stock.spec.ts` passes, and every existing spec passes unmodified.
