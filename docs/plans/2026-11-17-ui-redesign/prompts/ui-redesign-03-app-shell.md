---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 03
unit: IMS-22
status: todo
model: sonnet
effort: high
depends_on: [01, 02]
parallel_group: C
files:
  create: []
  modify:
    - web/src/app/app.html
    - web/src/app/app.scss
    - web/src/app/app.ts
    - web/src/app/app.config.ts
    - web/src/app/app.spec.ts
  delete: []
---
# 03 · App shell: sidebar, mobile top bar and drawer

## Task
Replace the `mat-toolbar` shell with the handoff's layout:
- A sticky 248 px sidebar at ≥ 760 px.
- Below 760 px, a 56 px top bar with a menu button, a "+ Post" link and a slide-in drawer.
- The padded main content column.

Also turn on scroll-to-top, outline form fields by default, and Material Symbols as the default `mat-icon` font. Update `app.spec.ts` to the new DOM.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md`, "Layout shell" (every pixel value is there). Screens: `screenshots/desktop-01-stock.png` and `mobile-nav-drawer.png`. Exact inline styles are in `Inventory App.dc.html` (the `<aside>` block near the top).
- Prompt 01 provided the global tokens (`--ims-*`) and classes in `web/src/styles.scss`. Don't edit that file; shell styles go in `app.scss` and must stay under 4 kB. Prompt 02 provided `injectIsNarrow()` in `web/src/app/shared/layout.ts`.
- Current files: `web/src/app/app.html:1-39` (toolbar and nav, with the Admin-only links behind `auth.isAdmin()`), `app.ts` (`signOut()` calls `auth.logout()`), `app.config.ts` (`provideRouter(routes)`, the HTTP interceptor). `AuthService` (`core/auth/auth.service.ts`) exposes `user()` (`{ id, email, role: 'Admin'|'Staff', tenantName }`) and `isAdmin()`.

### Structure: one router outlet, one nav (plan D16)
- Keep **one** `<router-outlet />`, always rendered inside `<main class="main">`, so the login page isn't recreated when the user signs in. The chrome (sidebar, top bar) renders only when `auth.user()` is set.
- Render exactly **one** `<aside class="sidebar">`. On desktop it is the sticky sidebar. On narrow screens it renders only while `drawerOpen()` is true, as the fixed drawer, over a backdrop `<div class="backdrop">`. The e2e suite clicks `getByRole('link', { name: 'Ledger health' })` and asserts Staff has `toHaveCount(0)`, so the links must never exist twice, and a closed drawer must not be in the DOM.
- Layout: `.shell { display: flex; min-height: 100vh }`; sidebar 248 px wide, `position: sticky; top: 0; height: 100vh`, flex column, white, 1 px right border; `.main { flex: 1; min-width: 0 }` with an inner wrapper `max-width: 1240px; margin: 0 auto; padding: clamp(20px,3.4vw,40px) clamp(16px,3.4vw,40px) 48px`. When signed out, the main area has no sidebar and keeps the padding. Login is fine centred inside it.

### Sidebar contents (README values)
- **Brand block:** a 32×32 ink tile, radius 8, with a white `inventory_2` icon (18 px). Beside it, "Inventory" (15/20 600) above `user.tenantName` (12/16 muted, ellipsis).
- **Nav groups,** each with a group label (11 px 500 uppercase, `.06em`, faint, `padding: 0 10px 6px`) and a 20 px gap between groups:
  - **Inventory:** Stock `/stock` (`inventory_2`), Items `/items` (`category`), Locations `/locations` (`warehouse`), Documents `/documents` (`receipt_long`).
  - **Post stock:** Receive stock `/documents/new/receipt` (`move_to_inbox`), Use or sell stock `/documents/new/issue` (`outbox`), Move stock `/documents/new/transfer` (`swap_horiz`), and Correct stock `/documents/new/adjustment` (`tune`, **Admin only**).
  - **Admin** (the whole group Admin only): Ledger health `/admin/ledger` (`monitor_heart`).
  - Keep these link texts exactly. e2e and `app.spec.ts` use them.
- **Nav item:** `<a class="nav-item" routerLink=… routerLinkActive="active" ariaCurrentWhenActive="page"><mat-icon>…</mat-icon><span>Label</span></a>`. Height 36, `padding: 0 10px`, radius 8, gap 10, 20 px icon, 14 px label, 2 px between items. Default text secondary and icon `#8A857F`; hover `#F2F1EF` and ink; active `#ECEBE8`, ink text and icon, 500.
- **Active rules (plan D18):** Stock, Items, Locations and the Post stock links use `routerLinkActive` (subset match, so `/items/:id` keeps Items active). **Documents** must be active on `/documents` and `/documents/:id` but **not** on `/documents/new/*`. Derive it from the router URL: a `toSignal` of `NavigationEnd.urlAfterRedirects`, tested against `/^\/documents(\/(?!new(\/|$))[^/?#]+)?([?#].*)?$/`. Bind `[class.active]` and `[attr.aria-current]`.
- **User footer:** `padding: 14px 16px`, top border. A 32 px round avatar (`#ECEBE8`, initials 12/600 secondary): the initials are the first letters of the first two `.`/`_`/`-` segments of the email's local part, else its first two characters, uppercased. Beside it, the email (13/500, ellipsis) above the role (12/16 muted). On the right, a 32×32 `.btn-icon` sign-out `<button mat-stroked-button type="button" aria-label="Sign out" (click)="signOut()">` with a `logout` icon.

