---
type: prompt
plan: "[[2026-11-17-s13-compose-output]]"
id: 02
unit: IMS-17
status: todo
model: sonnet
effort: high
depends_on: [01]
parallel_group: B
files:
  create:
    - deploy/docker-compose.yaml
    - deploy/.env.example
  modify:
    - README.md
    - src/AppHost/AppHost.cs   # contingency edits only, listed below
  delete: []
---
# 02 · Publish to `deploy/`, smoke test, README

## Task
1. Run `aspire publish` into `deploy/`.
2. Make the committed output secret-free and reproducible.
3. Bring the stack up with `docker compose` alone, smoke-test it, and run the existing Playwright suite against it.
4. Document the flow in a new README section.

Report the evidence (commands and their output, with secrets never printed) so it can go into the PR description.

## Context
- **Repo and branch:** the repo root is `git rev-parse --show-toplevel`; the branch must be `IMS-17`. `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`. This is an x64 Linux host with Docker Compose v5 (SQL Server 2025 has no arm64 image).
- **Prompt 01 is done:**
  - `src/AppHost/AppHost.cs` has `builder.AddDockerComposeEnvironment("compose");` and `web … .PublishAsStaticWebsite("/api", api, o => { o.OutputPath = "dist/web/browser"; o.StripPrefix = false; })`.
  - `.config/dotnet-tools.json` pins `aspire.cli` 13.6.5, so run it as `dotnet aspire …` after `dotnet tool restore`.
  - The AppHost graph: `sql` (`2025-latest`, data volume) → `migrate` (`Api migrate`, admin connection `ConnectionStrings__inventory` via `WithReference(db)`, env `InventoryApp__Password`) → `seed` (`Api seed`, `inventory_app`, env `Seed__Password`) → `api` (`inventory_app`) → `web` (port 4200).
  - The parameters are `inventory-app-password`, `seed-password` and the SQL resource's own password.
- **Spec §12 S13:** *"`aspire publish` to Docker Compose, committed under `deploy/`, verified running without Aspire. Manual smoke test."*
- **User decision D3:** smoke test plus the existing Playwright suite against compose; evidence goes in the PR. No CI change.

### Secrets (hard rules)
- Never print a value from `deploy/.env`. To see it, list keys only: `sed 's/=.*/=/' deploy/.env`. Print the value of a key only if it is plainly non-secret, such as an image name, tag or port.
- `deploy/.env` must stay untracked. It is ignored today by `.gitignore:7` (`.env`); confirm with `git check-ignore -v deploy/.env`.
- Before finishing, prove that no secret value appears in any file you will leave for commit:
  ```bash
  for k in <each secret key>; do v="$(sed -n "s/^$k=//p" deploy/.env)"; [ -n "$v" ] && ! grep -rqF -- "$v" deploy/docker-compose.yaml deploy/.env.example README.md && echo "$k clean"; done
  ```

### Steps
1. **Read the CLI.** Run `dotnet tool restore`, then `dotnet aspire publish --help`. Find:
   - the option that selects the AppHost project (`src/AppHost/AppHost.csproj`);
   - the output-path option;
   - a non-interactive flag, if one exists.

   Use these exact options from here on, and record them in the README. The plan expects `--project` and `--output-path`. If `--help` shows different names, use those and list them in the report, so the plan's end-to-end block can be corrected.
2. **Publish.** Run `dotnet aspire publish <apphost option> src/AppHost/AppHost.csproj <output option> deploy` from the repo root. List what landed with `ls -la deploy`.
   - If it prompts for parameter values, generate compliant ones (step 4) and enter them, or pass them the way `--help` documents. Never echo them.
