---
type: prompt
plan: "[[2026-11-04-s1-skeleton]]"
id: 06
unit: IMS-2
status: todo
model: haiku
effort: low
depends_on: [05]
parallel_group: F
files:
  create:
    - .github/workflows/ci.yml
  modify: []
  delete: []
---
# 06 · GitHub Actions CI

## Task
Create `.github/workflows/ci.yml` with two jobs, `build` and then `test`. It runs on every pull request and on pushes to `main`, and executes exactly the repo's verification commands.

## Context
- **Repo root:** `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-2`; do not switch. The GitHub repo is `alessandrocruz5/inventory-management-system` (public).
- **Already in the repo:**
  - `global.json`: SDK 10.0.112 with `rollForward: latestFeature`, and the MTP test runner.
  - `InventorySystem.slnx`, the .NET projects under `src/` and `tests/`, and the Angular app in `web/` with `package-lock.json` and the npm scripts `lint`, `build` and `test` (Vitest).
- **Spec §11 (lines 651–658):**
  - One workflow, jobs `build` → `test`, on every PR and on pushes to `main`.
  - `build`: `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, then `npm run lint` and `npm run build` in `web/` (no global `ng`).
  - `test`: the .NET tests plus Vitest. GitHub's Ubuntu runners ship with Docker, which Testcontainers needs from S3 on.
  - The banned-package check and the `e2e` job are S12; don't add them.
- **Locked:**
  - Job ids are exactly `build` and `test`, with no `name:` override. After the merge, the `main` ruleset requires checks with those exact contexts, from the GitHub Actions app.
  - Node major is `26`, matching local; it's inside Angular 22's engines range.
  - The .NET SDK comes from `global.json`.

### Workflow shape
```yaml
name: ci
on:
  pull_request:
  push:
    branches: [main]
permissions:
  contents: read
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - checkout
      - setup-dotnet  (global-json-file: global.json)
      - setup-node    (node-version: '26', cache: npm, cache-dependency-path: web/package-lock.json)
      - run: dotnet build -warnaserror
      - run: dotnet format --verify-no-changes
      - run: npm ci            (working-directory: web)
      - run: npm run lint      (working-directory: web)
      - run: npm run build     (working-directory: web)
  test:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - checkout, setup-dotnet, setup-node  (same as build)
      - run: dotnet test
      - run: npm ci                        (working-directory: web)
      - run: npm test -- --watch=false     (working-directory: web)
```
- Use the latest major tag of `actions/checkout`, `actions/setup-dotnet` and `actions/setup-node`. Look each up with `gh api repos/actions/<name>/releases/latest --jq .tag_name` and pin to the major (for example `@v5`).
- Add no other steps, caches, matrices or secrets.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here. That includes linters you install for validation; use only what's already on the machine.
- Create or switch branches, commit, or push. build-unit pushes and opens the PR, and that's when CI first runs.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
python3 -I -c 'import sys,yaml; d=yaml.safe_load(open(".github/workflows/ci.yml")); assert set(d["jobs"])=={"build","test"}; assert d["jobs"]["test"]["needs"]=="build"; print("ok")' \
  || ruby -ryaml -e 'd=YAML.load_file(".github/workflows/ci.yml"); raise unless d["jobs"].keys.sort==%w[build test]; puts "ok"'
# Run every step locally in the job's order, from a clean web install:
dotnet build -warnaserror && dotnet format --verify-no-changes
(cd web && rm -rf node_modules && npm ci && npm run lint && npm run build)
dotnet test
(cd web && npm test -- --watch=false)
git status --porcelain                         # only ci.yml plus bookkeeping
```
- After build-unit opens the PR, `gh pr checks` shows `build` ✓ and `test` ✓. That's R8; report it then.
