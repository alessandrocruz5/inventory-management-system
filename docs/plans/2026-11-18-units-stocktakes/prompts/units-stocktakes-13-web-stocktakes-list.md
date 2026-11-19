---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 13
unit: IMS-31
status: done
model: sonnet
effort: medium
depends_on: []
parallel_group: M
files:
  create:
    - web/src/app/stocktakes/stocktakes.service.ts
    - web/src/app/stocktakes/stocktakes.service.spec.ts
    - web/src/app/stocktakes/stocktakes.ts
    - web/src/app/stocktakes/stocktakes.html
    - web/src/app/stocktakes/stocktakes.scss
    - web/src/app/stocktakes/stocktakes.spec.ts
    - web/src/app/stocktakes/start-stocktake-dialog.ts
  modify:
    - web/src/app/app.routes.ts
    - web/src/app/app.html
  delete: []
---
# 13 · `/stocktakes`: list and start

## Task
Add the stocktakes service, the `/stocktakes` list screen with a "Start a count" dialog, the route and a nav link.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. IMS-30 is merged.
- Angular, latest stable: standalone components, signals, zoneless, `@if`/`@for`, typed reactive forms, Material.
- API (IMS-30), all under `/api`:
  - `GET /stocktakes?page&pageSize&status&locationId` returns `Paged<StocktakeSummary>`. `StocktakeSummary` is `{ id, locationId, locationCode, locationName, status: 'Draft'|'Posting'|'Posted'|'Cancelled', notes, createdAt, createdBy, lineCount, countedCount, postedDocumentId }`.
  - `POST /stocktakes { locationId, notes }` returns 201 with the detail. A second open stocktake gets `409 stocktake-open-exists` with `stocktakeId`; an inactive location gets 422.
  - `GET /stocktakes/{id}` returns the detail: `{ …summary fields, updatedAt, lines: StocktakeLine[] }`.
  - `StocktakeLine` is `{ itemId, sku, name, baseUnit, itemIsActive, expectedQuantity, countedQuantity, difference, unitCost, countedAt, countedBy, currentOnHand, averageCost, movedSinceCount }`.
  - `PUT /stocktakes/{id}/lines/{itemId} { quantity, unit?, unitCost? }` returns the detail.
  - `POST /stocktakes/{id}/post` (Admin) returns the detail.
  - `POST /stocktakes/{id}/cancel` (Admin) returns the detail.
- Read first:
  - `web/src/app/documents/documents.service.ts` (service shape with `firstValueFrom` and `Paged` from `core/api/paged.ts`).
  - `web/src/app/items/items.ts/.html/.scss` (the Sprint 2 row-list pattern with status pills; IMS-24 chose rows over AG Grid for lists like this).
  - `web/src/app/shared/status-pill.ts`.
  - `web/src/app/locations/locations.service.ts` (physical locations for the picker).
  - `web/src/app/app.routes.ts` and `web/src/app/app.html:21-107` (the nav, and how Admin-only items are gated).
  - `web/src/app/core/problems/problem-messages.ts` (`problemMessage`, `problemFieldErrors`; the slugs are already mapped).
  - `web/src/app/shared/format.ts` (dates in Asia/Manila, quantity formatting).
- Labels: plain words for a non-technical owner. Nav "Count stock". Page title "Stock counts". Status pills: Draft → "Counting", Posting → "Posting…", Posted → "Posted", Cancelled → "Cancelled".

### Steps
1. `stocktakes.service.ts`: the interfaces above plus `list(query)`, `get(id)`, `start(input)`, `count(id, itemId, input)`, `post(id)` and `cancel(id)`, with `encodeURIComponent` on path parts. The spec covers URLs, methods and bodies (HttpTestingController).
2. `stocktakes` screen:
   - A filter by location (physical, active) and a status chip filter.
   - Rows: location, status pill, "{countedCount} of {lineCount} counted", started date and time. A row click goes to `/stocktakes/:id`.
   - A "Start a count" button, visible to everyone (Staff can start), opens `start-stocktake-dialog`: a location select and optional notes.
     - On 201, navigate to the new id.
     - On `stocktake-open-exists`, show the message with an "Open it" action that navigates to `stocktakeId`.
     - Other problems go through `problemMessage`.
   - Paging like the documents list. Empty state: "No counts yet. Start one to check what's really on the shelf."
   - Works at 375 px.
3. Route: add only `{ path: 'stocktakes', canActivate: [authGuard], loadComponent: () => import('./stocktakes/stocktakes').then(m => m.Stocktakes) }`, before `'**'`. Prompt 14 creates `stocktake-detail` and adds the `stocktakes/:id` route, so the build stays green in between.
4. `app.html`: add a "Count stock" nav item to `/stocktakes` for all roles, after "Documents", in the existing markup style.
5. `stocktakes.spec.ts`:
   - Rows render the status labels.
   - The start dialog posts `{ locationId, notes }` and navigates.
   - A 409 `stocktake-open-exists` offers "Open it".

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Add npm packages or NgRx. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
```
- The existing e2e nav assertions still hold. `tests/e2e/specs/post-stock-mobile.spec.ts` checks the shell nav at 375 px (IMS-16 fixed an overflow there), so read it and check the new nav item doesn't break that layout.