3. **Inspect `deploy/docker-compose.yaml`** (it holds no secrets, only `${VAR}` references; confirm that by the secret check above). Check each item:
   - **Services:** `sql`, `migrate`, `seed`, `api` and `web` exist, plus the dashboard if the compose environment adds one (keep it).
   - **Ordering:** `migrate` depends on `sql`; `seed` depends on `migrate` with `condition: service_completed_successfully`; `api` depends on `seed` the same way; `web` depends on `api`.
   - **Admin connection:** only `migrate`'s connection string uses the SQL admin (`sa`). `seed` and `api` use `User ID=inventory_app`. This is a review constraint: the API connects as `inventory_app`, never as the server admin.
   - **Web:** it publishes host port `4200`, and its image is the generated YARP static site.
   - **Restart policy:** neither `migrate` nor `seed` has one that restarts after a clean exit (`always` or `unless-stopped`).
   - **Healthcheck:** no healthcheck targets `/health` on `api`. `/health` is mapped only in Development (`src/ServiceDefaults/Extensions.cs:114`), and the containers run in Production.
   - **Images:** every project image (`api`, `migrate`, `seed`, `web`) is referenced by name, as `image:` or a `*_IMAGE` key in `.env`.
4. **`.env` values.** List the keys of `deploy/.env`. For each secret key that is empty, fill it in place (`sed -i "s|^KEY=.*|KEY=$v|"` with `v` generated in the shell, never echoed):
   - **SQL admin password:** `v="Sa1$(openssl rand -hex 16)Z"`. SQL Server complexity needs three of: upper, lower, digit, symbol.
   - **inventory_app password:** `v="App$(openssl rand -hex 16)Z9"`. Letters and digits only, because it is embedded in a connection string (`AppHost.cs:16-17`), and at most 128 characters (`src/Api/Setup/DatabasePrincipals.cs:14`). `CHECK_POLICY = ON` applies.
   - **Seed password:** `v="Seed!$(openssl rand -hex 12)A9"`. Identity's default policy needs an upper, a lower, a digit and a symbol, length ≥ 6. CI uses the same shape (`.github/workflows/ci.yml:69-71`).
   - If the publish already wrote values (generated defaults), keep them, after checking each has the required character classes with `grep -q` patterns, without printing.
5. **Images.** Check `docker image ls` for the images the compose file references.
   - If `aspire publish` didn't build them, find the step that builds images locally without starting the stack (`dotnet aspire do --help`, `dotnet aspire publish --help`) and run it.
   - Use `aspire deploy` only if no build-only step exists; then `docker compose down` before step 6, so the smoke test starts the stack by hand.
   - Record the exact build command for the README.
6. **Run without Aspire.** Make sure nothing listens on `:4200` (`ss -ltn | grep ':4200'`), so no AppHost is running. Then:
   ```bash
   cd deploy && docker compose up -d
   docker compose ps -a      # wait until migrate and seed show Exited (0) and api/web/sql are Up; poll, max ~5 min
   ```
   If a service fails, run `docker compose logs <svc> | tail -50`, which shows no secrets.
7. **Smoke test:**
   ```bash
   curl -s -o /dev/null -w '%{http_code}\n' http://localhost:4200/            # 200
   curl -s http://localhost:4200/stock | grep -c '<app-root'                  # ≥1: SPA deep-link fallback
   curl -s -o /dev/null -w '%{http_code}\n' http://localhost:4200/api/auth/me # 401: proxied to the API
   docker compose ps --format '{{.Service}} {{.Ports}}'                       # note which services publish host ports
   docker volume ls | grep -i sql                                             # note the compose volume name
   ```
8. **The e2e suite against compose.** The suite reuses a server on `:4200` when `CI` is unset (`tests/e2e/playwright.config.ts`, `reuseExistingServer: !process.env['CI']`).
   ```bash
   cd "$(git rev-parse --show-toplevel)"
   unset CI
   export E2E_PASSWORD="$(sed -n 's/^SEED_PASSWORD=//p' deploy/.env)"   # never echo; SEED_PASSWORD is Compose's name for the seed-password parameter
   [ -n "$E2E_PASSWORD" ] || echo "SEED_PASSWORD missing"   # if missing, find the real key with: sed 's/=.*/=/' deploy/.env | grep -i seed
   (cd tests/e2e && npm ci && npx playwright test)
   ```
   Every spec must pass. Save the summary lines (counts and timings) for the report.
