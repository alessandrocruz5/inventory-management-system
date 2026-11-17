---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 06
unit: IMS-24
status: todo
model: sonnet
effort: medium
depends_on: [IMS-22]
parallel_group: A
files:
  create: []
  modify:
    - web/src/app/items/items.ts
    - web/src/app/items/items.html
    - web/src/app/items/items.scss
    - web/src/app/items/items.spec.ts
    - "docs/Inventory System — Sprint 1 Spec.md"
  delete: []
---
# 06 · Items list with per-row actions

## Task
Replace `/items`'s AG Grid and select-then-act toolbar with the handoff's card list:
- A filter bar with search and a "Show inactive" switch.
- Wrapping rows, each linking to the item, with per-row Admin actions (Edit, Deactivate).

Record the deviation from spec §10 in the spec.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`.
- **User decision (plan D1):** the design's rows replace the grid. AG Grid and row selection leave this screen.
- IMS-22 globals in `web/src/styles.scss` (don't edit it): `.page`, `.page-header`, `.page-title`, `.page-subtitle`, `.page-actions`, `.card`, `.card__footer`, `.filter-bar`, `.mono`, `.secondary`, `.muted`, `.strong`, `.btn-sm`, `.btn-icon`, `.skeleton`, `.skeleton-row`, `.empty-state`. Material slide-toggle and buttons are already restyled globally. `StatusPill` is in `shared/status-pill.ts` (`tone`, `label` inputs). `mat-icon` defaults to Material Symbols. The shell pads the main area, so drop `:host { padding }`.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md` §2 "Items". Screen: `screenshots/desktop-02-items.png`. Prototype: `Inventory App.dc.html?screen=items`.
- Current code: `items.ts:44-181`. `openDialog(item | null)`, `deactivate()` (confirm, then `itemsService.deactivate(id)`, snackbar, reload), the debounced search and `load()` all stay. The selection state (`selected`, `canDeactivate`, `rowSelection`, `onSelectionChanged`), `columnDefs`, `theme`, `ModuleRegistry` and the AG Grid imports go.

### Layout
- `host: { class: 'page' }`.
- **Header:** `Items`, with the subtitle `{n} active items`, or `{n} items` while Show inactive is on (n = `total()`; "1 active item" / "1 item" singular). Admin only: `<button mat-flat-button type="button" (click)="openDialog(null)"><mat-icon>add</mat-icon>New item</button>`. Keep the text "New item".
- `.card` holding:
  - **`.filter-bar`:**
    - The search, `flex: 1 1 240px; max-width: 360px`: an outline `mat-form-field` with a `matPrefix` `search` icon and `<input matInput type="search" [formControl]="search" placeholder="Search SKU or name" aria-label="Search SKU or name">`, with no `mat-label`.
    - The existing `mat-slide-toggle` "Show inactive", aligned to the field's centre line.
  - **The rows:** `@for` over `rows()`. Each row is `.item-row`: flex-wrap, `padding: 12px 16px`, border-bottom divider, hover subtle, `align-items: center`, `gap: 8px 16px`.
    - **Left:** an `<a class="item-main" [routerLink]="['/items', item.id]">` (flex, gap 16, `min-width: 0`, ink, no underline) holding the SKU (`.mono`, 13 px secondary, fixed 112 px, ellipsis) and the name (500, wraps).
    - **Right group** (`margin-left: auto`, flex, gap 12, `align-items: center`): the unit (32 px wide, muted), then `<app-status-pill>` (Active `good`, Inactive `neutral`), then the Admin actions.
    - **Admin actions:** `<button mat-stroked-button class="btn-sm" type="button" [attr.aria-label]="'Edit ' + item.sku" (click)="openDialog(item)">Edit</button>`. Then, **only when `item.isActive`**, `<button mat-stroked-button class="btn-icon" type="button" [attr.aria-label]="'Deactivate ' + item.sku" (click)="deactivate(item)"><mat-icon>block</mat-icon></button>`. This keeps today's rule that only active items can be deactivated.
    - The link and the buttons are siblings; there are no nested interactive elements.
  - **Loading:** while `loading()` and there are no rows, 5 `.skeleton-row`s. When it isn't loading and there are none, `<p class="empty-state">No items match.</p>`.
  - **`.card__footer`** with the existing `mat-paginator`.
- `deactivate(item: Item)` now takes the row's item. The dialog text and the snackbars are unchanged.
- `items.scss`: the row and filter internals only, under 2 kB.

### Spec doc (`docs/Inventory System — Sprint 1 Spec.md`)
- §10 table, `/items` row (line ~602): change "Grid with search" to "List with search; per-row edit and deactivate". Leave the rest of the row unchanged.
- §15 "Log": append `- 2026-11-17: Sprint 2 UI redesign ([[2026-11-17-ui-redesign]]) shows /items as a list with per-row actions instead of an AG Grid (D1).`

### `items.spec.ts`
Drop the `AgGridAngular` override, then:
- Keep the "loads the first page of active items" case as is.
- **Staff:** with two rows returned, there is no "New item", no `button[aria-label^="Edit "]` and no `button[aria-label^="Deactivate "]`.
- **Admin:** "New item" is enabled. Each row has `Edit <sku>`. `Deactivate <sku>` exists for the active row only (fixture: one active, one inactive).
- Each row's `a.item-main` has `href` `/items/<id>`. Provide `provideRouter([])`.
- Clicking `Deactivate <sku>` opens `ConfirmDialog` with that item's SKU in the message: mock `MatDialog.open` with `afterClosed: () => of(false)`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. Not `items.service.ts`, `item-dialog.*`, `styles.scss` or `package.json`. Leave AG Grid installed, because Stock and Documents use it.
- Change search debounce, the active filter, paging or the dialog behaviour.
- Add dependencies, or create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)/web"
npm run lint && npm run build && npm test -- --watch=false
! grep -q "ag-grid" src/app/items/items.ts src/app/items/items.html
```
- `/items` at 1280 px matches `desktop-02-items.png`. At 375 px the rows wrap with no sideways scroll, Edit and Deactivate measure ≥ 44 px (Deactivate 44×44) from IMS-22's mobile block (plan D19; don't override it in `items.scss`), and Staff sees no actions.