### Mobile (< 760 px, `isNarrow()`)
- **Top bar,** sticky top 0, height 56, white, bottom border, z-index above content:
  - A 44×44 menu button (`aria-label="Open navigation"`, `[attr.aria-expanded]="drawerOpen()"`, icon `menu` 24 px).
  - "Inventory" (15/18 600) above the tenant name (12/16 muted).
  - On the right, **a link, not a button** (plan D10): `<a mat-stroked-button class="btn-md" routerLink="/documents/new/receipt"><mat-icon>add</mat-icon>Post</a>`. A `<button>` named "Post" would collide with the e2e `getByRole('button', { name: 'Post' })` on the post form at 375 px.
- **Drawer:** `position: fixed; top: 0; left: 0; height: 100vh; width: 248px; z-index: 20`, shadow `0 8px 32px rgba(0,0,0,.18)`. The backdrop is `position: fixed; inset: 0; background: rgba(28,27,25,.32); z-index: 19`.
- **Closing the drawer:** on a backdrop click, on Escape (`(document:keydown.escape)` host listener), and on every `NavigationEnd`. Clicking a nav item navigates, which closes it. Close it too when the viewport widens past 760 px (an `effect` on `isNarrow()`).
- **Touch targets:** nav items are 44 px tall on mobile. The "+ Post" link (`.btn-md`) and the sign-out `.btn-icon` get ≥ 44 px from IMS-22's mobile block in `styles.scss` (plan D19); don't size them down in `app.scss`.

### `app.ts`
Add `isNarrow = injectIsNarrow()`, `drawerOpen = signal(false)`, `openDrawer()`/`closeDrawer()`, an `initials` computed, and the documents-active computed. Import `MatIconModule`, `MatButtonModule`, `RouterLink`, `RouterLinkActive` and `RouterOutlet`, and drop `MatToolbarModule`. Keep `signOut()`.

### `app.config.ts`
- `provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' }))` (README "Scroll to the top on route change").
- `{ provide: MAT_FORM_FIELD_DEFAULT_OPTIONS, useValue: { appearance: 'outline', subscriptSizing: 'dynamic' } }`.
- `provideAppInitializer(() => inject(MatIconRegistry).setDefaultFontSetClass('material-symbols-outlined'))` (plan D9), so `<mat-icon>name</mat-icon>` renders Material Symbols.
- Keep the existing providers.

### `app.spec.ts`
Rewrite the toolbar-based cases; keep the intent of each one:
- Signed in, the sidebar shows the tenant name, `staff@demo.test` and `Staff`.
- `button[aria-label="Sign out"]` calls `logout` once.
- Every link in the Inventory and Post stock groups exists (`a[href=…]`) with its text.
- Correct stock and Ledger health appear for Admin only, toggling `isAdmin` as the current spec does.
- Signed out, there is no `aside` and no top bar.
- A narrow case (mock `BreakpointObserver` to match, as `layout.ts`'s JSDoc shows): the top bar is present, `a[href="/documents/new/receipt"]` in the top bar has the text "Post" and isn't a `button`, no `aside` exists until the menu button is clicked, and one `aside` exists after it.
- Documents is active on `/documents/abc` but not on `/documents/new/receipt`, if routing can be driven in the test with `provideRouter([...])` + `RouterTestingHarness`. Otherwise unit-test the URL regex through a small exported pure function in `app.ts`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. No edits to `styles.scss` or any screen.
- Change routes, guards, `AuthService` or role checks. Hiding stays in the template; the API still enforces roles.
- Use `mat-sidenav` or `mat-toolbar`. The design's sizes and the single-`aside` rule are simpler with plain elements.
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
- Running the AppHost (`dotnet run --project src/AppHost`) and opening `http://localhost:4200`: the desktop matches `screenshots/desktop-01-stock.png`'s sidebar, and 390 px matches `mobile-nav-drawer.png`. At 375 px, `document.documentElement.scrollWidth <= 375` with the drawer closed and open, and the menu button, "+ Post", the nav items and sign-out each measure ≥ 44 px tall (sign-out ≥ 44 px wide too) (D19).
- CI's `e2e` job passes on the PR: the four Playwright tests are unchanged.