9. **Replay:** `cd deploy && docker compose down && docker compose up -d`. `seed` exits 0 again: users and opening stock already exist, and their idempotency keys replay. Re-run step 7's three `curl` checks.
10. **Reproducibility:** run `docker compose down`, then copy `deploy/docker-compose.yaml` (and any other generated non-secret file) aside, for example to a `mktemp -d` directory. From the repo root, run step 2's publish command again, then `diff -r` the copies against `deploy/`, excluding `.env`.
    - Expected: no diff.
    - If parts change on every run (timestamps, random tags), report exactly which lines. Don't hand-edit generated output to hide it.
11. **`deploy/.env.example`.** Write the same keys, in the same order as `deploy/.env`:
    - Non-secret values (image names, ports) are copied.
    - Secret values are empty.
    - Above each password key, add a `#` comment giving its rule from step 4, and the command that generates a compliant value.
    - The first lines say: `# Copy to .env (git-ignored) and fill the passwords. Generated by aspire publish; see README "Docker Compose".`
12. **README.** Add a `## Docker Compose` section after `## End-to-end tests` (`README.md:4`), in the same terse style:
    - What it is (S13: `aspire publish` output under `deploy/`, runs without Aspire).
    - Prerequisites (.NET SDK from `global.json`, Docker on x64).
    - Regenerate: `dotnet tool restore` plus the exact publish and image-build commands. Re-run them after any AppHost change.
    - Configure: `cp deploy/.env.example deploy/.env`, plus the password rules.
    - Run: `cd deploy && docker compose up -d`, then `http://localhost:4200`, signing in with the seeded users and the `.env` seed password.
    - Localhost only: the auth cookie is `Secure` and `SameSite=Strict`, and browsers accept it over HTTP only on localhost.
    - Run the e2e suite against it: `E2E_PASSWORD` from `deploy/.env`, and don't run the AppHost at the same time, because both use port 4200.
    - Stop with `docker compose down`, and add `-v` to drop the database volume.
    - Known limits: sign-in sessions don't survive recreating the `api` container (Data Protection keys aren't persisted); no TLS.

### Contingencies: the only allowed edits to `src/AppHost/AppHost.cs`
Make each edit only when its check fails, re-publish (step 2), and list it in the report:
- **`web` publishes no host port 4200:** add `.WithExternalHttpEndpoints()` to the `web` chain.
- **`migrate` or `seed` restarts after a clean exit:** add `.PublishAsDockerComposeService((_, svc) => svc.Restart = "no")` to that resource. Check the delegate's parameter order and the property name with the compiler or IntelliSense; if the API differs, report it instead of guessing.
- **A healthcheck on `/health` gates `web`:** stop and report. Don't map `/health` in Production (it's ServiceDefaults, outside scope) and don't remove `WithHttpHealthCheck` (run mode uses it).

For any other failure, stop and report the evidence; don't improvise. That covers:
- no SPA fallback on `/stock`;
- `seed` or `api` using the admin connection;
- the Node or web image build failing;
- a publish that isn't reproducible.

## Do not
- Touch files outside the `files` list (plus any extra non-secret file that `aspire publish` itself writes into `deploy/`). If you must, stop and report why.
- Commit, stage for commit or print `deploy/.env` or any secret value. Never print `.env` lines or `docker compose config` output, because it expands secrets.
- Change anything under `src/Api`, `src/Modules`, `src/ServiceDefaults`, `web/src`, `tests/` or `.github/`.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.
- Leave the compose stack running at the end (`docker compose down`, keeping the volume), or leave an AppHost running.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
(cd web && npm run lint && npm run build && npm test -- --watch=false)
bash .github/scripts/check-banned-packages.sh
git check-ignore -q deploy/.env && echo ignored
git status --porcelain      # deploy/docker-compose.yaml, deploy/.env.example, README.md (+ contingency AppHost.cs); never deploy/.env
```
- Steps 6–10 passed. The report contains:
  - the publish and image-build commands;
  - the `docker compose ps -a` table after `up`;
  - the three `curl` results;
  - the Playwright summary against compose;
  - the replay result;
  - the reproducibility result;
  - the secret check result (`… clean` per key);
  - the published host ports;
  - the volume name;
  - any contingency edits made.
