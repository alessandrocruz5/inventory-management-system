---
type: prompt
plan: "[[2026-11-17-s14-azure]]"
id: 05
unit: IMS-18
status: todo
model: sonnet
effort: high
depends_on: [04]
parallel_group: C
files:
  create: []
  modify:
    - README.md
    - CLAUDE.md
    - docs/Inventory System — Sprint 1 Spec.md
    - deploy/azure/main.bicep   # contingency only (step 5)
  delete: []
---
# 05 · Docs, spec amendment, deploy and smoke test

## Task
1. Add `## Azure` to the README, and an `E2E_BASE_URL` note to its e2e section.
2. Add the hidden-coupling line to CLAUDE.md.
3. Amend spec §12/§4/§15 for the Static Web Apps deviation.
4. **With the user's explicit go-ahead only**, deploy with `deploy/azure/deploy.sh` and run the smoke test. Report the evidence for the PR description.

## Context
- **Repo and branch:** the repo root is `git rev-parse --show-toplevel`. Work on the branch you were given (`IMS-18`), and stop if `git branch --show-current` isn't it.
- **The plan** `docs/plans/2026-11-17-s14-azure/2026-11-17-s14-azure.md` holds the decisions D1–D11 and the end-to-end block. Prompt 04 wrote `deploy/azure/{main.bicep,deploy.sh,.env.example}`. Read them for the exact key names before you write the docs.
- **README.md**:
  - It has `## End-to-end tests` (line 4) and `## Docker Compose` (line 27). Copy their style: a short intro, then **Prerequisites/Configure/Run/…** paragraphs and fenced bash.
  - Add `## Azure` after `## Docker Compose`. Cover these points:
    - **Intro:** `deploy/azure/main.bicep` is hand-written (not `aspire publish`); it describes what runs where.
      - `web` is nginx, on HTTPS, scaling to zero, and proxies `/api` to an internal-only `api`, also scaling to zero.
      - `migrate` (SQL admin) and `seed` (`inventory_app`) run as Container Apps Jobs, in that order, started by `deploy.sh`.
      - Azure SQL is on the free offer and auto-pauses when its free monthly limit runs out.
    - **Why not Static Web Apps:** linking a Container App backend needs the Standard plan (about US$9/month), and the cookie needs one origin.
    - **Prerequisites:**
      - az CLI with Bicep, `az login`, Docker, and the .NET SDK on x64.
      - A subscription whose free Azure SQL database is unused.
      - `docker login ghcr.io` (e.g. `gh auth token | docker login ghcr.io -u <user> --password-stdin` after `gh auth refresh -s write:packages`).
      - After the first push, make the `inventory-api` and `inventory-web` packages **public** in GitHub's package settings.
    - **Configure:** `cp deploy/azure/.env.example deploy/azure/.env` and fill it. The file has the rules and generators, and it's git-ignored.
    - **Deploy:** `deploy/azure/deploy.sh` (use `--skip-build` to reuse pushed images). It prints `WEB_URL`, and a rerun is safe.
    - **Verify:** the curl checks and Playwright with `E2E_BASE_URL="$WEB_URL" E2E_PASSWORD=…`. The suite posts to and rebuilds the demo tenant.
    - **Cold starts:** the first request after ~15 min idle can take up to a minute (the api starts and the database resumes).
    - **Cost:**
      - A table: Container Apps (monthly free grant, scale to zero, max 1 replica); Azure SQL free offer (100,000 vCore-seconds and 32 GB per month, AutoPause); Log Analytics (1 GB/day cap, 30-day retention); GHCR (free for public packages).
      - Expected about US$0 at demo traffic. Suggest an Azure budget alert.
    - **Teardown:** `az group delete -n "$AZ_RESOURCE_GROUP" --yes`.
    - **Limits:**
      - Demo data only. The SQL firewall allows Azure services (no private endpoint), and Data Protection keys sit unencrypted in the database.
      - A redeploy briefly runs the new `api` before `migrate`.
      - Region `southeastasia`; real tenant data would need a Data Privacy Act (RA 10173) review first.
  - In `## End-to-end tests`, add one sentence: set `E2E_BASE_URL` to run the suite against an already running stack (compose or Azure) without starting the AppHost.
- **CLAUDE.md** `## Hidden coupling`: add a bullet after the `deploy/` line:
  - `` `deploy/azure/main.bicep` is hand-written (S14): it mirrors the API's run-mode args (`migrate`, `seed`), env keys (`ConnectionStrings__inventory`, `InventoryApp__Password`, `Seed__Password`), port 8080 and the nginx upstream `http://api`. Changing any of them, or `web/nginx.conf.template`, updates the Bicep in the same PR, and only the `migrate` job gets the SQL admin connection string. ``
  - Change nothing else.
