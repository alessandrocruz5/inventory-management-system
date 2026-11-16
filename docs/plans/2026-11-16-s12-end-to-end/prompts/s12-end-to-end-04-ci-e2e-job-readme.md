---
type: prompt
plan: "[[2026-11-16-s12-end-to-end]]"
id: 04
unit: IMS-16
status: todo
model: sonnet
effort: medium
depends_on: [01, 02]
parallel_group: B
files:
  create: []
  modify:
    - .github/workflows/ci.yml
    - README.md
  delete: []
---
# 04 · CI `e2e` job and README section

## Task
Add the `e2e` job to `.github/workflows/ci.yml`. It runs after `test`, starts the stack through Playwright's `webServer` (the AppHost), runs `tests/e2e`, and uploads the report and traces when it fails. Then add an "End-to-end tests" section to `README.md`.

## Context
- Repo root: `git rev-parse --show-toplevel`. Branch `IMS-16`; stop if `git branch --show-current` says otherwise.
- Spec §11 (`:654-659`): jobs `build` → `test` → `e2e`; *"`e2e`: start the stack, run Playwright, upload traces as artifacts on failure."* The job id must be exactly `e2e`: it becomes a required status-check context on `main` (TASKS.md IMS-2 Downstream; plan R7/D12, done by the user, not you).
- Current `ci.yml`: `on: pull_request` + `push: main`; `permissions: contents: read`; `concurrency` with `cancel-in-progress`. Jobs `build` (prompt 01 added a "Check banned packages" step) and `test`. Each job uses `actions/checkout@v7`, `actions/setup-dotnet@v6` with `global-json-file: global.json`, and `actions/setup-node@v7` with `node-version: '26'`, `cache: npm` and `cache-dependency-path: web/package-lock.json`. Copy those exact action versions. Change nothing in `build` or `test`.
- How the suite runs (prompt 02, `tests/e2e/playwright.config.ts`):
  - `webServer.command` is `dotnet run --project ../../src/AppHost --launch-profile http` with `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`. It waits for `http://localhost:4200/api/auth/me`, with a 15-minute timeout. `reuseExistingServer` is off when `CI` is set (GitHub sets `CI=true`).
  - When `E2E_PASSWORD` is set, it's passed to the AppHost as `Parameters__seed-password` and used to sign in.
  - Reporters in CI: `list` + `html` (to `playwright-report/`). Traces and screenshots of failed tests go to `test-results/`.
  - The AppHost's SQL resource is a Docker container (`mcr.microsoft.com/mssql/server:2025-latest`); `ubuntu-latest` ships Docker. The `web` resource runs `npm start` in `web/`, so `web/node_modules` must exist.
- **Job** (plan D5, D7, D9). Write it like this, resolving `<major>` first:
  ```yaml
    e2e:
      needs: test
      runs-on: ubuntu-latest
      timeout-minutes: 30
      steps:
        - uses: actions/checkout@v7
        - uses: actions/setup-dotnet@v6
          with:
            global-json-file: global.json
        - uses: actions/setup-node@v7
          with:
            node-version: '26'
            cache: npm
            cache-dependency-path: |
              web/package-lock.json
              tests/e2e/package-lock.json
        - name: Generate seed password
          run: |
            pw="E2e!$(openssl rand -hex 16)Aa9"
            echo "::add-mask::$pw"
            echo "E2E_PASSWORD=$pw" >> "$GITHUB_ENV"
        - run: dotnet dev-certs https
        - run: npm ci
          working-directory: web
        - run: npm ci
          working-directory: tests/e2e
        - run: npx playwright install --with-deps chromium
          working-directory: tests/e2e
        - run: npx playwright test
          working-directory: tests/e2e
        - if: failure()
          uses: actions/upload-artifact@v<major>
          with:
            name: playwright-report
            path: |
              tests/e2e/playwright-report/
              tests/e2e/test-results/
            retention-days: 14
  ```
  - `<major>`: run `gh api repos/actions/upload-artifact/releases/latest --jq .tag_name` and use its major (for example `v5`). If `gh` isn't available, use the newest major in the action's README and say so in the report.
  - The password: `E2e!` + 32 hex chars + `Aa9` is 39 chars with upper, lower, digit and special. That satisfies Identity's default policy and the AppHost parameter's `Min*` classes (CLAUDE.md Hidden coupling).
  - The password is masked before it's exported, and is never echoed anywhere else.
- **README** (`README.md` holds only a `# inventory-management-system` heading today). Append a `## End-to-end tests` section, written for a developer cloning the repo, that covers:
  - What it is: Playwright in `tests/e2e` (T14, the ledger page as Admin and Staff, and the post-stock form at 375 px), run by CI's `e2e` job.
  - Prerequisites: .NET SDK per `global.json`, Node 26, Docker on an **x64** host. SQL Server 2025 has no ARM64 image, so it doesn't run on Apple-silicon Macs.
  - The password: export `E2E_PASSWORD` without printing it, using the command `export E2E_PASSWORD="$(dotnet user-secrets list --project src/AppHost | sed -n 's/^Parameters:seed-password = //p')"`. It must be the persisted `seed-password`, because the seed only creates missing users.
  - Run: `cd tests/e2e && npm ci && npx playwright install chromium && npx playwright test`. It reuses a running AppHost, or starts one (`--launch-profile http`).
  - Side effects on a reused database: every run adds `E2E-…` locations holding `CANDLE-WHT` stock and runs a ledger rebuild on the demo tenant. Nothing can be hard-deleted.
  - Coupling: renaming the AppHost's `seed-password` parameter, the `web` port 4200, the seeded user emails, or a UI label used by the specs breaks the suite.
  - Keep it to about 20 lines, plain language, no secrets.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change GitHub settings or rulesets, or run any `gh` command that writes.
- Add dependencies, extra jobs, matrix builds or caching beyond what's listed.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
python3 -c "import yaml,sys; d=yaml.safe_load(open('.github/workflows/ci.yml')); j=d['jobs']; assert list(j)==['build','test','e2e'], list(j); assert j['e2e']['needs']=='test'; print('ok')"
command -v actionlint >/dev/null && actionlint .github/workflows/ci.yml || echo "actionlint not installed: skipped"
git diff .github/workflows/ci.yml   # only the new e2e job is added (build/test untouched apart from prompt 01's step)
grep -n '^## End-to-end tests' README.md
```
- The real proof is the PR's `e2e` job going green. build-unit's verify step checks it after the PR opens. If it fails, the uploaded `playwright-report` artifact and the job log (the AppHost output is piped) show why.
