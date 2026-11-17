---
type: prompt
plan: "[[2026-11-16-s12-end-to-end]]"
id: 02
unit: IMS-16
status: done
model: sonnet
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - tests/e2e/package.json
    - tests/e2e/package-lock.json
    - tests/e2e/playwright.config.ts
    - tests/e2e/support/app.ts
    - tests/e2e/specs/t14-stock-flow.spec.ts
  modify:
    - .gitignore
  delete: []
---
# 02 · Playwright package, config, helpers and T14

## Task
Create the Playwright suite in `tests/e2e` as its own npm package. Its config starts the real stack through the Aspire AppHost. Write the shared helpers and the T14 test: *"Staff signs in, receives 10 @ ₱100, issues 3, sees 7 and ₱700.00; Admin reverses the issue and sees 10"* (spec §11 `:647`).

## Context
- Repo root: `git rev-parse --show-toplevel`. Branch `IMS-16`; stop if `git branch --show-current` says otherwise.
- **No app code changes (plan D10).** Never edit `src/**` or `web/src/**`. If the app misbehaves (a wrong number, a missing button), stop and report it with the trace, and don't work around it in the test.
- **Host limit:** SQL Server 2025 can't run on an arm64 Mac (S8 Log). The full run needs an x64 Docker host (the desktop) or CI. On the Mac, the check is `npx playwright test --list`.
- **Package** (`tests/e2e/package.json`): `"name": "e2e"`, `"private": true`, scripts `{ "test": "playwright test" }`, and **one** devDependency, `@playwright/test`, pinned to the exact version that `npm view @playwright/test version` prints (no `^`, the same as the AG Grid pin in IMS-5). Add no TypeScript, ESLint or `@types/node`: Playwright transpiles `.ts` itself (plan D6). Generate `package-lock.json` with `npm install` in `tests/e2e`. `@playwright/test` is on CLAUDE.md's npm allowed list; add nothing else.
- **`.gitignore`:** append `playwright-report/` and `blob-report/` under a `# Playwright` comment. `test-results/` (`:425`) and `node_modules/` (`:294`) are already there.
- **Config** (`tests/e2e/playwright.config.ts`), plan D1/D5/D7:
  ```ts
  import { defineConfig, devices } from '@playwright/test';

  const password = process.env['E2E_PASSWORD'];

  export default defineConfig({
    testDir: './specs',
    fullyParallel: false,
    workers: 1,               // the tests share the demo tenant; a rebuild must not interleave a posting
    retries: 0,               // no retries hiding flakes
    forbidOnly: !!process.env['CI'],
    timeout: 90_000,
    expect: { timeout: 15_000 },
    reporter: process.env['CI'] ? [['list'], ['html', { open: 'never' }]] : [['list']],
    use: {
      baseURL: 'http://localhost:4200',
      trace: 'retain-on-failure',
      screenshot: 'only-on-failure',
    },
    projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
    webServer: {
      // The same graph as `dotnet run --project src/AppHost`: sql → migrate → seed → api → web (:4200).
      command: 'dotnet run --project ../../src/AppHost --launch-profile http',
      url: 'http://localhost:4200/api/auth/me', // a 401 counts as ready: proxy and api are both up
      reuseExistingServer: !process.env['CI'],
      timeout: 15 * 60_000,
      stdout: 'pipe',
      stderr: 'pipe',
      env: {
        ASPIRE_ALLOW_UNSECURED_TRANSPORT: 'true',
        ...(password ? { 'Parameters__seed-password': password } : {}),
      },
    },
  });
  ```
  Keep it this shape. `playwright test --list` must work without `E2E_PASSWORD`, so the password is read lazily in the helpers.
- **Facts the helpers rely on** (verified in the code):
  - Users `staff@demo.test` and `admin@demo.test` (`src/Modules/Platform/Platform/Seeding/PlatformSeeder.cs:30-31`), password = the AppHost `seed-password` parameter.
  - Login page (`web/src/app/login/login.html`): fields labelled `Email` and `Password`, button `Sign in`. Success goes to `/`, which redirects to `/stock`.
  - Auth cookie is `Secure` + `SameSite=Strict` (`IdentityRegistration.cs:31-33`). So **create data with an in-browser `fetch` from a signed-in page** (`page.evaluate`), never with `request`/APIRequestContext (plan D2).
  - `POST /api/locations` (Admin) takes `{ code, name }` (code ≤ 32 chars) and returns `201` with `{ id, code, name, … }`. Locations list `ORDER BY` physical first, then `Code`, and the post-stock and stock pages load one page of 100.
  - `CANDLE-WHT` ("Candle, white", unit `pc`) is seeded with **no** stock anywhere (`InventorySeeder.cs:28,94`).
  - Post-stock form (`web/src/app/documents/post-stock.html`): `mat-select` labelled `Location` (options `CODE · Name`); input `Reference`; item autocomplete labelled `Item` (options `SKU · Name`); inputs `Quantity` and, for receipts, `Unit cost`; submit button `Post`. A successful post navigates to `/documents/<guid>` (`post-stock.ts:259-261`).
  - Document detail (`document-detail.html`, `.ts:131-143`): an Admin-only button `Reverse` opens a dialog whose confirm button is also `Reverse`. Afterwards the page shows `Reversed by` with a link.
  - Stock page (`stock.html`, `stock.ts:35-77`): `mat-select` labelled `Location` with `All locations` and `CODE · Name` options. AG Grid cells carry `col-id` `onHand`, `averageCost`, `stockValue`.
  - Formatting (`web/src/app/shared/format.ts:19-25`): money is `₱` + en-US with 2 decimals (`₱700.00`, `₱1,000.00`); quantities drop trailing zeros (`7`, `10`).
