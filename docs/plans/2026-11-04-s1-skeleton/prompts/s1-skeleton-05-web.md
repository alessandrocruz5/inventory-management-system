---
type: prompt
plan: "[[2026-11-04-s1-skeleton]]"
id: 05
unit: IMS-2
status: done
model: sonnet
effort: medium
depends_on: [04]
parallel_group: E
files:
  create:
    - web/**
  modify:
    - src/AppHost/AppHost.cs
    - src/AppHost/AppHost.csproj
  delete: []
---
# 05 · Angular web app and AppHost wiring

## Task
Generate the Angular app in `web/` with angular-eslint, make its `start` script listen on the port Aspire assigns, and add it to the AppHost as the `web` resource on port 4200.

## Context
- **Repo root:** `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-2`; do not switch. Node 26.12.1 and npm 11.20 are installed; there is no global `ng`, so use `npx`.
- **Versions checked 2026-11-04:**
  - `@angular/cli` 22.2.5. `@angular/core` 22.2.4 engines: `node ^22.22.3 || ^24.15.0 || >=26.0.0`, so Node 26 is fine.
  - `angular-eslint` 22.6.0.
  - `Aspire.Hosting.JavaScript` 13.6.4. It's the current Aspire JS hosting package; `Aspire.Hosting.NodeJs` stopped at 9.5.
- **Already built:** `src/AppHost/AppHost.cs` with `sql` (2025-latest, data volume) → `inventory` database → `api` (`/health` check), and `src/Api`, `src/ServiceDefaults`, the modules, `tests/Architecture.Tests`, `InventorySystem.slnx` and `global.json`.
- **Spec:**
  - CLAUDE.md Stack: Angular latest stable, standalone components, signals, zoneless, `@if`/`@for`, typed reactive forms.
  - Allowed npm packages: what `ng new` adds, plus `angular-eslint` and its peers `eslint` and `typescript-eslint`. The user approved the peers during the audit, and `ng add angular-eslint` writes all three into `devDependencies`.
  - §11 CI: "the runner has no global `ng`", so CI uses npm scripts.
  - CLAUDE.md `## Commands` runs `cd web && npm run lint && npm run build && npm test -- --watch=false`. All three scripts must exist and pass.
- **AddJavaScriptApp API** (`Aspire.Hosting.JavaScript.xml`): `AddJavaScriptApp(string name, string appDirectory, string runScriptName = "dev")`. It "configures the resource with Node.js defaults and sets up npm integration". `WithNpm(bool install, …)` optionally installs packages before start. CLAUDE.md says to check the current API, so read the package's XML docs or IntelliSense before you write the AppHost lines.

### Steps
1. Read `npx -y @angular/cli@22.2.5 new --help` first. Then run from the repo root:
   `npx -y @angular/cli@22.2.5 new web --directory web --skip-git --style scss --ssr=false --routing --package-manager npm --interactive=false`
   Add these only if `--help` lists them:
   - `--ai-config=none`
   - `--zoneless` and `--test-runner=vitest`. They're the v21+ defaults; pass them only if the flags exist.

   `--skip-git` matters: the repo already exists, so no nested `.git`.
2. Verify the generated app is what the stack requires. If any check fails, stop and report; don't hand-convert:
   - `web/src/app/app.config.ts` uses zoneless change detection, and `zone.js` is neither in `package.json` nor in polyfills.
   - Components are standalone.
   - The `angular.json` test target uses `@angular/build:unit-test` with Vitest.
3. Copy `web/package.json`'s `dependencies` and `devDependencies` keys, then run `cd web && npx ng add angular-eslint@22.6.0 --skip-confirmation`.
   - The new keys must be exactly `angular-eslint`, `eslint` and `typescript-eslint`. If the schematic added anything else, stop and report; that needs the user's approval (CLAUDE.md "Ask before you").
   - Make sure `package.json` has `"lint": "ng lint"`, and add it if the schematic didn't.
4. In `web/package.json`, set `"start": "ng serve --port ${PORT:-4200}"`. npm runs scripts with `sh` on Linux, and CI is Ubuntu. Aspire supplies `PORT`; standalone `npm start` falls back to 4200.
5. Keep `package-lock.json`; CI runs `npm ci`. Keep the generated welcome page and `app.spec.ts`; S2 replaces the shell.
6. AppHost:
   - `dotnet add src/AppHost package Aspire.Hosting.JavaScript --version 13.6.4`.
   - In `src/AppHost/AppHost.cs`, keep the existing `sql`/`db`/`api` code (re-add `var api =` if prompt 04 dropped it) and add, before `builder.Build().Run();`:
     ```csharp
     builder.AddJavaScriptApp("web", "../../web", "start")
         .WithHttpEndpoint(port: 4200, env: "PORT")
         .WithReference(api)
         .WaitFor(api);
     ```
   - Adapt it to the real API, but meet these goals:
     - **One HTTP endpoint.** The proxy is on host port 4200 and `ng serve` listens on the port in `PORT`. If `AddJavaScriptApp` already declares an `http` endpoint, configure that one instead of adding a duplicate.
     - **Fresh clones install themselves.** A clone without `node_modules` must install before start: use `WithNpm(install: true)` if installing isn't already the default.
     - **Web gets the api reference.** `WithReference(api)` is there for S2's dev proxy. Don't build that proxy here.
     - **No `WithExternalHttpEndpoints()`.** It matters only for publishing, so S13 adds it.
   - The app directory is relative to `src/AppHost`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here: no Angular Material, AG Grid, Playwright, proxy config, environments or extra components.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
test ! -d web/.git
cd web && npm run lint && npm run build && npm test -- --watch=false
cd ..
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
rm -rf web/node_modules                       # simulate a fresh clone for the web resource
dotnet run --project src/AppHost > "${TMPDIR:-/tmp}/apphost.log" 2>&1 &   # §13's exact command (default https profile)
# wait for sql, api and web to start
curl -fsS http://localhost:4200/ | grep -q '<app-root' && echo web-ok
curl -fsS http://localhost:<api http port>/health      # Healthy
kill %1
git status --porcelain                         # web/**, the two AppHost files, plus bookkeeping
```
- Report the dashboard's resource states: `sql` and `api` Healthy, `web` Running.
