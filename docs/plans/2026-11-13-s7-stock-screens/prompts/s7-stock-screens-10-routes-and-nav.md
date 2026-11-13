---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 10
unit: IMS-11
status: todo
model: sonnet
effort: low
depends_on: [09]
parallel_group: J
files:
  create: []
  modify:
    - web/src/app/app.routes.ts
    - web/src/app/app.html
    - web/src/app/app.spec.ts
  delete:
    - web/src/app/home/home.ts
---
# 10 · Routes, landing page and navigation

## Task
Route the S7 screens, make Stock on hand the landing page (spec §10), add the navigation links with the plain verbs, update the shell spec, and delete the unused `Home` placeholder.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11`. Angular 22 standalone, lazy `loadComponent` routes, functional guards.
- Components from prompts 06–09: `Stock` (`web/src/app/stock/stock.ts`), `PostStock` and `postStockGuard` (`web/src/app/documents/post-stock.ts`, `post-stock.guard.ts`), `Documents` (`web/src/app/documents/documents.ts`), `DocumentDetail` (`web/src/app/documents/document-detail.ts`). Check each exported class name before importing.
- **`app.routes.ts`** (today `:4-22`). Spec §10 names the route `/stock` (audit FIX 2). Replace the `''` route (`Home`) with these two:
  ```ts
  { path: '', pathMatch: 'full', redirectTo: 'stock' },
  { path: 'stock', canActivate: [authGuard], loadComponent: () => import('./stock/stock').then((m) => m.Stock) },
  ```
  Then add, before `'**'`:
  ```ts
  { path: 'documents', canActivate: [authGuard], loadComponent: () => import('./documents/documents').then((m) => m.Documents) },
  { path: 'documents/new/:type', canActivate: [authGuard, postStockGuard], loadComponent: () => import('./documents/post-stock').then((m) => m.PostStock) },
  { path: 'documents/:id', canActivate: [authGuard], loadComponent: () => import('./documents/document-detail').then((m) => m.DocumentDetail) },
  ```
  `documents/new/:type` must come before `documents/:id`. `login.ts:48` already navigates to `/`, which now redirects to `/stock`; so does `'**'` (it redirects to `''`).
- **Delete `web/src/app/home/home.ts`.** Only `app.routes.ts:9` imported it (confirm with `grep -rn "home/home" web/src`).
- **`app.html`** (today `:5-8`). The nav becomes, in this order:
  ```html
  <a mat-button routerLink="/stock" routerLinkActive="active">Stock</a>
  <a mat-button routerLink="/documents/new/receipt" routerLinkActive="active">Receive stock</a>
  <a mat-button routerLink="/documents/new/issue" routerLinkActive="active">Use or sell stock</a>
  <a mat-button routerLink="/documents/new/transfer" routerLinkActive="active">Move stock</a>
  @if (auth.isAdmin()) {
    <a mat-button routerLink="/documents/new/adjustment" routerLinkActive="active">Correct stock</a>
  }
  <a mat-button routerLink="/documents" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Documents</a>
  <a mat-button routerLink="/items" routerLinkActive="active">Items</a>
  <a mat-button routerLink="/locations" routerLinkActive="active">Locations</a>
  ```
  The labels are fixed by spec §10 and used by S12/T14. The toolbar already wraps (`app.scss` `flex-wrap: wrap`), so `app.scss` doesn't change. `app.ts` already exposes `auth` and imports `RouterLink`/`RouterLinkActive`, so it doesn't change either.
- **`app.spec.ts`** (today `:14-58`). Its `AuthService` stub is `{ user, logout }`. Add `isAdmin = signal(false)` to the stub (reset in `beforeEach`), or the template's `auth.isAdmin()` throws. Keep every existing fact. Its first `querySelector('button')` is Sign out; the nav uses `<a>`, so that still holds. Add:
  1. `links to the stock screens in the toolbar`: as Staff, `a[href="/stock"]` "Stock", `a[href="/documents/new/receipt"]` "Receive stock", `…/issue` "Use or sell stock", `…/transfer` "Move stock", `a[href="/documents"]` "Documents".
  2. `shows Correct stock to Admin only`: with `isAdmin` false there's no `a[href="/documents/new/adjustment"]`; with it true the link exists, with the text "Correct stock".

## Do not
- Change `app.ts`, `app.scss`, the guards or any component.
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
grep -rn "home/home" web/src || echo "no Home references"
```
- `npm run build` emits lazy chunks for stock, documents, post-stock and document-detail, and the initial bundle stays within the `angular.json` budget.
- `app.spec.ts` passes with the two new facts, and every other spec passes unmodified.
