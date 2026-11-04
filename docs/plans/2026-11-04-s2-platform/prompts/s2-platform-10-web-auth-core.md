---
type: prompt
plan: "[[2026-11-04-s2-platform]]"
id: 10
unit: IMS-3
status: todo
model: sonnet
effort: medium
depends_on: [09]
parallel_group: J
files:
  create:
    - web/src/app/core/auth/auth.service.ts
    - web/src/app/core/auth/auth.service.spec.ts
    - web/src/app/core/auth/auth.guard.ts
    - web/src/app/core/auth/auth.guard.spec.ts
    - web/src/app/core/problems/problem-messages.ts
    - web/src/app/core/problems/problem.interceptor.ts
    - web/src/app/core/problems/problem.interceptor.spec.ts
  modify:
    - web/src/app/app.config.ts
  delete: []
---
# 10 · Angular auth core and interceptor

## Task
In `web/`, add a signal-based auth service, a functional auth guard, and the single HTTP interceptor. The interceptor maps ProblemDetails `type` slugs to friendly messages and sends `401` to `/login`. Register `HttpClient` with the interceptor. Vitest specs cover all three.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`, app in `web/`: Angular 22.2.5, standalone, zoneless (default), Vitest through `ng test` (`@angular/build:unit-test`), angular-eslint, Prettier (`web/.prettierrc`). Current `web/src/app/app.config.ts`:
```ts
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
export const appConfig: ApplicationConfig = {
  providers: [provideBrowserGlobalErrorListeners(), provideRouter(routes)]
};
```
Spec §10 conventions:
- Signals, functional guards and interceptors.
- State in small signal-based services per feature, with no NgRx.
- One interceptor maps ProblemDetails `type` slugs to friendly messages and sends `401` to `/login`.
- Hide buttons the role can't use.

The API is same-origin through the dev proxy (`/api` → api), so use relative URLs (`/api/auth/...`) and no `withCredentials`.

API contract (from the backend in this PR):
- `POST /api/auth/login`, body `{ email, password }`. `200` returns `{ id, email, role: 'Admin' | 'Staff', tenantName }`. Every failure is `401` with ProblemDetails `{ type: '/problems/invalid-credentials', title, detail, status }`. Empty fields get `400` with `{ type: '/problems/validation', errors: { Email?: string[], Password?: string[] } }`.
- `POST /api/auth/logout` → `204`.
- `GET /api/auth/me` → the same user shape, or `401`.

**`core/problems/problem-messages.ts`:**
```ts
// Keep in sync with Kernel.Problems.ProblemTypes (CLAUDE.md Hidden coupling).
export const PROBLEM_MESSAGES: Readonly<Record<string, string>> = {
  'validation': 'Please check the highlighted fields.',
  'invalid-credentials': 'Email or password is incorrect. After 5 failed attempts the account is locked for 15 minutes.',
};
export const DEFAULT_PROBLEM_MESSAGE = 'Something went wrong. Please try again.';
export function problemSlug(type: unknown): string | null  // '/problems/<slug>' -> '<slug>', else null
export function problemMessage(body: unknown): string        // slug's message, else DEFAULT_PROBLEM_MESSAGE
```
Exactly these two slugs. Later slices add each new slug here and in Kernel in the same PR.

**`core/auth/auth.service.ts`**: `@Injectable({ providedIn: 'root' })`.
- Exported `type CurrentUser = { id: string; email: string; role: 'Admin' | 'Staff'; tenantName: string }`.
- State: `private readonly _user = signal<CurrentUser | null>(null)`, `readonly user = this._user.asReadonly()`, `readonly isAdmin = computed(() => this._user()?.role === 'Admin')`.
- `loaded`: a promise memo, so `ensureLoaded()` calls `GET /api/auth/me` at most once until `login`/`logout` resets it. It resolves to `CurrentUser | null`, and a `401` becomes `null` (not an error).
- `login(email, password): Promise<CurrentUser>` posts and sets `_user`. On error it rethrows the `HttpErrorResponse` for the caller.
- `logout(): Promise<void>` posts, clears `_user` (even if the post fails) and navigates to `/login`.
- Use `firstValueFrom` over `HttpClient`.

**`core/auth/auth.guard.ts`**: `export const authGuard: CanActivateFn = async () => (await inject(AuthService).ensureLoaded()) ? true : inject(Router).createUrlTree(['/login'])`. Capture `inject` before the `await`; `inject` must run synchronously in the injection context.

**`core/problems/problem.interceptor.ts`**: `export const problemInterceptor: HttpInterceptorFn`.
- Pass the request through.
- On `HttpErrorResponse` with `status === 401`, if the request URL isn't `/api/auth/login` or `/api/auth/me` (those 401s are handled by the caller and the guard), navigate to `/login`.
- Always rethrow the error. Mapping to text is done by callers through `problemMessage(err.error)`.
- `inject(Router)` at the top of the function.

**`app.config.ts`**: add `provideHttpClient(withInterceptors([problemInterceptor]))`.

**Specs** (Vitest globals as in `web/src/app/app.spec.ts`, `TestBed`, `provideHttpClient()` + `provideHttpClientTesting()`, `HttpTestingController`):
- `problemSlug`/`problemMessage`: known slug, unknown slug → default, non-object → default.
- `AuthService`:
  - `ensureLoaded` with `200` sets `user()`.
  - With `401` it resolves `null` and `user()` stays `null`.
  - Two calls → one HTTP request.
  - `login` sets `user()` and `isAdmin()`.
  - `logout` clears `user()` and navigates to `/login`. Spy on `Router.navigate` or `navigateByUrl`, whichever the service uses.
- `authGuard`: it returns `true` when a user loads, and a `UrlTree` for `/login` on `401`. Use `TestBed.runInInjectionContext`.
- `problemInterceptor`: a `401` from `/api/items` navigates to `/login`, a `401` from `/api/auth/login` doesn't, and a `500` doesn't.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Install packages (Material is prompt 11).
- Store the user or anything else in `localStorage`. The cookie is the session.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
# all specs green; new specs >= 12 tests
```
