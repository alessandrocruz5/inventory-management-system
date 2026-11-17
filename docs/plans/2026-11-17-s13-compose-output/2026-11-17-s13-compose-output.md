---
type: plan
kind: feature
status: done
repo: inventory-management-system
delivery: pr
base: develop
sprint: 1
epic: IMS-1
created: 2026-11-17
risk: medium
prompts: 2
graph_built: 2026-11-17 10:13:29 (refreshed with `graphify update .` before planning)
tags: [plan]
---
# S13 Compose output (IMS-17)

## Request
> IMS-17

This plan covers the existing unit `IMS-17 — S13 Compose output (stretch) · Added · spec: §12 S13 · depends: IMS-16 · model: haiku` (TASKS.md, Sprint 1). It keeps the key, Sprint 1, epic IMS-1 and base `develop` (workflow §8, "Re-planning legacy units"). The ticket already exists; the ticket step only rewrites the TASKS.md line.

Spec §12 S13 (`:682`): *"`aspire publish` to Docker Compose, committed under `deploy/`, verified running without Aspire. Tests: Manual smoke test. Tier: Stretch"*. §4 (`:184`): `deploy/  `aspire publish` output (stretch)`. §3 (`:126`): the stack runs locally on the x64 desktop only. S12's plan (`2026-11-16-s12-end-to-end.md:123`): *"S13 can point the same suite at the compose stack later"*.

| # | Requirement |
|---|---|
| R1 | `aspire publish` turns the AppHost graph (`sql`, `migrate`, `seed`, `api`, `web`) into a Docker Compose deployment, through a compose environment declared in the AppHost. The Aspire CLI is pinned in the repo, so a fresh clone gets it with `dotnet tool restore`. |
| R2 | The output is committed under `deploy/`, with no secret values in it. `deploy/.env` stays git-ignored. `deploy/.env.example` lists every key, with secret values blank. |
| R3 | In compose, `web` is the production Angular build served at `http://localhost:4200`, which proxies `/api` to `api`. The browser sees one origin, so the `SameSite=Strict`, `Secure` auth cookie works on localhost, and deep links such as `/stock` serve `index.html`. |
| R4 | The stack runs with `docker compose` alone, without the AppHost or DCP. `migrate` (as the server admin) and then `seed` (as `inventory_app`) each run once and exit 0; `api` and `web` start after them; `api` connects as `inventory_app`. A second `up` on the same volume is a replay that changes nothing. |
| R5 | A manual smoke test passes, and so does the existing Playwright suite run against the compose stack. The evidence goes in the PR description. |
| R6 | `dotnet run --project src/AppHost` behaves as before (`ng serve` with the dev proxy), and the CI `e2e` job stays green. |
| R7 | The README documents how to regenerate, run and stop the stack, the password rules for `.env`, and the localhost-only limit. CLAUDE.md records the approved package and tool, and the new coupling between the AppHost and `deploy/`. |

