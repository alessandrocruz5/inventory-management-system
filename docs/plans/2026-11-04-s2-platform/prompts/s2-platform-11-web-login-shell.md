---
type: prompt
plan: "[[2026-11-04-s2-platform]]"
id: 11
unit: IMS-3
status: todo
model: sonnet
effort: medium
depends_on: [10]
parallel_group: K
files:
  create:
    - web/src/app/login/login.ts
    - web/src/app/login/login.html
    - web/src/app/login/login.scss
    - web/src/app/login/login.spec.ts
    - web/src/app/home/home.ts
  modify:
    - web/package.json
    - web/package-lock.json
    - web/angular.json
    - web/src/styles.scss
    - web/src/index.html
    - web/src/app/app.ts
    - web/src/app/app.html
    - web/src/app/app.scss
    - web/src/app/app.routes.ts
    - web/src/app/app.spec.ts
  delete: []
---
# 11 · Angular Material, login screen and shell

## Task
Install Angular Material. Replace the `ng new` welcome page with an app shell: a toolbar with the tenant name, the user's email and role, and Sign out. Add the `/login` screen and a placeholder home route behind the auth guard.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`, app in `web/` (Angular 22.2.5, standalone, zoneless, Vitest, angular-eslint, SCSS). Prompt 10 created:
- `core/auth/auth.service.ts`: `AuthService` with `user()` signal (`{ id, email, role, tenantName } | null`), `isAdmin()`, `ensureLoaded()`, `login(email, password)` (rethrows `HttpErrorResponse`) and `logout()` (clears and navigates to `/login`).
- `core/auth/auth.guard.ts`: `authGuard`.
- `core/problems/problem-messages.ts`: `problemMessage(body)`.

`web/src/app/app.html` is still the `ng new` placeholder (lines 1–342), followed by `<router-outlet />`. `web/src/app/app.spec.ts` asserts `h1` contains "Hello, web"; replace that assertion.

**Material:**
- `cd web && npx ng add @angular/material@22.2.5 --skip-confirmation --theme=azure-blue --typography=true`. If those option names differ in 22.2.5, run `npx ng add @angular/material@22.2.5 --help` and pick the closest: a prebuilt or M3 theme, with typography.
- Allowed packages are `@angular/material` and `@angular/cdk` only. After `ng add`, `git diff web/package.json` must show no other new dependency. If it adds one (for example `@angular/animations`), stop and report.
- `ng add` edits `angular.json`, `styles.scss` and `index.html` (fonts). Keep its edits.

**Routes (`app.routes.ts`):**
```ts
export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./login/login').then(m => m.Login) },
  { path: '', canActivate: [authGuard], loadComponent: () => import('./home/home').then(m => m.Home) },
  { path: '**', redirectTo: '' },
];
```
`Home` is a placeholder that S7 replaces with `/stock`. Don't add nav links to screens that don't exist yet.

**Shell (`app.ts`/`app.html`/`app.scss`):**
- When `auth.user()` is set, show a `mat-toolbar` with the app name "Inventory", `user().tenantName`, `user().email` (`role`), and a "Sign out" `mat-button` that calls `auth.logout()`. Then `<router-outlet />`.
- When there's no user (the login page), render only `<router-outlet />`.
- Use `@if`. Remove the whole placeholder block and its `<style>`, and the `title` signal.
- It must work at 375 px wide (spec §10): let the toolbar wrap, or hide the email under 480 px.

**Login (`login/login.ts` + html/scss), standalone `Login`:**
- A typed reactive form: `new FormGroup({ email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }), password: new FormControl('', { nonNullable: true, validators: [Validators.required] }) })`.
- Material form fields (`matInput`), labels "Email" and "Password" (`type="password"`), and a "Sign in" `mat-flat-button`.
- Submit is disabled while invalid or while `submitting()` (a signal) is true. Disabling it in flight is spec §10's rule for every form.
- On submit: `submitting.set(true)`, then `await auth.login(...)`, then navigate to `/`. On error, `error.set(problemMessage(err.error))` and keep the form values. Clear the password field only on failure. Finally `submitting.set(false)`.
- Show `error()` in a `role="alert"` element.
- Plain words, for a shop owner who isn't technical. Centered card, usable at 375 px.

**`home/home.ts`** (inline template): "Signed in to {{ auth.user()?.tenantName }}".

**Specs:**
- `app.spec.ts`: the app creates. With `AuthService.user` stubbed to a user, the toolbar shows the tenant name and a Sign out button. With `null`, there's no toolbar.
- `login.spec.ts`:
  - Submit is disabled when empty.
  - A successful login navigates to `/`.
  - A `401` with `{ type: '/problems/invalid-credentials' }` shows the generic invalid-credentials message in the alert and re-enables Submit.
  - Stub `AuthService` with a plain object `{ login: vi.fn(), user: signal(null) }` through `{ provide: AuthService, useValue: … }`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. `ng add` output in the listed files is expected.
- Add AG Grid (S7), other packages, or screens beyond login and home.
- Hard-code role checks for hidden buttons yet: there are no role-gated actions in S2.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
git diff --stat -- package.json      # only @angular/material and @angular/cdk added
grep -c 'Congratulations' src/app/app.html   # 0
```
- Full-stack check (AppHost running, see prompt 07; seed password from the dashboard's `seed-password` parameter):
  - http://localhost:4200 redirects to `/login`.
  - A wrong password shows the generic message.
  - `staff@demo.test` reaches the shell, which shows "Demo Retail" and "staff@demo.test (Staff)".
  - Reloading stays signed in. Sign out returns to `/login`, and pressing back to `/` goes to `/login` again.
  - The layout works at 375 px in devtools.
