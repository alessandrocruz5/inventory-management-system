---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 08
unit: IMS-24
status: todo
model: sonnet
effort: medium
depends_on: [07]
parallel_group: C
files:
  create: []
  modify:
    - web/src/app/locations/locations.ts
    - web/src/app/locations/locations.html
    - web/src/app/locations/locations.scss
    - web/src/app/locations/locations.spec.ts
  delete: []
---
# 08 · Locations screen

## Task
Restyle `/locations` to the handoff:
- A page header with a subtitle and "+ New location" (Admin).
- One card list. Each row has a mono code badge, the name, status pills (System, Inactive, Negative stock allowed) and, for Admin on physical locations, the "Allow negative stock" switch, Edit and Deactivate, which wrap below the identity on mobile.

Every behaviour stays the same: the dialogs, the confirm on allow, the revert on failure, `busyId` and the snackbars.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`.
- IMS-22 globals in `web/src/styles.scss` (don't edit it): `.page`, `.page-header`, `.page-title`, `.page-subtitle`, `.page-actions`, `.card`, `.card__footer`, `.mono`, `.muted`, `.secondary`, `.btn-md`, `.skeleton*`, `.empty-state`. The Material slide-toggle and stroked buttons are already restyled globally. `StatusPill` is in `shared/status-pill.ts`. The shell pads the main area.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md` §4 "Locations". Screen: `screenshots/desktop-04-locations.png`. Prototype: `Inventory App.dc.html?screen=locations`.
- **Locked (TASKS.md IMS-14):** the switch text stays "Allow negative stock", and the confirm stays "Allow" (e2e and the spec rely on it).
- Current code: `locations.html:15-31` holds the pill rules. Keep them exactly:
  - `showNegative = allowNegativeStock && !isSystem && !(isAdmin && isActive)`.
  - System when `isSystem`; Inactive when `!isActive`; Negative stock allowed when `showNegative`.
  - `locations.html:33-61` holds the Admin actions: physical only; the switch and Deactivate only when active; `aria-label`s `Edit CODE` and `Deactivate CODE`.

### Layout
- `host: { class: 'page' }`.
- **Header:** `Locations`, with the subtitle "Where stock is kept. System locations record stock entering and leaving." Admin only: `<button mat-flat-button type="button" (click)="openDialog(null)"><mat-icon>add</mat-icon>New location</button>`. Keep the text "New location".
- `.card` holding, `@for` each location, a `.location-row`: `padding: 16px`, flex-wrap, `gap: 12px 24px`, `align-items: center`, `justify-content: space-between`, border-bottom divider.
  - **`.identity`** (flex, wrap, gap `6px 12px`, `align-items: center`, `min-width: 0`):
    - `.code-badge`: `.mono`, 13/500, `padding: 2px 8px`, radius 6, background `var(--ims-chip)`; text faint when inactive.
    - Name: 500, muted when inactive, `overflow-wrap: anywhere`.
    - The pills: `<app-status-pill tone="neutral" label="System">`, `… label="Inactive"`, and `<app-status-pill tone="warn" label="Negative stock allowed">`.
  - **`.actions`** (Admin and non-system only; flex, wrap, gap 8 / 16, `align-items: center`):
    - The existing `mat-slide-toggle` (when active; 13 px secondary label).
    - `Edit` as `mat-stroked-button class="btn-md"`.
    - `Deactivate` as `mat-stroked-button class="btn-md"` (when active).
    - Keep every binding and `aria-label`.
  - **Loading:** while `loading()` and there are no rows, 4 `.skeleton-row`s. Empty: keep `<p class="empty-state">No locations.</p>` when not loading.
  - **`.card__footer`** with the existing `mat-paginator`.
- `locations.ts`: drop `MatChipsModule` and `MatListModule` from `imports`, and add `StatusPill` and `MatIconModule`. Don't change any method.
- `locations.scss`: replace its rules with the row, badge and actions internals, under 2 kB. Remove the `:host` padding and the `h1`/`.header` rules.

### `locations.spec.ts`
Keep the intent of every case and only swap the selectors:
- `mat-chip` becomes `app-status-pill`. Assert its `textContent`, and for "Negative stock allowed" its `data-tone="warn"`.
- `mat-slide-toggle` queries stay; the component is still used.
- The button text and `aria-label` assertions at `:69` stay valid; adjust them only if the DOM order changed.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. Not `locations.service.ts`, `location-dialog.*` or `styles.scss`.
- Change the pill rules, the toggle's confirm, revert or `busyId` handling, or any text in dialogs and snackbars.
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
- `/locations` at 1280 px matches `desktop-04-locations.png`. At 375 px the actions wrap under the identity with no sideways scroll, and Edit and Deactivate measure ≥ 44 px tall from IMS-22's mobile block (plan D19; don't override it in `locations.scss`). Staff sees the "Negative stock allowed" pill and no switch.
- CI `e2e` passes.