## Context used
- Note: TASKS.md Sprint 1, the IMS-17 line: `spec: §12 S13`, provisional model `haiku`. The header says `/plan-feature` replaces `spec:` with `plan:`.
- Note: spec §12 `:682`, §4 `:184`, §3 `:126` (x64 only), §8 (cookie auth; the API connects as `inventory_app`), §13 `:691` (`dotnet run --project src/AppHost` starts everything).
- Note: CLAUDE.md "Allowed packages": `Aspire.Hosting.Docker` and `Aspire.Cli` aren't listed, so the user approved them (D1). "Hidden coupling": the generated passwords set `Min*` because Identity and SQL `CHECK_POLICY` reject weak values.
- Note: TASKS.md Downstream for IMS-3: only `migrate` gets the server admin. For IMS-16: renaming `seed-password`, port 4200, the seeded emails or a UI label breaks the e2e suite, and the stack can't run on arm64 Macs. For IMS-7: restore needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1` on this machine.
- Note: `docs/plans/2026-11-16-s12-end-to-end/2026-11-16-s12-end-to-end.md:89,123`: S12 left Docker Compose to S13 and suggested pointing the suite at it.
- Graph: `graphify affected "AppHost.cs" --depth 2` found no affected nodes. The AppHost is a leaf, so its blast radius is run-time and publish-time behaviour, not callers.
- Graph: `graphify query "AppHost web PublishAsStaticWebsite compose deploy"` only reaches `web/angular.json`, whose default output goes to `dist/web/browser` (it sets no `outputPath`; `web/dist/web/browser` exists).
- Code: `src/AppHost/AppHost.cs:1-49`: the whole graph. `web` is `AddJavaScriptApp("web","../../web","start")` on port 4200 with `PORT`; `api` has `WithHttpHealthCheck("/health")`; `migrate` and `seed` are `AddProject<Projects.Api>` with `launchProfileName: null`.
- Code: `src/AppHost/AppHost.csproj:1-21`: Aspire SDK and packages at 13.6.5, and `NoWarn` already holds `ASPIRE010`.
- Code: `src/Modules/Platform/Platform/Identity/IdentityRegistration.cs:29-33`: the cookie is `HttpOnly`, `SecurePolicy.Always` and `SameSite.Strict`.
- Code: `web/proxy.conf.js:1-3`: the dev proxy for `/api` reads `services__api__http__0`, used in run mode only.
- Code: `src/Api/Program.cs:31-60` (the `migrate` and `seed` run modes; OpenAPI and Scalar map only in Development); `src/ServiceDefaults/Extensions.cs:112-120` (`/health` and `/alive` map only in Development).
- Code: `src/Api/Setup/DatabasePrincipals.cs:14,21` (the `inventory_app` password is at most 128 characters, with `CHECK_POLICY = ON`); `src/Api/Setup/MigrateCommand.cs:13`; `src/Modules/Platform/Platform/Seeding/PlatformSeeder.cs:19`.
- Code: `tests/e2e/playwright.config.ts`: `baseURL` `http://localhost:4200`, `reuseExistingServer: !CI`, ready URL `/api/auth/me`. The specs deep-link to `/stock`, `/admin/ledger` and `/documents/...` (`tests/e2e/support/app.ts:18,81,100`).
- Code: `.github/workflows/ci.yml:52-90`: the `e2e` job runs the AppHost in run mode on Node 26. `.github/scripts/check-banned-packages.sh` scans transitive NuGet packages.
- Code: `.gitignore:7`: `.env` already ignores `deploy/.env` (checked with `git check-ignore -v`). `deploy/.env.example` isn't ignored.
- Package docs (`~/.nuget/packages/aspire.hosting.javascript/13.6.5/.../Aspire.Hosting.JavaScript.xml`):
  - `PublishAsStaticWebsite(apiPath, apiTarget, options)` gives *"a YARP reverse proxy image for static file serving and API reverse-proxy … across all deployment targets (Docker Compose, Azure App Service, etc.)"*.
  - `PublishAsStaticWebsiteOptions.OutputPath` notes that *"Angular uses dist/browser"*, and `StripPrefix` exists.
  - `AddJavaScriptApp` generates a Dockerfile at publish time, and the Node version comes from the project's config files.
  - The DLL carries the experimental id `ASPIREJAVASCRIPT001`.
- NuGet: `dotnet package search` lists `Aspire.Hosting.Docker` and `Aspire.Cli` at 13.6.5 (stable). Neither is installed locally, and the `aspire` CLI isn't on PATH.

## Options
Three choices, each settled by the user on 2026-11-17.

**Packages and CLI**

| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| A. `Aspire.Hosting.Docker` + `Aspire.Cli` pinned in `.config/dotnet-tools.json` | 1 package, 1 tool | Low | Low | Bumped together with the other Aspire packages | Needs approval (not on the allowed list) |
| B. Same package, CLI installed globally and documented | 1 package | Low | The CLI version drifts from the packages | Every developer installs it by hand | Same |
| C. No packages; hand-written compose file | 0 | Medium | Isn't `aspire publish`, so it misses S13 | Drifts from the AppHost | None |

**Recommendation:** A. **Chosen:** A.

**How `web` is served in compose**

| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| A. `web.PublishAsStaticWebsite("/api", api, …)`: an Aspire-generated YARP image | AppHost only | Low | SPA fallback and port mapping are unverified until the smoke test | None: the image is generated | One origin keeps the §8 cookie rules |
| B. The API serves the SPA (`PublishWithContainerFiles` + `UseStaticFiles`/`MapFallbackToFile`) | `Program.cs` + AppHost | Medium | Changes the API pipeline, and needs a dev/publish split | Medium | Same origin |
| C. Hand-written `web/Dockerfile` with nginx | ~40 lines of config | Medium | Hand-written proxy and fallback rules | Maintained by hand | Same origin |

