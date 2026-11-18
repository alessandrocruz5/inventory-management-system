---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 04
unit: U1
status: todo
model: sonnet
effort: medium
depends_on: [02]
parallel_group: D
files:
  create:
    - web/src/app/items/item-unit-dialog.ts
    - web/src/app/items/item-unit-dialog.html
    - web/src/app/items/item-unit-dialog.spec.ts
  modify:
    - web/src/app/items/items.service.ts
    - web/src/app/items/items.service.spec.ts
    - web/src/app/items/item-detail.ts
    - web/src/app/items/item-detail.html
    - web/src/app/items/item-detail.spec.ts
  delete: []
---
# 04 · Item detail: units section (Admin add and deactivate)

## Task
On `/items/:id`, show the item's units. An Admin can add a unit through a dialog and deactivate one after confirming.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. Angular, latest stable: standalone components, signals, zoneless, `@if`/`@for`, typed reactive forms, Angular Material.
- API (prompt 02):
  - `GET /api/items/{id}` now returns `units: [{ code, factor, isActive }]`.
  - `POST /api/items/{id}/units` takes `{ code, factor }` and returns 201 with the unit. 400 errors come on `code` or `factor`.
  - `POST /api/items/{id}/units/{code}/deactivate` returns 200 with the unit. Both POSTs are Admin only.
- Read first:
  - `web/src/app/items/items.service.ts:7-110`: the interfaces, plus `firstValueFrom(this.http…)` and `encodeURIComponent`.
  - `item-detail.ts/.html`: the current balances section and its Sprint 2 styling.
  - `item-dialog.ts/.html`: the dialog pattern. `:82` maps `problemFieldErrors(body)` onto controls with `setErrors({ server })`, and `problemMessage` gives everything else.
  - `web/src/app/shared/confirm-dialog.ts`.
  - How `items.ts` hides Admin actions: the auth service's role signal.
- Conventions (spec §10):
  - Quantities drop trailing zeros. Use `formatQuantity` in `web/src/app/shared/format.ts`.
  - Hide what the role can't use.
  - Plain words: the section heading is "Units", and each row reads "1 case = 12 pc".

### Steps
1. `items.service.ts`:
   - `export interface ItemUnit { code: string; factor: number; isActive: boolean }`.
   - `ItemDetail` gains `units: ItemUnit[]`.
   - Add `addUnit(itemId, input: { code: string; factor: number }): Promise<ItemUnit>` and `deactivateUnit(itemId, code): Promise<ItemUnit>`, with `encodeURIComponent` on both path parts.
   - Spec: request URLs and bodies, in the existing `items.service.spec.ts` style (HttpTestingController).
2. `item-unit-dialog`: a standalone dialog with a typed form `{ code: string, factor: number | null }`.
   - Required. `factor` > 0 with at most 4 decimals; reuse `amountValidator` from `web/src/app/documents/posting-form.ts:157` if its contract fits, otherwise mirror its rules locally.
   - Hint: "How many {baseUnit} in one {code}?"
   - On submit, call `addUnit`. Server field errors go onto their controls, other problems into a message line. Close with the created unit.
   - Spec: validation, the server error mapping, and closing on success.
3. `item-detail`:
   - A "Units" card under the balances. List each unit as "1 {code} = {factor} {baseUnit}", with an "Inactive" status pill (`shared/status-pill`) when inactive.
   - Admin only: an "Add unit" button opens the dialog, then reloads the item. A per-row "Deactivate" (active rows only) asks for confirmation with "Deactivate {code}? It stays on past documents; you can't post in it any more.", then reloads.
   - Empty state: "No other units. Stock is counted in {baseUnit}."
   - Must work at 375 px with no horizontal scroll.
   - Spec: Staff sees no add or deactivate controls, Admin does, the list text, and the empty state.

## Do not
- Touch files outside `files`. If you must, stop and report why. The post-stock form is prompt 08.
- Add npm packages. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
```
- With the AppHost running, an Admin can add `case` = 12 to an item on `/items/:id` and deactivate it. Staff sees the list only.
