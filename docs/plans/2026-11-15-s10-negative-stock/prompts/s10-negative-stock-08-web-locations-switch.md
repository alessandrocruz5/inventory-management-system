---
type: prompt
plan: "[[2026-11-15-s10-negative-stock]]"
id: 08
unit: IMS-14
status: done
model: sonnet
effort: medium
depends_on: [07]
parallel_group: B
files:
  create: []
  modify:
    - web/src/app/locations/locations.ts
    - web/src/app/locations/locations.html
    - web/src/app/locations/locations.scss
    - web/src/app/locations/locations.spec.ts
  delete: []
---
# 08 · Web: Allow negative stock switch

## Task
On `/locations`, give Admins an "Allow negative stock" switch on each active physical location, with a warning confirm when turning it on. Show Staff a "Negative stock allowed" chip instead. Cover it in `locations.spec.ts`.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-14`. Stop if `git branch --show-current` says otherwise. Angular standalone components, signals, zoneless, `@if`/`@for`, Angular Material (already installed; `MatSlideToggleModule` comes from `@angular/material/slide-toggle`, part of the allowed package). Forms must work at 375 px wide.
- Spec §10 `:603`: *"Physical list with create/edit/deactivate and an Allow negative stock switch (Admin, with a warning); virtual ones read-only with a 'System' chip"*. Hide controls the role can't use; the API still enforces the role.
- Prompt 07 added `LocationsService.setAllowNegativeStock(location, allow): Promise<Location>` and the `negative-balances-exist` message, so `problemMessage(body)` maps it.
- Read first: `web/src/app/locations/locations.ts` (the `deactivate` method is the pattern to copy: `ConfirmDialog` via `MatDialog`, then the service, a snack bar, and `load()`; errors go through `problemMessage(err instanceof HttpErrorResponse ? err.error : null)`), `locations.html`, `locations.scss` (`.actions` wraps), `locations.spec.ts`, and `web/src/app/shared/confirm-dialog.ts` (`{ title, message, confirm }` → `boolean`).

### 1. Component (`locations.ts`, `locations.html`, `locations.scss`)
- Import `MatSlideToggleModule` (and `MatSlideToggleChange`).
- Add `protected readonly busyId = signal<string | null>(null);`.
- In the row, for `auth.isAdmin() && !location.isSystem && location.isActive`, render it inside `.actions`, before the buttons:
  ```html
  <mat-slide-toggle
    [checked]="location.allowNegativeStock"
    [disabled]="busyId() === location.id"
    (change)="toggleNegative(location, $event)"
    >Allow negative stock</mat-slide-toggle
  >
  ```
- In the chip set, show `<mat-chip>Negative stock allowed</mat-chip>` when `location.allowNegativeStock && !location.isSystem && !(auth.isAdmin() && location.isActive)`, so Staff (and Admins on inactive rows) can still see the setting. Extend the `@if` that wraps the chip set accordingly.
- `protected toggleNegative(location: Location, event: MatSlideToggleChange): void`:
  - Turning on (`event.checked`): open `ConfirmDialog` with `title: 'Allow negative stock?'`, `message: \`Sales and issues at ${location.code} can then take stock below zero. Costs past zero are estimated from the last cost and corrected when stock arrives.\``, `confirm: 'Allow'`. On cancel: `event.source.checked = false` and stop, with no request. On confirm: `void this.saveNegative(location, true, event)`.
  - Turning off: `void this.saveNegative(location, false, event)` with no confirm.
- `private async saveNegative(location: Location, allow: boolean, event: MatSlideToggleChange)`: `busyId.set(location.id)`; `await this.locationsService.setAllowNegativeStock(location, allow)`; on success a snack bar (`Negative stock allowed at ${location.code}` / `Negative stock turned off at ${location.code}`, `'OK'`, `{ duration: 4000 }`); on error `event.source.checked = location.allowNegativeStock` and a snack bar with `problemMessage(...)`, `{ duration: 6000 }`; `finally` `busyId.set(null)` and `await this.load()`, so the row shows the server's state and the new `version`.
- `locations.scss`: if needed, `align-items: center` on `.actions` so the switch lines up with the buttons. It must still wrap at 375 px.

### 2. `locations.spec.ts`
- **The switch renders an inner `<button role="switch">`**, which would break the existing `buttons(row('QC'))` expectations. Change the `buttons` helper to `root.querySelectorAll('button:not([role="switch"])')` and keep every existing expectation as it is.
- Provide stubs: `LocationsService` `{ list, setAllowNegativeStock }`, `MatDialog` `{ open }` (returning `{ afterClosed: () => of(result) }`), `MatSnackBar` `{ open: snack }`. Reset them in `beforeEach`.
- Add:
  - `shows Admin the switch on active physical rows only`: QC has a `mat-slide-toggle`; SUP and an inactive physical row don't.
  - `shows Staff a Negative stock allowed chip and no switch`: QC with `allowNegativeStock: true`, Staff → chip text present, no `mat-slide-toggle`.
  - `asks before allowing negative stock and sends nothing on cancel`: Admin; click the switch's `button[role="switch"]`; `open` was called with `ConfirmDialog` and data `{ confirm: 'Allow', … }`; `afterClosed` → `false`; `setAllowNegativeStock` not called; the switch isn't checked.
  - `allows negative stock after confirm and reloads`: `afterClosed` → `true` → `setAllowNegativeStock(qc, true)`; `list` called twice; snack bar `Negative stock allowed at QC`.
  - `turns it off without a confirm`: QC on; click → `setAllowNegativeStock(qc, false)`; `open` not called.
  - `shows the problem message when turning off is refused`: `setAllowNegativeStock` rejects with `new HttpErrorResponse({ status: 422, error: { type: '/problems/negative-balances-exist' } })` → the snack bar gets `PROBLEM_MESSAGES['negative-balances-exist']`; `list` reloads.

## Do not
- Touch files outside `files`, including the service, the dialog component or the problem messages. If you must, stop and report why.
- Put the switch in the edit dialog, or send the toggle from the edit dialog.
- Add npm packages, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
```
- Every existing `Locations` spec still passes unchanged (apart from the `buttons` helper), and the six new ones pass.
