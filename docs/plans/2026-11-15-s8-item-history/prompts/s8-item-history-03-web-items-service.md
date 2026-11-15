---
type: prompt
plan: "[[2026-11-15-s8-item-history]]"
id: 03
unit: IMS-12
status: todo
model: haiku
effort: low
depends_on: [02]
parallel_group: C
files:
  create: []
  modify:
    - web/src/app/items/items.service.ts
    - web/src/app/items/items.service.spec.ts
  delete: []
---
# 03 · Web items service: detail and movements

## Task
Extend `ItemsService` with the types for `GET /api/items/{id}` (now with `balances`) and `GET /api/items/{id}/movements`. `get` returns `ItemDetail`; add `movements(id, page, pageSize)`. Extend the spec.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-12`. Angular (standalone, signals, zoneless), Vitest through `npm test`. Prompts 01–02 added the API; JSON is camelCase and decimals are numbers.
- `web/src/app/items/items.service.ts`: the `Item` interface is at `:6-13`; `get` at `:45-47` returns `Promise<Item>`. `get` has no caller outside its spec, so changing its return type to a superset breaks nothing. `Paged<T>` is in `web/src/app/core/api/paged.ts` and is already imported.
- Add after `Item` (export each):
  ```ts
  export interface ItemBalance {
    locationId: string;
    locationCode: string;
    locationName: string;
    onHand: number;
    stockValue: number;
    averageCost: number | null;
    lastUnitCost: number | null;
  }

  export interface ItemDetail extends Item {
    balances: ItemBalance[];
  }

  export interface ItemMovement {
    movementId: number;
    documentId: string;
    documentType: DocumentType;
    lineNo: number;
    postedAt: string;
    postedBy: string;
    direction: 'In' | 'Out';
    locationId: string;
    locationCode: string;
    locationName: string;
    counterpartLocationId: string;
    counterpartLocationCode: string;
    counterpartLocationName: string;
    quantityChange: number;
    valueChange: number;
    runningOnHand: number;
    runningValue: number;
  }
  ```
  `DocumentType` comes from `../shared/format` (`import { DocumentType } from '../shared/format';`).
- Change `get(id: string): Promise<ItemDetail>` (same URL, `this.http.get<ItemDetail>(…)`).
- Add, copying `list`'s `HttpParams` style (`:32-43`):
  ```ts
  movements(id: string, page: number, pageSize: number): Promise<Paged<ItemMovement>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return firstValueFrom(
      this.http.get<Paged<ItemMovement>>(`/api/items/${encodeURIComponent(id)}/movements`, { params }),
    );
  }
  ```
- `web/src/app/items/items.service.spec.ts`: the `get encodes the id` test (`:84-88`) flushes `item`; flush `{ ...item, balances: [] }` instead and expect `(await promise).balances` to equal `[]`. Add:
  - `movements encodes the id and sends page and pageSize`: `service.movements('a/b', 2, 50)`; `httpMock.expectOne((r) => r.url === '/api/items/a%2Fb/movements')`; method `GET`; params `page` `'2'`, `pageSize` `'50'`; flush `{ items: [], page: 2, pageSize: 50, total: 0 }` and expect the promise to resolve to it.

## Do not
- Change `list`, `create`, `update`, `deactivate` or the `Item` interface.
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `items.service.spec.ts` passes with the new test; every other spec passes unmodified.
