---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 08
unit: IMS-29
status: todo
model: sonnet
effort: high
depends_on: [07]
parallel_group: H
files:
  create: []
  modify:
    - web/src/app/documents/posting-form.ts
    - web/src/app/documents/posting-form.spec.ts
    - web/src/app/documents/post-stock.ts
    - web/src/app/documents/post-stock.html
    - web/src/app/documents/post-stock.spec.ts
  delete: []
---
# 08 · Post-stock form: choose a unit per line

## Task
On `/documents/new/:type`, once a line's item is chosen, show a unit select: the base unit plus the item's active units. Send `unit` only when it isn't the base unit, convert to base units wherever the form compares with on-hand or values at average cost, and map `lines[i].unit` errors.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. Angular, latest stable: standalone components, signals, zoneless, typed reactive forms, Angular Material.
- API (prompts 06–07):
  - Every posting line accepts an optional `unit`. Quantity and `unitCost` are in that unit.
  - A missing `unit` means the base unit. Sending the base unit's name works but changes the RequestHash, so **omit** it for base.
  - 400 keys gain `lines[i].unit`. A non-exact conversion comes back on `lines[i].quantity` or `lines[i].quantityDelta`.
  - `insufficient-stock`'s `available` and `requested` are in **base** units.
- `GET /api/items/{id}` returns `units: [{ code, factor, isActive }]` and `baseUnit` (`ItemsService.get(id)` and the `ItemUnit` type from prompt 04, in `web/src/app/items/items.service.ts`).
- Read first:
  - `web/src/app/documents/posting-form.ts`:
    - `:24` `PostingLine`
    - `:77-123` `buildPostingBody`
    - `:126-139` `linesOverOnHand`
    - `:197-300` `PostingErrors`, `LINE_KEY` and `postingErrors`
    - `:303-325` `postingTotal`
  - `post-stock.ts` (484 lines; how a line's item is chosen and `onHand[i]` is filled) and `post-stock.html`.
  - Their specs.
- The on-hand warning and the "Value at average cost" total use base units, so multiply the entered quantity by the factor before comparing.

### Steps
1. `posting-form.ts` (pure helpers, spec-tested):
   - `PostingLine` gains `unit: string | null` (null = base) and `factor: number` (1 for base).
   - `buildPostingBody`: add `unit: l.unit` to a line object **only when** `l.unit !== null`. With null, the body is byte-identical to today's.
   - `linesOverOnHand`: compare `quantity × factor` with `have`. Round the product to 4 dp, AwayFromZero, using the rounding helper already used by `postingTotal`.
   - `postingTotal`: for lines not using a unit cost, `quantity × factor × averageCost`. For unit-cost lines it stays `quantity × unitCost`, in the entered unit.
   - `LINE_KEY`: add `unit` to the alternation. `PostingErrors.lines[i]` gains a `'unit'` field.
   - `insufficient-stock` text (`:276`): keep the numbers in base units and make the unit the item's base unit. The caller passes the base unit label.
2. `post-stock.ts/.html`:
   - On item selection, fetch `ItemsService.get(itemId)` once per item id (cache it in a `Map` signal). Store `baseUnit` and the active units.
   - Show a compact `mat-select` "Unit" next to the quantity. Options: the base unit, then each active unit as "{code} ({factor} {baseUnit})". Default to base. Changing the unit updates the line's `unit` and `factor`.
   - The unit-cost label reads "Unit cost per {unit}".
   - Map the `unit` server error onto the select.
   - Must work at 375 px. The existing mobile e2e (`tests/e2e/specs/post-stock-mobile.spec.ts`) must still pass, so don't rename labels it uses (read it).
3. Specs:
   - `posting-form.spec.ts`: a base line has no `unit` key, and a case line has `unit: 'case'`. The over-on-hand check uses the factor. The total uses the factor. `lines[0].unit` maps.
   - `post-stock.spec.ts`: choosing an item with units shows the select, and submitting in `case` sends `unit: 'case'`.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Change `IdempotencyKey` handling (key reuse rules, spec §10).
- Add npm packages. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
```
- With the AppHost running: receive 1 `sack` of `RICE-WHT` @ 1,250 at QC → the document shows 25 kg costing ₱1,250.00.
