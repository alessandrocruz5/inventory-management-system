---
type: prompt
plan: "[[2026-11-15-s10-negative-stock]]"
id: 09
unit: IMS-14
status: todo
model: haiku
effort: low
depends_on: []
parallel_group: C
files:
  create: []
  modify:
    - web/src/app/stock/stock.ts
    - web/src/app/stock/stock.spec.ts
    - web/src/styles.scss
  delete: []
---
# 09 · Web: red negatives on `/stock`

## Task
On the stock-on-hand grid, show negative quantities and values in red (spec §10 `:600`: "negative balances in red").

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-14`. Stop if `git branch --show-current` says otherwise. Angular + AG Grid Community 36.2.0 (`ag-grid-angular`), Vitest.
- `web/src/app/stock/stock.ts` defines `columnDefs: ColDef<StockRow>[]`; the Qty column has `field: 'onHand'` and the Value column `field: 'stockValue'`. `stock.spec.ts` reads `columnDefs` through the `StockInternals` cast (see `it('formats quantity and money on the column defs', …)` around `:124`).
- AG Grid renders cells outside Angular's emulated view encapsulation, so `stock.scss` rules can't reach them. The style goes in the global `web/src/styles.scss`.

### Changes
- In `stock.ts`, add a module-level constant and use it on the `onHand` and `stockValue` column defs only:
  ```ts
  /** Negative balances show in red (spec §10). Applies to the pinned totals row too. */
  const NEGATIVE_CELL: ColDef<StockRow>['cellClassRules'] = {
    'stock-negative': (params) => typeof params.value === 'number' && params.value < 0,
  };
  ```
  `cellClassRules: NEGATIVE_CELL` on both columns. Leave the other columns alone.
- In `web/src/styles.scss`, append:
  ```scss
  // AG Grid cells sit outside component styles: negative stock on /stock (spec §10).
  .ag-cell.stock-negative {
    color: var(--mat-sys-error);
  }
  ```
- In `stock.spec.ts`, add `it('marks negative quantity and value cells', …)`: find the `onHand` and `stockValue` defs, call `cellClassRules['stock-negative']` with `{ value: -2 }` → true, with `{ value: 0 }` and `{ value: 5 }` → false, and with `{ value: undefined }` → false. Cast the params as the existing spec does for `valueFormatter`. Also assert the `averageCost` def has no `cellClassRules`.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Use `::ng-deep`, inline `cellStyle`, or new packages.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
grep -n 'stock-negative' src/app/stock/stock.ts src/styles.scss
```
