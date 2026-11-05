---
type: prompt
plan: "[[2026-11-05-s4-master-data]]"
id: 09
unit: IMS-5
status: todo
model: sonnet
effort: medium
depends_on: [05, 08]
parallel_group: I
files:
  create:
    - web/src/app/locations/locations.service.ts
    - web/src/app/locations/locations.service.spec.ts
    - web/src/app/locations/locations.ts
    - web/src/app/locations/locations.html
    - web/src/app/locations/locations.scss
    - web/src/app/locations/locations.spec.ts
    - web/src/app/locations/location-dialog.ts
    - web/src/app/locations/location-dialog.html
    - web/src/app/locations/location-dialog.spec.ts
  modify:
    - web/src/app/app.routes.ts
    - web/src/app/app.html
    - web/src/app/app.spec.ts
  delete: []
---
# 09 · Web: Locations page

## Task
Build `/locations`. Physical locations get Admin-only create, edit and deactivate. The three system locations are read-only with a "System" chip. Add the route and the nav link. Copy the Items feature from prompts 07–08.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`; the app is in `web/`. You are on branch `IMS-5`; don't switch it. **Read and mirror these:** `web/src/app/items/items.service.ts` (+ spec), `items/item-dialog.ts`/`.html` (+ spec), `items/items.ts` (signals, `MatSnackBar`, `ConfirmDialog`, `problemMessage`, `@if (auth.isAdmin())`), `shared/confirm-dialog.ts`, `core/api/paged.ts`, `core/problems/problem-messages.ts` (`problemFieldErrors`), and `app.routes.ts`/`app.html`/`app.spec.ts` as prompt 08 left them.

**API contract** (prompt 05):
- `GET /api/locations?page=&pageSize=` → `Paged<Location>`, physical first, then the system rows (`ADJ`, `CUS`, `SUP`)
- `POST /api/locations` `{ code, name }` → `201`
- `PUT /api/locations/{id}` `{ code, name, version }` → `200`; stale → `409 concurrency-conflict`
- `POST /api/locations/{id}/deactivate` → `200`
- On a system location, a PUT or deactivate → `400 validation` with `errors.location`
- `Location = { id; code; name; kind: 'Physical' | 'Supplier' | 'Customer' | 'Adjustment'; isSystem: boolean; allowNegativeStock: boolean; isActive: boolean; version: string }`
- There is **no negative-stock switch in S4**: S10 adds it with its warning (spec §10). Don't render `allowNegativeStock`, and don't send it.

**1. `locations.service.ts`:** `LocationsService`, with `Location` and `LocationInput` (`{ code; name }`) and `list({ page, pageSize })`, `create`, `update(location, input)` (sends `version`) and `deactivate(id)`. It follows `ItemsService` method for method. **`locations.service.spec.ts`** mirrors `items.service.spec.ts`: the query params, `update` sending `version`, and the deactivate POST.

**2. `location-dialog.ts` + `.html`:** like `ItemDialog`, with fields `code` (required, maxLength 32) and `name` (required, 200). The title is "New location" or "Edit location". Server field errors go to `code`/`name`, and anything else goes to the form-level alert. **`location-dialog.spec.ts`:** Save is disabled when empty; create and update are called correctly; a 400 `errors.code` message is shown.

**3. `locations.ts` + `.html` + `.scss`** (`export class Locations`):
- Load with `list({ page, pageSize })` and a `mat-paginator` (sizes `[25, 50, 100]`, default 25), like Items.
- Render a Material list (`mat-list`), **not** AG Grid: each row shows `code` and `name`, with chips (`mat-chip-set`) "System" when `isSystem` and "Inactive" when `!isActive`.
- **Admin only**: a "New location" button. Physical rows get an "Edit" icon button, and active physical rows also get "Deactivate", which confirms first with `ConfirmDialog`. System rows never get actions. Feedback goes through the snackbar, exactly as on Items.
- Rows wrap at 375 px, and the action buttons stay reachable. Page title "Locations".

**4. `locations.spec.ts`:** stub `LocationsService` to return one physical location (`QC`) and one system location (`SUP`), and stub `AuthService` with `isAdmin: signal(…)`. Assert:
- `SUP`'s row shows a "System" chip.
- Staff sees no New/Edit/Deactivate.
- Admin sees "New location" plus Edit and Deactivate on `QC` only, none on `SUP`.

**5. Routes and nav.**
- In `app.routes.ts`, add `{ path: 'locations', canActivate: [authGuard], loadComponent: () => import('./locations/locations').then((m) => m.Locations) }` after `items`.
- In `app.html`, add `<a mat-button routerLink="/locations" routerLinkActive="active">Locations</a>` after the Items link.
- In `app.spec.ts`, extend the nav test to expect `a[href="/locations"]` too.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add an Allow negative stock switch, or any control for `allowNegativeStock` or `kind`.
- Change the Items feature, `ConfirmDialog` or `problem-messages.ts`.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- The three new spec files pass, along with the updated `app.spec.ts` and every existing spec.
- `! grep -rn 'allowNegativeStock' src/app/locations/*.html`
