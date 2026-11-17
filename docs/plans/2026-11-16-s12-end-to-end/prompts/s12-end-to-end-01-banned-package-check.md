---
type: prompt
plan: "[[2026-11-16-s12-end-to-end]]"
id: 01
unit: IMS-16
status: done
model: sonnet
effort: medium
depends_on: []
parallel_group: A
files:
  create:
    - .github/scripts/check-banned-packages.sh
  modify:
    - .github/workflows/ci.yml
  delete: []
---
# 01 · Banned-package check in the `build` job

## Task
Write `.github/scripts/check-banned-packages.sh` and run it as the last step of the existing `build` job in `.github/workflows/ci.yml`. The script fails, naming every offender, when any .NET project references a banned package (directly or transitively) or any tracked npm lockfile contains PrimeNG. It passes on the current tree.

## Context
- Repo root: `git rev-parse --show-toplevel`. Branch `IMS-16`; stop if `git branch --show-current` says otherwise. Restore on the desktop needs `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`.
- Spec §11 (`docs/Inventory System — Sprint 1 Spec.md:658`): *"A job fails if any project references a banned package (section 2): parse `dotnet list package --format json` and grep `package.json`."* CLAUDE.md: *"Do not add MediatR, AutoMapper, MassTransit, PrimeNG or FluentAssertions 8+."* §13: *"No banned package appears anywhere; the CI check proves it."*
- Plan decision D4 (user-confirmed): check .NET **top-level and transitive** packages, and every **tracked `package-lock.json`** (stricter than `package.json`; still covers direct deps).
- **Rules**
  - .NET: run `dotnet list InventorySystem.slnx package --include-transitive --format json`. The verified shape is `{"version":1,"projects":[{"path":"…/Api.csproj","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id","requestedVersion","resolvedVersion"}],"transitivePackages":[{"id","resolvedVersion"}]}]}]}`.
    - Banned: an `id` matching `^(MediatR|AutoMapper|MassTransit)($|\.)`, case-insensitive (`AutoMapper.Extensions.…` counts).
    - Banned: `id` equal to `FluentAssertions` (case-insensitive) whose `resolvedVersion` major (text before the first `.`, ignoring any `-prerelease`) is ≥ 8.
    - Fail closed: any project without a non-empty `frameworks` array means "not restored", so exit 2 with a message telling the user to restore or build first.
  - npm: the files from `git ls-files '*package-lock.json'` (today `web/package-lock.json`; prompt 02 adds `tests/e2e/package-lock.json`).
    - Banned: any key of `.packages` matching `(^|/)node_modules/(primeng|@primeng/[^/]+)$`.
    - Fail closed: a lockfile without a `.packages` object (lockfile v1) exits 2.
  - Output: one line per offender, `BANNED  <project path or lockfile>  <id>  <version>`, then exit 1. When clean, print one line, `No banned packages (<n> .NET projects, <m> lockfiles).`, and exit 0.
- **Interface:** `check-banned-packages.sh [--dotnet-json FILE] [LOCKFILE ...]`.
  - With no arguments it scans the repo; run it from the repo root (resolve the root with `git rev-parse --show-toplevel` and `cd` there for the defaults).
  - `--dotnet-json` replaces the `dotnet list` call, and positional lockfiles replace `git ls-files`. These exist only so a fixture can prove the failure path. Turn the argument paths into absolute paths **before** any `cd`.
- **Portability:** it must run under macOS `/bin/bash` 3.2 and on Ubuntu, so no `mapfile`/`readarray`, no associative arrays, no `${var,,}`. Use `set -euo pipefail`, `jq` for all JSON (do the case-insensitive matching inside `jq` with `test(…; "i")` and `ascii_downcase`), and `mktemp` for the dotnet output, removed with a `trap`. Start with the shebang `#!/usr/bin/env bash` and a 3-line header comment saying what it bans and where the rule comes from (CLAUDE.md "Banned packages", spec §11).
- **CI step:** in `.github/workflows/ci.yml`, job `build`, add after the existing `npm run build` step:
  ```yaml
      - name: Check banned packages
        run: bash .github/scripts/check-banned-packages.sh
  ```
  `build` has already run `dotnet build` (so the projects are restored) and `npm ci` in `web`. `ubuntu-latest` ships `jq`. Leave every other line of `ci.yml` unchanged; prompt 04 adds the `e2e` job later. Commit the script with the executable bit (`chmod +x`), though CI calls it through `bash` either way.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies (no Node or PowerShell helper, no npm package), abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
bash .github/scripts/check-banned-packages.sh            # exit 0, prints "No banned packages (…)"
# failure path: exactly 3 BANNED lines, exit 1
tmp="$(mktemp -d)"
cat > "$tmp/dotnet.json" <<'JSON'
{"version":1,"projects":[{"path":"x/X.csproj","frameworks":[{"framework":"net10.0",
 "topLevelPackages":[{"id":"MediatR","requestedVersion":"12.4.1","resolvedVersion":"12.4.1"}],
 "transitivePackages":[{"id":"FluentAssertions","resolvedVersion":"8.0.1"},{"id":"Shouldly","resolvedVersion":"4.3.0"}]}]}]}
JSON
cat > "$tmp/package-lock.json" <<'JSON'
{"lockfileVersion":3,"packages":{"":{"name":"x"},"node_modules/primeng":{"version":"20.0.0"},"node_modules/@angular/core":{"version":"22.2.0"}}}
JSON
set +e; bash .github/scripts/check-banned-packages.sh --dotnet-json "$tmp/dotnet.json" "$tmp/package-lock.json"; echo "exit=$?"; set -e
# FluentAssertions 7 passes; an unrestored project fails closed (exit 2)
sed 's/8.0.1/7.2.0/; s/"MediatR"/"Humanizer"/' "$tmp/dotnet.json" > "$tmp/ok.json"
echo '{"lockfileVersion":3,"packages":{"":{}}}' > "$tmp/ok-lock.json"
bash .github/scripts/check-banned-packages.sh --dotnet-json "$tmp/ok.json" "$tmp/ok-lock.json"   # exit 0
echo '{"version":1,"projects":[{"path":"y/Y.csproj"}]}' > "$tmp/unrestored.json"
set +e; bash .github/scripts/check-banned-packages.sh --dotnet-json "$tmp/unrestored.json" "$tmp/ok-lock.json"; echo "exit=$?"; set -e   # exit=2
```
- The first fixture run prints `exit=1` and three `BANNED` lines (MediatR 12.4.1, FluentAssertions 8.0.1, primeng 20.0.0), not Shouldly or `@angular/core`.
- `git diff .github/workflows/ci.yml` shows only the new step in `build`.
