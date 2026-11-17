---
type: prompt
plan: "[[2026-11-15-s8-item-history]]"
id: 05
unit: IMS-12
status: done
model: haiku
effort: low
depends_on: [04]
parallel_group: E
files:
  create: []
  modify:
    - web/src/app/stock/stock.ts
    - web/src/app/stock/stock.html
    - web/src/app/stock/stock.spec.ts
  delete: []
---
# 05 · Stock row click

## Task
A click on a `/stock` grid row opens `/items/:itemId` (spec §10: "row click opens the item"). The pinned totals row does nothing.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-12`. Prompt 04 registered the `items/:id` route.
- Copy the documents list exactly:
  - `web/src/app/documents/documents.ts:3,68` (`import { Router } from '@angular/router';`, `private readonly router = inject(Router);`) and `:143-147`:
    ```ts
    protected onRowClicked(row: DocumentSummary | undefined): void {
      if (row) {
        void this.router.navigate(['/documents', row.id]);
      }
    }
    ```
  - `web/src/app/documents/documents.html:35`: `(rowClicked)="onRowClicked($event.data)"` on `<ag-grid-angular>`.
- In `web/src/app/stock/stock.ts` (class `Stock`, injections at `:23-25`): inject `Router` and add
  ```ts
  protected onRowClicked(row: Partial<StockRow> | undefined): void {
    if (row?.itemId) {
      void this.router.navigate(['/items', row.itemId]);
    }
  }
  ```
  The pinned totals row's data is `{ stockValue }` (`totalsRow`, `:72-74`), with no `itemId`, so it's ignored.
- In `web/src/app/stock/stock.html:15-23`: add `(rowClicked)="onRowClicked($event.data)"` to `<ag-grid-angular>`.
- In `web/src/app/stock/stock.spec.ts`: add `onRowClicked(row: Partial<StockRow> | undefined): void;` to the `StockInternals` interface (`:12-17`); provide `{ provide: Router, useValue: { navigate } }` with `const navigate = vi.fn();` reset in `beforeEach` (as `documents.spec.ts:21,39` and its providers do). Tests, copying `documents.spec.ts:88-93`:
  1. `navigates to the item on row click`: `onRowClicked(row(5))` → `navigate` called with `['/items', 'i']` (the fixture's `itemId` is `'i'`, `:24-30`).
  2. `ignores the totals row`: `onRowClicked({ stockValue: 100 })` and `onRowClicked(undefined)` → `navigate` not called.

## Do not
- Change the columns, the totals, the location filter or `stock.service.ts`.
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- The 2 new `stock.spec.ts` tests pass; every existing `stock.spec.ts` test and every other spec passes unmodified.