- **Spec** `docs/Inventory System — Sprint 1 Spec.md`:
  - §12 row S14 (line ~683), the Delivers cell: `Container Apps (scale to zero) for \`api\` (internal) and \`web\` (S13's nginx image, one origin) + Azure SQL free offer, from hand-written Bicep in \`deploy/azure/\`. Static Web Apps was dropped: linking a backend needs its Standard plan`. The tests and tier cells stay the same.
  - §4 tree (line ~184): under `deploy/`, add a line `deploy/azure/  Bicep + deploy.sh (S14, stretch)`, in the same code block style.
  - §15 Log: `- 2026-11-17: S14 plan ([[2026-11-17-s14-azure]]) replaced Static Web Apps with nginx on Container Apps and added Data Protection keys in the database.`
  - Don't touch §6.
- **Deploy and smoke.** This is a **human gate**: it creates billable resources in the user's Azure subscription.
  - Before step 1, ask the user to confirm that they've filled `deploy/azure/.env`, run `az login` and `docker login ghcr.io`, and want to deploy now. If you can't ask (you run as a subagent), stop after the docs and report `READY FOR DEPLOY` with the commands below.
  - Never print `.env` values.
  1. `deploy/azure/deploy.sh`. On the first run, after the push and before the deploy finishes pulling, the GHCR packages must be public. If the apps fail to pull, ask the user to flip the visibility, then rerun with `--skip-build`.
  2. Run the plan's end-to-end Azure block. First confirm the `migrate` job's latest execution is `Succeeded`: `az containerapp job execution list -g "$AZ_RESOURCE_GROUP" -n migrate --query "[0].properties.status" -o tsv`. That's the RCSI check (D14): on Azure, the guarded `EnableReadCommittedSnapshot` migration throws unless RCSI is on. Then check the curl codes, `ingress.external` false for `api`, `minReplicas` 0 for both apps, the free-offer fields, and Playwright with `E2E_BASE_URL`.
  3. **R5, scale to zero:**
     - Sign in with curl and a cookie jar: `curl -s -c jar -H 'Content-Type: application/json' -d '{"email":"<seeded admin email>","password":"'"$SEED_PASSWORD"'"}' "$WEB_URL/api/auth/login"`. Take the seeded emails from `src/Modules/Platform/Platform/Seeding/PlatformSeeder.cs` or `tests/e2e/support/app.ts`.
     - Wait until `az containerapp replica list -g "$AZ_RESOURCE_GROUP" -n api -o tsv` is empty. Poll every 60 s, for up to 20 min.
     - Then `curl -s -b jar -o /dev/null -w '%{http_code}' "$WEB_URL/api/auth/me"` must return `200`.
  4. Rerun `deploy/azure/deploy.sh --skip-build`. It succeeds, and the `seed` job succeeds again (replay).
  5. **Contingencies.** Edit `main.bicep` only for these, then redeploy and report:
     - If `/api` from `web` returns 404 or loops, set `web`'s `services__api__http__0` to `https://${api.properties.configuration.ingress.fqdn}` and add `proxy_ssl_server_name on;`. That second line is in `web/nginx.conf.template`, which is outside this prompt's files, so stop and report instead of editing it.
     - If the `migrate` job fails with `READ_COMMITTED_SNAPSHOT is OFF on this Azure SQL database`, someone turned RCSI off. **Stop and report.** The fix is `ALTER DATABASE [inventory] SET READ_COMMITTED_SNAPSHOT ON` run from `master` as the SQL admin, and that's for the user to run. Never edit a migration in this prompt.
     - If the `migrate` job fails on any other migration SQL, **stop and report** the job's log (`az containerapp job logs show`, secrets redacted).
  6. Ask the user whether to keep the deployment running or tear it down (`az group delete`). Don't delete it unasked.

## Do not
- Touch files outside the `files` list. Edit `main.bicep` only for the step 5 contingency.
- Deploy without the user's explicit go-ahead in this session.
- Print secrets, cookies or `.env` values. Redact the cookie value in any output you report.
- Edit spec §6.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --max-parallel-test-modules 1
(cd web && npm run lint && npm run build && npm test -- --watch=false)
grep -n '^## Azure' README.md && grep -n 'deploy/azure/main.bicep' CLAUDE.md && grep -n 'deploy/azure' "docs/Inventory System — Sprint 1 Spec.md"
git ls-files -z | xargs -0 grep -l -F -f <(sed -n 's/^[A-Z_]*PASSWORD=//p' deploy/azure/.env | grep .) 2>/dev/null | wc -l   # 0: no secret in tracked files
```
- The deploy evidence is reported for the PR description: the script output (no secrets), the job statuses, every curl code, the `az` query results, the Playwright summary, the scale-to-zero cookie result and the rerun result. Or `READY FOR DEPLOY` if the gate wasn't passed.