- **Helpers** (`tests/e2e/support/app.ts`), all exported:
  - `STAFF = 'staff@demo.test'`, `ADMIN = 'admin@demo.test'`.
  - `password(): string` returns `process.env['E2E_PASSWORD']` or throws `Error('E2E_PASSWORD is not set: export the AppHost seed-password (README "End-to-end tests")')`.
  - `signIn(page, email)`: go to `/login`, fill `getByLabel('Email')` and `getByLabel('Password')`, click `getByRole('button', { name: 'Sign in' })`, and `expect(page).toHaveURL(/\/stock$/)`.
  - `createLocation(browser): Promise<{ id: string; code: string }>`:
    - Open a new context, `signIn(page, ADMIN)`, and build the code as `'E2E-' + (Number.MAX_SAFE_INTEGER - Date.now()).toString(36).toUpperCase() + <2 random base36 chars, uppercased>`. The newest run sorts first (D2).
    - The name is `'E2E ' + new Date().toISOString()`.
    - `page.evaluate` a `fetch('/api/locations', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body })` that returns `{ status, json }`.
    - `expect(status).toBe(201)`, close the context, return `{ id: json.id, code }`.
  - `choose(page, label, optionText)`: click `getByRole('combobox', { name: label })`, then `getByRole('option', { name: optionText })`. Accept a string or a RegExp, so callers can match `new RegExp('^' + code + ' ·')`.
  - `postDocument(page, type: 'receipt' | 'issue', line: { locationCode; sku; quantity: number; unitCost?: number; reference: string }): Promise<string>`:
    - Go to `/documents/new/${type}`, `choose` `Location` by code, fill `Reference`.
    - Type the SKU into `getByRole('combobox', { name: 'Item' })` and pick the option matching `^SKU ·`.
    - Fill `Quantity`, and `Unit cost` when given. Click `Post`.
    - `expect(page).toHaveURL(/\/documents\/[0-9a-f-]{36}$/)` and return the id from the URL.
  - `stockRow(page, locationCode, sku)`: go to `/stock`, `choose` `Location` by code, and return `page.getByRole('row').filter({ hasText: sku })` after expecting it to have count 1.
  - If a role or label locator doesn't resolve against the real app (Material sometimes labels differently), change **the locator**, keep it role/label-based where possible, and note the change in your report.
- **T14** (`tests/e2e/specs/t14-stock-flow.spec.ts`): one `test('T14 · Staff receives 10 @ ₱100 and issues 3; Admin reverses the issue', async ({ page, browser }) => …)`, with `test.step` per phase:
  1. `const loc = await createLocation(browser)`.
  2. Staff: `signIn(page, STAFF)`; `postDocument(page, 'receipt', { locationCode: loc.code, sku: 'CANDLE-WHT', quantity: 10, unitCost: 100, reference: 'T14 receipt' })`.
  3. Staff: `const issueId = await postDocument(page, 'issue', { …, quantity: 3, reference: 'T14 issue' })`. On the issue's detail page (the URL you landed on), expect the heading to be visible and `getByRole('button', { name: 'Reverse' })` to have count 0 (§10: hide buttons the role can't use).
  4. Staff: `stockRow(page, loc.code, 'CANDLE-WHT')`, and expect the `[col-id="onHand"]` cell to read `7`, `[col-id="averageCost"]` `₱100.00`, and `[col-id="stockValue"]` `₱700.00`.
  5. Admin: a **new** context from `browser`, `signIn(admin, ADMIN)`, go to `/documents/${issueId}`, click `Reverse`, click `getByRole('dialog').getByRole('button', { name: 'Reverse' })`, then expect `getByText('Reversed by')` to be visible.
  6. Admin: `stockRow(admin, loc.code, 'CANDLE-WHT')` reads `10`, `₱100.00`, `₱1,000.00`. Close the context.
  Expected numbers (moving average, §7): receipt 10 @ 100 gives 10 / 1,000; issue 3 @ 100 gives 7 / 700; the reversal brings back 3 @ 100, giving 10 / 1,000.

## Do not
- Touch files outside the `files` list (in particular `src/**`, `web/**`, `ci.yml`). If you must, stop and report why.
- Add any npm package other than `@playwright/test`, or a `tsconfig.json`, global setup, storage-state files or retries.
- Print or log `E2E_PASSWORD` or any user-secrets value.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
(cd tests/e2e && npm ci && npx playwright test --list)   # any host: lists the T14 test, no password needed
git status --short   # only the six declared paths changed; no playwright-report/ or test-results/ untracked
# x64 Docker host (desktop) only; skip on the arm64 Mac and say so in the report
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
export E2E_PASSWORD="$(dotnet user-secrets list --project src/AppHost | sed -n 's/^Parameters:seed-password = //p')"
(cd tests/e2e && npx playwright install chromium && npx playwright test)   # 1 passed
(cd tests/e2e && npx playwright test)   # a second run on the same database also passes (fresh location per run)
```
- If you can only run `--list` here, say so plainly in the report. The PR's CI `e2e` job (prompt 04) is then the proof.
