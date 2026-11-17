# inventory-management-system
General Inventory Management System with AI capabilities

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
