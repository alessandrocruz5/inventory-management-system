---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 05
unit: IMS-23
status: todo
model: sonnet
effort: medium
depends_on: [04]
parallel_group: B
files:
  create:
    - web/src/app/documents/documents.scss
  modify:
    - web/src/app/documents/documents.ts
    - web/src/app/documents/documents.html
    - web/src/app/documents/documents.spec.ts
  delete: []
---
# 05 · Documents list screen

## Task
Restyle `/documents` to the handoff:
- A page header with a subtitle.
- One card holding a grid-layout filter bar (Type, From, To), the themed AG Grid with type icons, mono references and status pills on desktop, or stacked rows below 760 px, and the pagination row.

Filters, paging, row click and errors keep their current behaviour.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`.
- **Copy the patterns prompt 04 just applied to `web/src/app/stock/stock.ts` and `stock.html`:**
  - `host: { class: 'page' }`, the `.page-header` markup, the `.card` / `.filter-bar` / `.card__footer` structure.
  - The label-above `mat-select` with `aria-labelledby` (plan D6).
  - The Angular component cell renderer declared inside the screen file, never an HTML string (plan D11).
  - `StatusPillCell` for the status column, `appGridTheme`, `domLayout="autoHeight"`, `[rowClass]="'clickable'"`.
  - The `@if (isNarrow())` list instead of the grid (plan D5), with skeleton rows and an empty state.
- IMS-22 globals in `web/src/styles.scss` (don't edit it): `.page*`, `.card*`, `.filter-bar`, `.field`, `.field-label`, `.mono`, `.muted`, `.secondary`, `.strong`, `.num`, `.stacked-list`, `.stacked-row`, `.skeleton*`, `.empty-state`. Helpers: `documentStatus(doc)` → `{ tone, label }` and the existing `documentTypeLabel`, `reasonLabel`, `postedByLabel`, `formatManilaDateTime`, `manilaDayRange` in `shared/format.ts`; `injectIsNarrow()` in `shared/layout.ts`.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md` §5 "Documents". Screens: `screenshots/desktop-05-documents.png` and `mobile-documents.png`. Prototype: `Inventory App.dc.html?screen=documents`.
- Current code: `documents.ts:31-63` (inline `styles`, which move to the new `documents.scss`), `documents.ts:75-105` (columns), `documents.html` (filters with `mat-label`s).

### Layout
- **Header:** `Documents`, with the subtitle "Every posting, newest first. Open one to see its lines or reverse it." No action.
- **Filter bar:** add a modifier class in `documents.scss` that sets `display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 180px), 220px)); gap: 12px 16px`. It has three `.field`s, labels above:
  - **Type:** `mat-select` with `aria-labelledby`, options "All types" plus `typeLabel(t)` (Received / Used or sold / Moved / Corrected / Reversal).
  - **From** and **To:** `<label class="field-label" for="documents-from">` above `<input matInput type="date" id="documents-from">`, the same for `to`, with the same `(change)` handlers.
- **Desktop grid columns, in this order:**
  1. **Posted:** `formatManilaDateTime`, `cellClass: 'secondary tabular'`, `minWidth: 170`.
  2. **Type:** a component renderer showing an 18 px `mat-icon` (muted) plus the label (500). Keep the icon map as a `const TYPE_ICONS: Record<DocumentType, string>` in `documents.ts`: Receipt `south_west`, Issue `north_east`, Transfer `swap_horiz`, Adjustment `tune`, Reversal `undo`.
  3. **Reference:** `field: 'reference'`, `cellClass: 'mono'`, "—" when null.
  4. **Reason:** `reasonLabel`, "—" when empty.
  5. **Lines:** `lineCount`, right-aligned.
  6. **Posted by:** `postedByLabel(postedBy, auth.user()?.id)`. `Guid.Empty` shows "System" (IMS-11).
  7. **Status:** `valueGetter` → `documentStatus(data)` with `cellRenderer: StatusPillCell`. Posted / Reversed / Reverses another replaces today's blank, Reversed or Reverses-another text.
- **Mobile rows:** `<a class="stacked-row" [routerLink]="['/documents', d.id]">`.
  - A 36×36 icon tile (`#F2F1EF` = `var(--ims-chip)`, radius 8, the type icon).
  - Then a flex column: line 1 is the type label (500) with `<app-status-pill>` pushed right; line 2 is `REF · n lines` (13 px; when there is no reference, just `n lines`; "1 line" singular); line 3 is `posted · by` (12 px muted).
- `.card__footer` with the existing `mat-paginator`.
- `documents.scss`: the filter grid, the type cell and the mobile row internals only, under 2 kB. Point `styleUrl` at it and delete the inline `styles`.

### `documents.spec.ts`
Keep every existing test passing. Add:
- The status `valueGetter` gives Posted / Reversed / Reverses another, through a typed internals cast to `columnDefs`.
- With `BreakpointObserver` mocked to match, one `a.stacked-row` per document with `href` `/documents/<id>`, and the text `REF · 2 lines`.
- The Type `mat-select` is labelled by the "Type" label, and the From/To inputs have `<label for>`s.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. Not `styles.scss` or `documents.service.ts`.
- Change filter semantics (`manilaDayRange`, reset to page 1) or the request parameters.
- Use HTML-string cell renderers or `innerHTML`.
- Add dependencies, or create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `/documents` at 1280 px matches `desktop-05-documents.png`, and at 390 px matches `mobile-documents.png`, with no sideways scroll at 375 px.
- CI `e2e` passes.
