---
type: prompt
plan: "[[2026-11-05-s4-master-data]]"
id: 08
unit: IMS-5
status: todo
model: sonnet
effort: high
depends_on: [07]
parallel_group: H
files:
  create:
    - web/src/app/shared/confirm-dialog.ts
    - web/src/app/items/items.ts
    - web/src/app/items/items.html
    - web/src/app/items/items.scss
    - web/src/app/items/items.spec.ts
  modify:
    - web/package.json
    - web/package-lock.json
    - web/src/app/app.routes.ts
    - web/src/app/app.html
    - web/src/app/app.ts
    - web/src/app/app.spec.ts
  delete: []
---
# 08 · Web: AG Grid, Items page, routes and nav

## Task
Install AG Grid Community and build the `/items` page: a grid with search, an inactive toggle, server paging, an Admin-only New/Edit/Deactivate (with a confirm step), and snackbar feedback. Add the route and the toolbar nav link.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`; the app is in `web/` (Angular 22.2, **zoneless**, standalone, signals, `@if`/`@for`, Material 22.2.5, Vitest). You are on branch `IMS-5`; don't switch it. Prompt 07 added `ItemsService` (`list/get/create/update/deactivate`, the `Item` and `ItemQuery` types), `ItemDialog` (data `{ item: Item | null }`, closes with the saved `Item` or `undefined`), `core/api/paged.ts` and `problemFieldErrors`. `AuthService.isAdmin` is a computed signal (`core/auth/auth.service.ts:20`). `problemMessage(body)` gives the friendly text for any problem.

**1. Install, pinned and exact** (user decision; both are on CLAUDE.md's allowed list). From `web/`:
```bash
npm view ag-grid-community@36.2.0 version && npm view ag-grid-angular@36.2.0 peerDependencies   # expect @angular/core >= 20
npm install --save-exact ag-grid-community@36.2.0 ag-grid-angular@36.2.0
```
Use 36.2.0, not 36.3.0: 36.3.0 is one day old, and the two packages must match. If 36.2.0 won't install, stop and report; don't pick another version on your own.

**2. `items/items.ts` + `.html` + `.scss`:** `export class Items`, a standalone component.
- **AG Grid 36:** register modules once at module scope in this file: `ModuleRegistry.registerModules([AllCommunityModule]);` (from `ag-grid-community`). Use the Theming API: the `[theme]` input is `themeQuartz`, and there are no AG Grid CSS imports. Import `AgGridAngular` from `ag-grid-angular` **only in this file**, so AG Grid stays in the lazy `/items` chunk. Never import it from `app.ts` or `app.config.ts`; the initial bundle budget is 500 kB warn / 1 MB error (`angular.json`).
- Columns are `ColDef<Item>[]`: SKU, Name, Unit (`baseUnit`) and Status (`Active`/`Inactive` through a `valueGetter`), with `flex` and a `minWidth` so the grid scrolls horizontally at 375 px. Set `rowSelection` to `{ mode: 'singleRow', checkboxes: false, enableClickSelection: true }` and track the selected row in a signal through `(selectionChanged)`. Turn off AG Grid's own pagination.
- State is signals: `rows`, `total`, `loading`, `selected`. A typed `FormControl<string>` for search feeds `valueChanges.pipe(debounceTime(300), distinctUntilChanged())` into a reload; use `takeUntilDestroyed()`. A "Show inactive" `mat-slide-toggle`: off → `active: true`; on → `active` omitted.
- Server paging: a `mat-paginator` below the grid with `[length]="total()"`, page size options `[25, 50, 100]` and default 25. `(page)` reloads with `page: pageIndex + 1`. Reset to page 1 when the search or the toggle changes.
- **Admin only** (`@if (auth.isAdmin())`; spec §10 "hide buttons the role can't use"):
  - "New item" opens `ItemDialog` with `{ item: null }`.
  - "Edit" (disabled until a row is selected) opens it with the selected item.
  - "Deactivate" (disabled unless the selected item is active) opens `ConfirmDialog`, then `items.deactivate(id)`.
  - On success, show a `MatSnackBar` message ("Item saved" / "Item deactivated") and reload. On an error, show the snackbar with `problemMessage(err.error)`. This shows S5a's future `stock-not-zero` message with no change here.
- Page title "Items". Layout is a column. The search field and the toggle wrap on narrow screens. Give the grid a fixed height (e.g. `height: 480px`), because AG Grid needs one.

**3. `shared/confirm-dialog.ts`:** a tiny standalone `ConfirmDialog` with inline template and data `{ title: string; message: string; confirm: string }`. It has a Cancel button (closes `false`) and a confirm button (closes `true`). S9 and S11 will reuse it.

**4. `app.routes.ts`.** Before `'**'`, add `{ path: 'items', canActivate: [authGuard], loadComponent: () => import('./items/items').then((m) => m.Items) }`.

**5. `app.html` + `app.ts`.** In the toolbar, after `.tenant`, add `<nav class="nav"><a mat-button routerLink="/items" routerLinkActive="active">Items</a></nav>`. Use **anchors, not buttons**: `app.spec.ts:42` takes the first `button` as Sign out. Import `RouterLink` and `RouterLinkActive` in `app.ts`. The toolbar already wraps and hides the email below 480 px (`app.scss`); leave the SCSS alone.

**6. Specs.**
- `app.spec.ts`: add one test. When signed in, the toolbar has an `a[href="/items"]` with the text "Items". Keep the existing three tests.
- `items/items.spec.ts`: AG Grid doesn't render reliably under jsdom, so `TestBed.overrideComponent(Items, { remove: { imports: [AgGridAngular] }, add: { schemas: [CUSTOM_ELEMENTS_SCHEMA] } })`. Provide an `ItemsService` stub (`list: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0 })`), and an `AuthService` stub `{ isAdmin: signal(false) }`. Don't import `@angular/platform-browser/animations` or `@angular/animations`: Material 22 needs no animations provider, and the package isn't installed. Assert:
  - The first load calls `list` with `{ active: true, page: 1, pageSize: 25 }` and no `search`.
  - Staff sees no "New item", "Edit" or "Deactivate" button.
  - Admin sees all three, and "Edit"/"Deactivate" are disabled with nothing selected.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add any npm package other than these two (in particular `@angular/animations`), or use AG Grid Enterprise features (`ag-grid-enterprise`, server-side row model).
- Add the Locations page (prompt 09) or change `Home`.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `npm run build` shows no budget warning, and the AG Grid code sits in a lazy chunk, not `main`: check the build's "Lazy chunk files" table.
- `grep -n '"ag-grid-community": "36.2.0"\|"ag-grid-angular": "36.2.0"' package.json` prints 2 lines.
- `! grep -rn 'ag-grid' src/app/app.ts src/app/app.config.ts src/main.ts`
