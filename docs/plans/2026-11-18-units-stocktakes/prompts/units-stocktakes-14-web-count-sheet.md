---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 14
unit: IMS-31
status: done
model: sonnet
effort: high
depends_on: [13]
parallel_group: N
files:
  create:
    - web/src/app/stocktakes/stocktake-detail.ts
    - web/src/app/stocktakes/stocktake-detail.html
    - web/src/app/stocktakes/stocktake-detail.scss
    - web/src/app/stocktakes/stocktake-detail.spec.ts
    - web/src/app/stocktakes/count-line-dialog.ts
  modify:
    - web/src/app/app.routes.ts
  delete: []
---
# 14 · `/stocktakes/:id`: the count sheet, Post and Cancel

## Task
Build the count sheet: one row per item showing the expected, counted and difference values. Tap a row to count it, in any of the item's units. Anyone can add an item. An Admin can post or cancel, and a posted count links to its document.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't.
- Prompt 13 added `web/src/app/stocktakes/stocktakes.service.ts` with its types (read it). IMS-29 added unit options to the post-stock form: `web/src/app/documents/post-stock.ts` loads `ItemsService.get(id)` to get `units`. Copy that unit-select pattern.
- API behaviour (IMS-30):
  - Count `PUT …/lines/{itemId} { quantity, unit?, unitCost? }` returns the full detail. Errors are 400 on `quantity`, `unit` or `unitCost`; `409 stocktake-not-open`; and `422 inactive-item`.
  - Post (Admin) returns the detail with `status` Posted and `postedDocumentId`, or `null` when nothing differed. Errors:
    - 400 with remapped `lines[j].quantity` / `lines[j].unitCost`. `j` is the index in `lines` as returned, already in display order.
    - 400 `lines`.
    - 422 `insufficient-stock` (`itemId`, base units), `inactive-item` and `inactive-location`.
    - After any error the stocktake is back to Draft.
  - Cancel (Admin).
  - `unitCost` is per base unit, and is needed only when a count is above the expected quantity and there's no stock to average.
- Read first:
  - `web/src/app/documents/document-detail.ts/.html/.scss` (Sprint 2 detail layout: back link, title pill, meta grid, notices).
  - `web/src/app/documents/post-stock.ts/.html` (the item autocomplete and the unit select).
  - `web/src/app/shared/{confirm-dialog,format,status-pill}.ts`.
  - The auth service's role signal (how `document-detail` shows Reverse only to Admin).

### Steps
1. Route: add `{ path: 'stocktakes/:id', canActivate: [authGuard], loadComponent: () => import('./stocktakes/stocktake-detail').then(m => m.StocktakeDetail) }` after `stocktakes`.
2. `stocktake-detail`:
   - **Header:** a back link to `/stocktakes`, the location name, a status pill (the labels from prompt 13), started by and at, and notes. A summary line "{counted} of {total} counted · {n} differences". When Posted, a link "View the correction" goes to `/documents/{postedDocumentId}`, or the text "No differences — nothing was posted."
   - **Rows** (stacked cards at 375 px):
     - SKU and name.
     - "Expected {expected} {baseUnit}", once counted.
     - "Counted {counted}", or "Not counted".
     - The difference, signed and colour-coded: red when short, using the existing negative styling; neutral when 0.
     - A "Stock moved since this count — recount?" notice when `movedSinceCount`.
     - An estimated value "≈ ₱{|difference| × averageCost}", only when `averageCost` isn't null. Format with the shared currency formatter.
   - **Count dialog** (`count-line-dialog.ts`), opened by tapping a row while Draft: a quantity, a unit select (base plus active units from `ItemsService.get`), and an optional "Unit cost per {baseUnit}" shown when the entered quantity × factor > expected **and** `currentOnHand <= 0`. On save, call `count` and replace the whole detail from the response. Server field errors go to their controls.
   - **"Add an item"** (Draft): the item autocomplete from post-stock opens the count dialog for that item.
   - **Admin actions** (Draft or Posting):
     - "Post count" confirms with "Post {n} corrections to {location}? This changes stock." and calls `post`.
     - On a 400 with `lines[j]…` keys, scroll to and highlight row j with the message.
     - On `insufficient-stock`, highlight the row whose `itemId` matches.
     - "Cancel count" (Draft only) confirms, then calls `cancel`.
     - In `Posting` state, show "Posting didn't finish. Post again to complete it." with the Post button.
   - Staff sees no Post or Cancel.
   - Read-only once Posted or Cancelled.
   - Disable buttons while a request is in flight.
3. `stocktake-detail.spec.ts`:
   - Rows render the expected, counted and difference values.
   - Staff sees no Post or Cancel, and Admin does.
   - Saving a count sends the unit when it isn't base.
   - A remapped `lines[1].unitCost` error highlights the second row.
   - The Posted state shows the document link.
   - The no-differences state shows its text.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Re-implement unit conversion rules beyond multiplying for the unit-cost hint; the server decides.
- Add npm packages. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
```
- With the AppHost running at 375 px: Staff starts a count at QC, counts `RICE-WHT` as 4 `sack` (100 kg), and an Admin posts it. The document shows the correction.
