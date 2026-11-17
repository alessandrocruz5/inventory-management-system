---
type: prompt
plan: "[[2026-11-16-s12-end-to-end]]"
id: 03
unit: IMS-16
status: done
model: sonnet
effort: medium
depends_on: [02]
parallel_group: B
files:
  create:
    - tests/e2e/specs/ledger-health.spec.ts
    - tests/e2e/specs/post-stock-mobile.spec.ts
  modify: []
  delete: []
---
# 03 · Ledger page (Admin/Staff) and 375 px post-stock specs

## Task
Add two Playwright spec files to the suite that prompt 02 created:
1. `/admin/ledger` as Admin and as Staff. This closes IMS-15's open Downstream: *"the manual check of `/admin/ledger` as Admin and Staff in the running app is still open, so cover it in the end-to-end tests."*
2. A receipt posted through the post-stock form at 375 px wide (spec §13 `:701`: *"The post-stock form works at 375 px wide."*).

## Context
- Repo root: `git rev-parse --show-toplevel`. Branch `IMS-16`; stop if `git branch --show-current` says otherwise.
- **No app code changes (plan D10).** If a test exposes an app bug (for example the 375 px page scrolls sideways because of the shell toolbar, or the ledger isn't healthy on a fresh seed), stop and report it with the trace and the measured values. Don't edit `src/**` or `web/**`, and don't weaken the assertion.
- **Reuse** the helpers in `tests/e2e/support/app.ts` (from prompt 02): `STAFF`, `ADMIN`, `signIn(page, email)` (ends on `/stock`), `createLocation(browser)` → `{ id, code }`, `choose(page, label, option)`, `postDocument(page, 'receipt' | 'issue', { locationCode, sku, quantity, unitCost?, reference })` → document id. Read that file first, and add nothing to it. If you need a helper, stop and report (it's outside this prompt's files).
- Config (prompt 02): `testDir: './specs'`, one worker, no retries, Chromium `Desktop Chrome`, `baseURL` `http://localhost:4200`.
- **Ledger page facts** (verified):
  - Nav link `Ledger health` is rendered only for Admins (`web/src/app/app.html`).
  - Route `/admin/ledger` is behind `adminGuard`, which sends non-Admins to `/`, and `/` redirects to `/stock` (`core/auth/admin.guard.ts`, `app.routes.ts`).
  - Page (`web/src/app/ledger/ledger-health.html`): heading `Ledger health`; after the check loads, the text `All checks passed` (or `N checks failed`). One section per invariant, `INV-1` to `INV-5`, each with a marker reading `Passed` or `Failed`. Buttons `Check again` and `Rebuild balances`.
  - Rebuild opens a confirm dialog titled `Rebuild balances` whose confirm button is `Rebuild` (`ledger-health.ts:94-101`). On a healthy ledger the result reads `Checked N balances. Nothing to fix.`
- **`tests/e2e/specs/ledger-health.spec.ts`**, `test.describe('Ledger health page', …)` with two tests:
  - `'Admin sees INV-1 to INV-5 pass, and a rebuild finds nothing to fix'`:
    - `signIn(page, ADMIN)`, click `getByRole('link', { name: 'Ledger health' })`, expect URL `/\/admin\/ledger$/` and `getByText('All checks passed')` visible.
    - Expect `getByText('Passed', { exact: true })` to have count 5.
    - Click `getByRole('button', { name: 'Rebuild balances' })`, then `getByRole('dialog').getByRole('button', { name: 'Rebuild' })`, and expect `getByText(/Nothing to fix\./)` visible.
  - `'Staff has no Ledger health link and is sent to /stock'`: `signIn(page, STAFF)`; expect `getByRole('link', { name: 'Ledger health' })` to have count 0; `page.goto('/admin/ledger')`; expect URL `/\/stock$/`.
- **375 px facts:**
  - `web/src/app/documents/post-stock.scss` stacks the header and lines in one column under 600 px.
  - The shell toolbar wraps (`web/src/app/app.scss`: `flex-wrap: wrap`) and hides the user email under 480 px.
  - The location must be fresh (plan D2), so the receipt never touches seeded stock.
- **`tests/e2e/specs/post-stock-mobile.spec.ts`**:
  - `test.use({ viewport: { width: 375, height: 812 } })` at the top of the file.
  - One test, `'Post-stock form works at 375 px: no sideways scroll, receipt posts'`:
    1. `const loc = await createLocation(browser)`. Its own Admin context; the viewport doesn't matter there.
    2. `signIn(page, STAFF)`, go to `/documents/new/receipt`, wait for the `Post` button to be visible.
    3. Assert there's no horizontal overflow: `expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)`.
    4. Post the receipt through `postDocument(page, 'receipt', { locationCode: loc.code, sku: 'CANDLE-WHT', quantity: 1, unitCost: 1, reference: 'E2E 375px' })`, which re-opens the form and ends on `/documents/<id>`.
    5. Assert the same overflow check on the document detail page. Its lines table has mobile `data-label` styling, so it should fit too. If it doesn't, report it rather than drop the assertion.

## Do not
- Touch files outside the `files` list (including `support/app.ts` and `playwright.config.ts`). If you must, stop and report why.
- Add dependencies, retries, `test.skip`/`fixme`, or longer timeouts to hide a failure.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
(cd tests/e2e && npx playwright test --list)   # any host: 4 tests (T14, 2 ledger, 1 mobile)
# x64 Docker host (desktop) only; skip on the arm64 Mac and say so in the report
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
export E2E_PASSWORD="$(dotnet user-secrets list --project src/AppHost | sed -n 's/^Parameters:seed-password = //p')"
(cd tests/e2e && npx playwright test)   # 4 passed
```
- If you can only run `--list` here, say so plainly in the report. The PR's CI `e2e` job is then the proof.
