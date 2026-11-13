---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 09
unit: IMS-11
status: todo
model: sonnet
effort: medium
depends_on: [08]
parallel_group: I
files:
  create:
    - web/src/app/documents/documents.ts
    - web/src/app/documents/documents.html
    - web/src/app/documents/documents.spec.ts
    - web/src/app/documents/document-detail.ts
    - web/src/app/documents/document-detail.html
    - web/src/app/documents/document-detail.spec.ts
  modify: []
  delete: []
---
# 09 · Documents list and detail screens

## Task
Create the standalone `Documents` (list) and `DocumentDetail` components (spec §10 `/documents`, `/documents/:id`). Prompt 10 routes them; don't touch the routes here.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11`. Angular 22 standalone, signals, zoneless, `@if`/`@for`, typed reactive forms. AG Grid Community 36.2.0, Angular Material. No new package. Put the small styles inline (`styles:`), as `web/src/app/items/item-dialog.ts:25-32` does. No `.scss` files.
- Copy the grid + paginator + `load()` pattern from `web/src/app/items/items.ts:1-181` and `items.html`, and the spec pattern from `web/src/app/items/items.spec.ts:1-45` (remove `AgGridAngular`, add `CUSTOM_ELEMENTS_SCHEMA`, `vi.fn()` stubs).
- Services and helpers (prompt 05): `DocumentsService.list({ type?, postedFrom?, postedTo?, page, pageSize })` → `Paged<DocumentSummary>`, `DocumentsService.get(id)` → `DocumentDetail` (`web/src/app/documents/documents.service.ts`). From `web/src/app/shared/format.ts`: `formatMoney`, `formatQuantity`, `formatManilaDateTime`, `manilaDayRange`, `documentTypeLabel`, `reasonLabel`, `postedByLabel`, `DocumentType`. `AuthService.user()` gives the current `id` for `postedByLabel` (plan D3).
- **`Documents` (list):**
  - `<h1>Documents</h1>`. Filters: a type `mat-select` ("All types" plus the five types with `documentTypeLabel`), and two `<input matInput type="date">` fields, "From" and "To" (native date inputs, so no Material date adapter is needed). Changing any filter resets to page 1 and reloads with `...manilaDayRange(from, to)` (UTC `Z` instants, half-open; plan D8).
  - Columns: Posted (`formatManilaDateTime(postedAt)`, minWidth 170), Type (`documentTypeLabel`), Reason (`reasonLabel`, or empty), Reference, Lines (`lineCount`, right-aligned), Posted by (`postedByLabel(postedBy, auth.user()?.id)`), Status (`Reversed` when `reversedByDocumentId`, `Reverses another` when `reversesDocumentId`, else empty).
  - Row click (`(rowClicked)`) → `router.navigate(['/documents', row.id])`.
  - Paginator `[25, 50, 100]`. Snack bar `problemMessage(...)` on a load error.
- **`DocumentDetail`:**
  - Read `id` from `ActivatedRoute.paramMap` (subscribe with `takeUntilDestroyed()`, so following a reversal link to another document reloads), then `DocumentsService.get(id)`. On a `404`, show `problemMessage(err.error)` ("That record doesn't exist, or you can't see it.") and a link back to `/documents`.
  - Header: `<h1>{{ documentTypeLabel(type) }}</h1>`, then Posted (`formatManilaDateTime`), Posted by (`postedByLabel`), Reason (adjustments), Reference, and Notes (each only when present).
  - Reversal links, either way (spec §9): `Reversed by <a routerLink="/documents/{{reversedByDocumentId}}">this reversal</a>` and `Reverses <a routerLink="/documents/{{reversesDocumentId}}">this document</a>`.
  - Lines: a plain responsive list or `<table>` (no grid needed). Columns: Item (`SKU · Name`), From → To (location codes; the names in a `title`), Qty (`formatQuantity` + `baseUnit`), Unit cost (`formatMoney`, or `—` when null), Total cost (`formatMoney`). Below 600 px, show each line as a stacked block (375 px must work).
  - No Reverse button (S9 adds it here).
- **Specs:**
  - `documents.spec.ts`: it loads page 1 with no filters. Choosing type `Issue` reloads with `type: 'Issue', page: 1`. Setting From `2026-10-30` and To `2026-10-30` reloads with `postedFrom: '2026-10-29T16:00:00.000Z'` and `postedTo: '2026-10-30T16:00:00.000Z'`. A row click navigates to `/documents/<id>`. Check the column formatters directly: the posted-at getter gives `30 Oct 2026, 2:14 PM` for `2026-10-30T06:14:00Z`; posted-by gives `System` for the zero Guid and `You` for the current user.
  - `document-detail.spec.ts`: it renders the type label, the lines with `₱` unit and total costs and quantities without trailing zeros (stub `quantity: 10` → `10`, `unitCost: 100` → `₱100.00`, `totalCost: 1000` → `₱1,000.00`). It renders both reversal links when the ids are set, and neither when they're null. A `404` shows the not-found message. A new `paramMap` id triggers a second `get`.

## Do not
- Add the Reverse button (S9), item links (S8), routes or navigation (prompt 10).
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `documents.spec.ts` and `document-detail.spec.ts` pass, and every existing spec passes unmodified.