**Recommendation:** A, because it touches nothing under `src/Api` or `web/src`, leaves run mode unchanged, and carries over to S14. **Chosen:** A.

**Verification**

| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| A. Manual smoke test + the existing Playwright suite run locally against compose; evidence in the PR | None in CI | Low | Compose can regress unnoticed until the next manual run | None | Meets "Manual smoke test" |
| B. Add a CI job `e2e-compose` | +10–15 min of CI | Medium | Low | A second slow job, plus a required-check decision | Beyond spec |
| C. Manual smoke test only | None | Lowest | The least evidence | None | Meets the spec |

**Recommendation:** A. **Chosen:** A.

## Decisions
- **D1** Add `Aspire.Hosting.Docker` 13.6.5 to `src/AppHost/AppHost.csproj`, and pin the `aspire.cli` 13.6.5 tool in `.config/dotnet-tools.json`. CLAUDE.md's allowed list records both in this PR, so reviewers don't flag them under the "no package outside the allowed list" constraint. User-approved 2026-11-17 (CLAUDE.md, "Ask before you add any NuGet … package").
- **D2 (superseded 2026-11-17, see D10)** Serve `web` in compose through `PublishAsStaticWebsite("/api", api, o => { o.OutputPath = "dist/web/browser"; o.StripPrefix = false; })`. `StripPrefix = false` because every API route lives under `/api` (`Program.cs:56`). User-approved.
- **D10** Replaces D2. The Aspire-generated YARP image has no SPA fallback (`GET /stock` was 404), so `web` is built from a hand-written `web/Dockerfile` (node build, then `nginx:1.28-alpine` with `web/nginx.conf.template`: `try_files $uri /index.html`, `/api/` proxied to `services__api__http__0`, listening on `PORT`). The AppHost calls `.WithExternalHttpEndpoints()` and `.PublishAsDockerFile(c => c.WithEntrypoint("/docker-entrypoint.sh").WithArgs("nginx", "-g", "daemon off;"))`: Aspire's validator demands an explicit entrypoint, and compose resets the image's CMD once an entrypoint is set. User-approved 2026-11-17 (option 1: nginx).
- **D3** Verify with a manual smoke test and the existing Playwright suite against the compose stack. Neither CI nor the e2e suite changes. User-approved.
- **D4** The model moves from the provisional `haiku` to `sonnet` for both prompts. The work drives an unverified publish toolchain and Docker, and has documented contingencies to judge; a failed `haiku` run would cost more. TASKS.md says models are provisional until planning.
- **D5** Add `web/.nvmrc` (`26`), so the Dockerfile that `AddJavaScriptApp` generates uses the same Node major as CI (`ci.yml` `node-version: '26'`) and the README. Without it, the generator falls back to its own default.
- **D6** `deploy/.env` holds secrets and stays ignored through the existing `.env` rule. `deploy/.env.example` is the committed template.
- **D7** One unit, IMS-17, with no high-stakes path. CLAUDE.md doesn't require a code-guardian review for S13.
- **D8** `deploy/` is generated. A later change to the AppHost's resources, parameters or ports must re-run the publish in the same PR. This is recorded under CLAUDE.md `## Hidden coupling`.
- **D9** Audit mitigation (Codex audit, 2026-11-17): the end-to-end block uses concrete commands. The publish runs as `dotnet aspire publish --apphost src/AppHost/AppHost.csproj --output-path deploy`, the documented Aspire 13 options. The seed password is read from `SEED_PASSWORD` in `deploy/.env` (Compose's name for the `seed-password` parameter) without echoing it. If `--help` or the generated `.env` shows different names, prompt 02 reports them, and the plan block and README use those names.

**Assumptions:**
- The compose environment keeps its default Aspire dashboard container, so OpenTelemetry is visible without `dotnet run`. It is harmless for a local stack, and turning it off later is one call.
- The containers run in the `Production` environment. `appsettings.Development.json` only sets logging. `/health`, `/alive`, OpenAPI and Scalar go unmapped there, which is correct for a deploy. This breaks nothing, unless the publisher emits a `/health` healthcheck for `api`; prompt 02 checks for one and has a contingency.
- ASP.NET Core Data Protection keys live inside the `api` container, so recreating it signs users out. That's acceptable for a local smoke stack; S14 must persist them (Downstream).
- `aspire publish` (or a documented `aspire` pipeline step) builds the container images locally. The compose file references them by image name, not by `build:` context. Prompt 02 confirms which command builds them, and the README records it.
- Cookies marked `Secure` are accepted over `http://localhost` (Chromium and Firefox treat localhost as secure, and today's dev proxy relies on the same behaviour). Over a LAN IP the cookie is dropped, so the README says "localhost only".

**Non-goals:**
- Any change under `src/Api/**`, `src/Modules/**`, `src/ServiceDefaults/**`, `web/src/**` or `tests/**`.
- A CI job for compose. Changing CI at all.
- TLS or HTTPS in compose; persisting Data Protection keys; pushing images to a registry.
- Azure (S14, IMS-18).
- Changing the cookie policy.
- `TASKS.md`, `CHANGELOG.md` or `docs/plans/**` inside the PR.
- Fixing the IMS-19 hotfix line, which still shows `[~]` although PR #10 merged it. That's bookkeeping on `develop`, outside this unit.

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `src/AppHost/AppHost.csproj` | M | Add `Aspire.Hosting.Docker` 13.6.5. Add an `ASPIRE*` experimental id to `NoWarn` only if the build raises it. | R1 | 01 |
| `src/AppHost/AppHost.cs` | M | `builder.AddDockerComposeEnvironment("compose")`; `web.PublishAsStaticWebsite("/api", api, …)`. Prompt 02 makes contingency edits only (external endpoint, restart policy, healthcheck). | R1, R3, R4, R6 | 01 (02) |
| `.config/dotnet-tools.json` | C | Tool manifest pinning `aspire.cli` 13.6.5 | R1 | 01 |
| `web/.nvmrc` | C | `26`: the Node major for the generated image | R3 | 01 |
| `CLAUDE.md` | M | Allowed packages: `Aspire.Hosting.Docker` and the `aspire.cli` tool. Hidden coupling: `deploy/` follows the AppHost. | R7 | 01 |
| `deploy/docker-compose.yaml` | C | `aspire publish` output, committed | R1, R2, R4 | 02 |
| `deploy/.env.example` | C | Every key from the generated `.env`, with secret values blank and the password rules as comments | R2 | 02 |
| `README.md` | M | New `## Docker Compose` section | R5, R7 | 02 |
| `web/Dockerfile`, `web/nginx.conf.template`, `web/.dockerignore` | C | D10: nginx image with SPA fallback and `/api` proxy | R3 | 02 |

Any other file `aspire publish` writes into `deploy/` is committed too (prompt 02 lists it), except `deploy/.env`.

## Blast radius
**Risk:** medium. No high-stakes path or shared code is touched, and run mode is unchanged. The risk comes from generated output that must never carry secrets, and from a publish toolchain the repo hasn't used before.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `dotnet run --project src/AppHost` (dev; spec §13 box 1) | Direct | `AppHost.cs` | No. The compose environment and `PublishAsStaticWebsite` are publish-time annotations; run mode keeps `ng serve` and the dev proxy. | Prompt 01 runs the e2e suite in run mode |
| CI `e2e` job | Non-graph | Runs the AppHost (http profile) | No, for the same reason | The PR's CI |
| CI `build` (`dotnet build -warnaserror`) | Non-graph | The new package; an experimental API under `TreatWarningsAsErrors` | Possibly: `ASPIREJAVASCRIPT001`, or a compute or pipeline id | `NoWarn` that exact id, with a comment (prompt 01) |
| `check-banned-packages.sh` | Non-graph | Transitive dependencies of `Aspire.Hosting.Docker` | No (Aspire's own dependencies) | Prompt 01 runs it |
| `.gitignore` `.env` rule | Non-graph | `deploy/.env` | No: it's already ignored (verified) | Prompt 02 checks `git status` and greps the tracked files for every secret value |
| Hidden coupling: generated passwords set `Min*` | Non-graph | In compose, `.env` supplies the values, not `GenerateParameterDefault` | Yes, if a hand-written value is weak: `migrate` fails `CHECK_POLICY`, or `seed` fails Identity | `.env.example` comments and the README rules; prompt 02 generates compliant values |
| `inventory_app` password inside a connection string (`Special = false`, at most 128 characters) | Non-graph | `AppHost.cs:15-22`, `DatabasePrincipals.cs:14` | Yes, if it contains `;` or `=` | The rule allows letters and digits only, ≥ 12 characters |
| Review constraint "the API connects as `inventory_app`" | Non-graph | `WithReference(appConnection, "inventory")` on `seed` and `api` | No, if the publish keeps it | Prompt 02 checks, by key name, that only `migrate` gets the admin connection |
| Auth cookie (`SecurePolicy.Always`, `SameSite.Strict`) | Non-graph | `IdentityRegistration.cs:32-33` | No on `http://localhost`, through one origin (D2); yes on a LAN IP | README: localhost only. TLS is a non-goal. |
| `/health` only in Development | Non-graph | `ServiceDefaults/Extensions.cs:114` | Only if the compose output adds a `/health` healthcheck that gates `web` | Prompt 02 checks; the contingency drops that healthcheck from the compose output |
| `migrate` and `seed` as one-shot containers | Non-graph | `WaitForCompletion` | Yes, if they get a restart policy that loops them | Prompt 02 checks; the contingency sets their restart policy to `no` |
| SPA deep links in the e2e specs | Non-graph | `app.ts:81,100`, `ledger-health.spec.ts:22` | Yes, if the YARP image has no `index.html` fallback | Prompt 02 checks with `curl /stock`; on failure it stops and reports |
| `playwright.config.ts` `reuseExistingServer` | Non-graph | Compose on `:4200` | Intended (D3). It clashes if the AppHost also runs on 4200. | README: stop one before starting the other |
| SQL data volume | Non-graph | `WithDataVolume()` becomes a compose named volume | No: the compose project namespaces it apart from Aspire's dev volume | Prompt 02 reports the volume name |
| S14 (IMS-18) | Downstream | A second compute environment (Azure Container Apps) | Probably: resources need `.WithComputeEnvironment(...)` once two environments exist | Downstream note at ledger time |
| `deploy/` drift | Non-graph | Any later AppHost change | The output goes stale | CLAUDE.md hidden-coupling line (D8) |

**Known risks in the touched area:**
- IMS-16 Downstream: renaming `seed-password`, port 4200 or the seeded emails breaks the suite. All three are kept.
- The stack can't run on arm64 Macs.
- IMS-3: only `migrate` gets the server admin.
- IMS-6: the object `DENY`s live in `DatabasePrincipals`, so `migrate` re-applies them on every `up`.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-17 | S13 Compose output | Added | 01, 02 | IMS-16 | no | R1: `dotnet tool restore && dotnet aspire publish` writes the compose deployment into `deploy/`, and a second publish leaves `git diff deploy/` empty. R2: no secret value appears in any tracked file, and `deploy/.env.example` lists every key. R3: `curl` on `:4200/` and on `:4200/stock` returns 200 with the HTML shell, and `:4200/api/auth/me` returns 401. R4: `docker compose ps -a` shows `migrate` and `seed` exited 0, with `api`, `web` and `sql` up; a `down`/`up` cycle is clean. R5: the Playwright suite passes against compose, and the PR quotes the output. R6: the suite passes in run mode, and CI is green. R7: the README section and the CLAUDE.md lines exist. |

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Compose environment, static web publish, CLI tool | IMS-17 | sonnet | medium | — | A | `src/AppHost/AppHost.csproj`, `src/AppHost/AppHost.cs`, `.config/dotnet-tools.json`, `web/.nvmrc`, `CLAUDE.md` |
| 02 | Publish to `deploy/`, smoke test, README | IMS-17 | sonnet | high | 01 | B | `deploy/docker-compose.yaml`, `deploy/.env.example`, `README.md`, `web/Dockerfile`, `web/nginx.conf.template`, `web/.dockerignore`, `src/AppHost/AppHost.cs` (D10) |

No `opus` prompts: neither prompt touches a high-stakes path or shared code.

## End-to-end verification
```bash
cd "$(git rev-parse --show-toplevel)"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
(cd web && npm run lint && npm run build && npm test -- --watch=false)
bash .github/scripts/check-banned-packages.sh

# R1/R2: reproducible publish, no secrets tracked
dotnet tool restore
dotnet aspire publish --apphost src/AppHost/AppHost.csproj --output-path deploy
dotnet aspire do build --apphost src/AppHost/AppHost.csproj --output-path deploy   # builds the images
docker tag "$(docker image ls web -q | head -1)" web:latest
git diff --exit-code -- deploy/          # a second publish changes nothing
git check-ignore -q deploy/.env          # .env stays ignored
git status --porcelain deploy/           # no deploy/.env listed

# R3/R4: run without Aspire
cd deploy && docker compose up -d && docker compose ps -a      # migrate, seed: Exited (0); sql, api, web: running
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:4200/            # 200
curl -s http://localhost:4200/stock | grep -c '<app-root'                  # ≥ 1 (SPA fallback)
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:4200/api/auth/me # 401

# R5: the e2e suite against compose (not CI, so it reuses :4200)
unset CI
export E2E_PASSWORD="$(sed -n 's/^SEED_PASSWORD=//p' .env)"   # still in deploy/; never echoed
[ -n "$E2E_PASSWORD" ] || echo "SEED_PASSWORD missing in deploy/.env"
cd ../tests/e2e && npx playwright test

# R4: replay
cd ../../deploy && docker compose down && docker compose up -d && docker compose ps -a   # seed exits 0 again

# R6: run mode unchanged (with the compose stack down)
docker compose down
cd ../tests/e2e && npx playwright test     # starts the AppHost itself
```

## Rollback
Revert the PR. No schema, data or API contract changes. Locally, `cd deploy && docker compose down -v` removes the compose stack and its volume, which is separate from Aspire's dev volume. Remove the tool with `dotnet tool uninstall aspire.cli` (local manifest).

## Log
- 2026-11-17: planned. Re-plans the legacy line IMS-17 (`spec: §12 S13`). The user chose D1 (package + local tool), D2 (YARP static site) and D3 (local e2e against compose).
- 2026-11-17: applied Codex audit (1 applied, 0 disputed): concrete publish and seed-password commands in the end-to-end block and prompt 02. The older section was superseded (1 applied, 1 disputed: the replay path, withdrawn by Codex). Status `audited`. The ticket step rewrote the TASKS.md IMS-17 line.
- 2026-11-17: build started on IMS-17; rollback point a70c7cef2f9403f3ba8444996b9a998f0f8f9e09.
- 2026-11-17: 01 done (sonnet, 0 retries): added NoWarn ASPIREJAVASCRIPT001; `.config/dotnet-tools.json` already existed (dotnet-ef), so aspire.cli was added to it; installed Playwright chromium locally; build, format, 580 tests, web lint/build/test, banned-packages and run-mode e2e (4/4) green.
- 2026-11-17: 02 BLOCKED at step 7 (sonnet): publish, image build and `compose up` work (migrate/seed exit 0, `/` 200, `/api/auth/me` 401, web on 4200 after adding `.WithExternalHttpEndpoints()` to `web` in AppHost.cs). `GET /stock` returns 404: the Aspire-generated YARP image (`PublishAsStaticWebsite`, D2) has no SPA fallback and no option to enable one. Prompt 02 stays `todo`. Findings for the plan: the CLI option is `--apphost` (not `--project`); images are built with `dotnet aspire do build --apphost … --output-path deploy` and tagged `api|migrate|seed:latest` and `web:<content hash>` (tag it `web:latest`); `publish` also writes `deploy/web.Dockerfile` and `deploy/.env` with empty image/port values (`API_PORT=8080`).
- 2026-11-17: D10 applied: user chose nginx (option 1). 02 done (sonnet, 1 retry after the YARP blocker): web/Dockerfile + nginx template; `.WithExternalHttpEndpoints()`; `.PublishAsDockerFile(...)` with explicit entrypoint and args. Evidence: migrate/seed exit 0; `/` 200; `/stock`, `/admin/ledger`, `/documents/x` serve the shell; `/api/auth/me` 401; Playwright against compose 4/4; down/up replay clean; second publish diff-free; run-mode Playwright 4/4; secret check clean. Volume `deploy_apphost-feed4158f1-sql-data`; web publishes 4200->4200.
- 2026-11-17: IMS-17 merged (PR #17); plan done.

## Outcome
**Shipped:** `deploy/` compose output, `web/Dockerfile` + nginx, `Aspire.Hosting.Docker` and `aspire.cli` 13.6.5, README section, CLAUDE.md updates. Smoke test and Playwright (compose and run mode) passed; CI green.
**Deviations:** D2 (YARP static site) was replaced by nginx (D10) because the generated image has no SPA fallback. The CLI option is `--apphost`; images need a separate `dotnet aspire do build`; `web`'s tag is a content hash, so it is retagged `web:latest`.
**Gotchas:** `PublishAsDockerFile` needs an explicit entrypoint, and compose then resets CMD, so args are restated. Publish does not overwrite an existing `deploy/.env`. The old generated `deploy/web.Dockerfile` is no longer produced.
