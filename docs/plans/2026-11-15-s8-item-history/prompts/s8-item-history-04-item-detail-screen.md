---
type: prompt
plan: "[[2026-11-15-s8-item-history]]"
id: 04
unit: IMS-12
status: done
model: sonnet
effort: medium
depends_on: [03]
parallel_group: D
files:
  create:
    - web/src/app/items/item-detail.ts
    - web/src/app/items/item-detail.html
    - web/src/app/items/item-detail.spec.ts
  modify:
    - web/src/app/app.routes.ts
  delete: []
---
# 04 · Item detail screen and route

## Task
Add the `/items/:id` screen (any signed-in role): the item, its balance per location, and the paged movement history with running balance, each row linking to its document (spec §10). Register the route.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-12`. Angular standalone components, signals, zoneless, `@if`/`@for`, functional guards. Vitest via `npm test`. Angular Material is installed; component styles warn above 4 kB (`angular.json` budgets), so keep styles inline and short.
- Prompt 03 added to `web/src/app/items/items.service.ts`: `ItemDetail` (`Item` + `balances: ItemBalance[]`), `ItemBalance` (`locationId, locationCode, locationName, onHand, stockValue, averageCost|null, lastUnitCost|null`), `ItemMovement` (`movementId, documentId, documentType, lineNo, postedAt, postedBy, direction: 'In'|'Out', locationId, locationCode, locationName, counterpartLocationId, counterpartLocationCode, counterpartLocationName, quantityChange, valueChange, runningOnHand, runningValue`), `get(id): Promise<ItemDetail>` and `movements(id, page, pageSize): Promise<Paged<ItemMovement>>`.
- **Copy the pattern of `web/src/app/documents/document-detail.ts` and `.html`** (read both first): `ActivatedRoute.paramMap` with `takeUntilDestroyed(inject(DestroyRef))`, the `requestedId` guard against stale responses, `problemMessage(err instanceof HttpErrorResponse ? err.error : null)` into an `error` signal, and its inline `styles` (the table, `.num`, and the `data-label` card layout below 600 px). Import the service type as `ItemDetail as ItemDetailData` because the component class is named `ItemDetail` (as `document-detail.ts:15` does).
- Format helpers from `web/src/app/shared/format.ts`: `formatMoney`, `formatQuantity`, `formatManilaDateTime`, `documentTypeLabel`, `postedByLabel(postedBy, this.auth.user()?.id)` (`AuthService` from `core/auth/auth.service`).
- **`item-detail.ts`** — `@Component({ selector: 'app-item-detail', imports: [RouterLink, MatPaginatorModule], templateUrl: './item-detail.html', styles: … })` `export class ItemDetail`:
  - Signals: `item` (`ItemDetailData | null`), `movements` (`ItemMovement[]`), `total`, `pageIndex` (0), `pageSize` (25), `loading`, `error` (`string | null`). `pageSizeOptions = [25, 50, 100]`.
  - On each `paramMap` id: reset `pageIndex` to 0, clear `item`/`movements`/`error`, then load `get(id)` and `movements(id, 1, pageSize())` together (`Promise.all`); apply results only if `requestedId === id`.
  - `onPage(event: PageEvent)`: set `pageIndex`/`pageSize`, reload only the movements (`movements(id, pageIndex + 1, pageSize)`), errors into `error`.
  - Helpers: `signedQuantity(q)` → `q > 0 ? '+' + formatQuantity(q) : formatQuantity(q)`; `signedMoney(v)` → `v > 0 ? '+' + formatMoney(v) : formatMoney(v)`; `counterpart(m)` → `m.direction === 'In' ? 'from ' + m.counterpartLocationCode : 'to ' + m.counterpartLocationCode`; `postedBy(m)` → `postedByLabel(m.postedBy, this.auth.user()?.id)`.
- **`item-detail.html`**:
  - `@if (error(); as message)`: `<p role="alert">{{ message }}</p>` and `<a routerLink="/stock">Back to stock</a>` (the `not-found` slug already maps to "That record doesn't exist, or you can't see it.").
  - `@if (item(); as it)`: `<h1>{{ it.sku }} · {{ it.name }}</h1>`; a `<dl>` with Unit (`baseUnit`) and, when `!it.isActive`, Status "Inactive".
  - `<h2>Stock by location</h2>`: if `it.balances` is empty, `<p>No stock at any location yet.</p>`; else a table: Location (`code · name`), Qty (`formatQuantity(onHand) baseUnit`, `.num`), Avg cost (`—` when null, else `formatMoney`), Value (`formatMoney(stockValue)`). Every `<td>` has a `data-label`.
  - `<h2>History</h2>`: if `movements()` is empty, `<p>No movements yet.</p>`; else a table with `@for (m of movements(); track m.movementId + m.direction)`: Posted (`formatManilaDateTime(m.postedAt)`), Document (`<a [routerLink]="['/documents', m.documentId]">{{ documentTypeLabel(m.documentType) }}</a>`), Location (`m.locationCode`, `title` = name), From / to (`counterpart(m)`), Qty (`signedQuantity(m.quantityChange)`), Value (`signedMoney(m.valueChange)`), Balance (`formatQuantity(m.runningOnHand)`), Balance value (`formatMoney(m.runningValue)`), Posted by (`postedBy(m)`). Then `<mat-paginator [length]="total()" [pageIndex]="pageIndex()" [pageSize]="pageSize()" [pageSizeOptions]="pageSizeOptions" (page)="onPage($event)" />`.
- **`app.routes.ts`**: after the `items` route (`:13-17`), add `{ path: 'items/:id', canActivate: [authGuard], loadComponent: () => import('./items/item-detail').then((m) => m.ItemDetail) }`. Keep `'**'` last.
- **`item-detail.spec.ts`** — copy the setup of `web/src/app/documents/document-detail.spec.ts:1-66` (`BehaviorSubject` of `convertToParamMap({ id: 'i1' })`, `provideRouter([])`, `{ provide: ItemsService, useValue: { get, movements } }`, `{ provide: AuthService, useValue: { user: signal({ id: 'u-1' }) } }`, `{ provide: ActivatedRoute, useValue: { paramMap: params } }`, `provideNoopAnimations()` only if the paginator needs it). Fixtures: an item `SKU-1 · Widget`, unit `pc`, one balance `MAIN` 10 / 1230 / avg 123; two movements: `{ documentId: 'd6', documentType: 'Receipt', direction: 'In', counterpartLocationCode: 'SUP', quantityChange: 6, valueChange: 750, runningOnHand: 10, runningValue: 1230, postedBy: '00000000-0000-0000-0000-000000000000', postedAt: '2026-10-30T06:14:00Z' }` and `{ documentId: 'd5', documentType: 'Transfer', direction: 'Out', counterpartLocationCode: 'B', quantityChange: -4, valueChange: -480, runningOnHand: 6, runningValue: 720, postedBy: 'u-1' }`. Tests:
  1. `renders the item, its balances and its history`: text contains `SKU-1 · Widget`, `₱123.00`, `₱1,230.00`, `Received`, `Moved`, `from SUP`, `to B`, `+6`, `-4`, `+₱750.00`, `System`, `You`, `30 Oct 2026, 2:14 PM`; and not `10.0000`.
  2. `links each history row to its document`: `href`s include `/documents/d6` and `/documents/d5`.
  3. `shows empty states`: `balances: []` and no movements → "No stock at any location yet." and "No movements yet.".
  4. `shows Inactive for an inactive item`.
  5. `shows the not-found message with a link back on 404`: `get` rejects with `HttpErrorResponse({ status: 404, error: { type: '/problems/not-found' } })` → the message and an `href` of `/stock`.
  6. `pages the history`: call the component's `onPage({ pageIndex: 1, pageSize: 25, length: 30 })` (cast to an internals interface, as `documents.spec.ts:11-17` does) → `movements` last called with `('i1', 2, 25)`, and `get` was called once.
  7. `reloads from page 1 when the id changes`: after paging, `params.next(convertToParamMap({ id: 'i2' }))` → `get` last called with `'i2'` and `movements` with `('i2', 1, 25)`.

## Do not
- Add AG Grid to this screen (plan D7: real links per row), a store, or a new shared helper.
- Touch `items.service.ts`, `items.ts`, `shared/format.ts` or other screens.
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- The 7 `item-detail.spec.ts` tests pass; `npm run build` has no budget error; every other spec passes unmodified.
