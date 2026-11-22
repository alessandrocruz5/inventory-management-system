# inventory-management-system
General Inventory Management System with AI capabilities

Live URL: https://web.purplesky-914b6fa4.southeastasia.azurecontainerapps.io/login

## End-to-end tests
Playwright specs in `tests/e2e` cover the stock flow (T14: receive, issue, reverse), the `/admin/ledger` page as Admin and Staff, and the post-stock form at 375 px. CI's `e2e` job runs them on every PR.

**Prerequisites:** the .NET SDK from `global.json`, Node 26, and Docker on an **x64** host. SQL Server 2025 has no ARM64 image, so the stack does not run on Apple-silicon Macs.

**Password:** the specs sign in with the AppHost's persisted `seed-password`, because the seed only creates missing users. Export it without printing it:

```bash
export E2E_PASSWORD="$(dotnet user-secrets list --project src/AppHost | sed -n 's/^Parameters:seed-password = //p')"
```

**Run:**

```bash
cd tests/e2e && npm ci && npx playwright install chromium && npx playwright test
```

The suite reuses a running AppHost, or starts one (`--launch-profile http`).

Set `E2E_BASE_URL` to run the suite against an already running stack (compose or Azure) without starting the AppHost.

**Side effects on a reused database:** every run adds `E2E-…` locations holding `CANDLE-WHT` stock and rebuilds the ledger on the demo tenant. Nothing can be hard-deleted.

**Coupling:** renaming the AppHost's `seed-password` parameter, the `web` port 4200, the seeded user emails, or a UI label the specs use will break the suite.

## Docker Compose
`deploy/` is `aspire publish` output of `src/AppHost/AppHost.cs` (S13). It runs with `docker compose` alone, without Aspire: `sql`, then `migrate` (server admin) and `seed` (`inventory_app`) once each, then `api` and `web`. `web` is `web/Dockerfile`: nginx serves the production Angular build and proxies `/api` to `api`, so the browser sees one origin.

**Prerequisites:** the .NET SDK from `global.json` and Docker with Compose, on an **x64** host.

**Regenerate** after any AppHost change (resources, parameters, ports, Aspire version) and commit the result:

```bash
dotnet tool restore
dotnet aspire publish --apphost src/AppHost/AppHost.csproj --output-path deploy   # writes deploy/docker-compose.yaml
dotnet aspire do build --apphost src/AppHost/AppHost.csproj --output-path deploy  # builds the api, migrate, seed and web images
docker tag "$(docker image ls web -q | head -1)" web:latest                        # web's tag is a content hash; .env uses web:latest
```

**Configure:** `cp deploy/.env.example deploy/.env`, then fill the three passwords. The comments in the file give each rule and a command that generates a compliant value. `deploy/.env` is git-ignored.

**Run:** `cd deploy && docker compose up -d`, then open <http://localhost:4200> and sign in with a seeded user and the `.env` `SEED_PASSWORD`. `migrate` and `seed` exit 0 and stay exited; a second `up` replays them without changes.

**Localhost only:** the auth cookie is `Secure` and `SameSite=Strict`. Browsers accept it over plain HTTP only on `localhost`, so a LAN address won't sign in. There is no TLS.

**e2e against it:** stop the AppHost first, because both use port 4200. Then, with `CI` unset:

```bash
export E2E_PASSWORD="$(sed -n 's/^SEED_PASSWORD=//p' deploy/.env)"
cd tests/e2e && npx playwright test
```

**Stop:** `docker compose down`; add `-v` to drop the database volume.

**Sessions:** Data Protection keys live in `platform.DataProtectionKeys`, so a sign-in survives recreating the `api` container.

## Azure
`deploy/azure/main.bicep` is hand-written (not `aspire publish` output) and describes what runs where (S14). `web` is the S13 nginx image on a Container App with HTTPS and scale to zero; it proxies `/api` to `api`, an internal-only Container App that also scales to zero. `migrate` (SQL admin) and `seed` (`inventory_app`) run as Container Apps Jobs, in that order, started by `deploy.sh`. Azure SQL uses the free offer and auto-pauses when its free monthly limit runs out.

**Why not Static Web Apps:** linking a Container App as its backend needs the Standard plan (about US$9/month), and the auth cookie needs a single origin.

**Prerequisites:**
- The az CLI with Bicep, signed in with `az login`; Docker; and the .NET SDK, on an **x64** host.
- A subscription whose free Azure SQL database is still unused.
- `docker login ghcr.io`, for example `gh auth refresh -s write:packages` then `gh auth token | docker login ghcr.io -u <user> --password-stdin`.
- After the first push, make the `inventory-api` and `inventory-web` packages **public** in GitHub's package settings.

**Configure:** `cp deploy/azure/.env.example deploy/azure/.env`, then fill it. The file holds each rule and a command that generates a compliant value, and it is git-ignored.

**Deploy:**

```bash
deploy/azure/deploy.sh               # builds and pushes the images, deploys, runs migrate then seed
deploy/azure/deploy.sh --skip-build  # reuse images already pushed
```

It prints `WEB_URL`. A rerun is safe: `seed` is a replay.

**Verify:**

```bash
curl -s -o /dev/null -w '%{http_code}\n' "$WEB_URL/api/auth/me"   # 401 when signed out
E2E_BASE_URL="$WEB_URL" E2E_PASSWORD=<SEED_PASSWORD> npx playwright test   # from tests/e2e
```

The suite posts stock to, and rebuilds the ledger of, the demo tenant.

**Cold starts:** the first request after about 15 minutes idle can take up to a minute, while the `api` starts and the database resumes.

**Cost:**

| Service | Free allowance |
| --- | --- |
| Container Apps | Monthly free grant; scale to zero, max 1 replica |
| Azure SQL free offer | 100,000 vCore-seconds and 32 GB per month; AutoPause when used up |
| Log Analytics | 1 GB/day cap, 30-day retention |
| GHCR | Free for public packages |

Expect about US$0 at demo traffic. Set an Azure budget alert anyway.

**Teardown:** `az group delete -n "$AZ_RESOURCE_GROUP" --yes`.

**Limits:**
- Demo data only. The SQL firewall allows Azure services (no private endpoint), and Data Protection keys sit unencrypted in the database.
- A redeploy briefly runs the new `api` before `migrate`.
- The region is `southeastasia`; real tenant data needs a Data Privacy Act (RA 10173) review first.
