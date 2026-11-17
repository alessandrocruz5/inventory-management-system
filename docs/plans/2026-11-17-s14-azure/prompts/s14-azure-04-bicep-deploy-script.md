---
type: prompt
plan: "[[2026-11-17-s14-azure]]"
id: 04
unit: IMS-18
status: todo
model: opus
effort: high
depends_on: [03]
parallel_group: B
files:
  create:
    - deploy/azure/main.bicep
    - deploy/azure/deploy.sh
    - deploy/azure/.env.example
  modify: []
  delete: []
---
# 04 · Bicep and deploy script

## Task
Write the Azure deployment as hand-written IaC under `deploy/azure/`:
1. `main.bicep`, at resource-group scope: Log Analytics, a Consumption Container Apps environment, an Azure SQL server and its free-offer database, Container Apps `api` (internal) and `web` (external), Container Apps Jobs `migrate` and `seed`, and output `webUrl`.
2. `deploy.sh`: load `.env`, validate it, build and push the two images to GHCR, deploy the Bicep with a temporary parameters file, run `migrate` and then `seed` and wait for each, warm up, and print the URL.
3. `.env.example`: every key, with blank secrets and the password rules.

Don't deploy anything in this prompt. Prompt 05 deploys, after the user approves.

## Context
- **Repo and branch:** the repo root is `git rev-parse --show-toplevel`. Work on the branch you were given (`IMS-18`), and stop if `git branch --show-current` isn't it.
- **Prerequisite:** the az CLI with Bicep (`az bicep version`). If `az` is missing, stop and report "install azure-cli (e.g. `yay -S azure-cli`) and run `az bicep install`". Never use sudo yourself. `shellcheck` isn't installed, so `bash -n` is the syntax check.
- **User decisions** (plan `2026-11-17-s14-azure`):
  - D1: Bicep plus the az CLI, no Aspire Azure packages, no AppHost change.
  - D2: `web` is the nginx image; `api` has internal ingress with `allowInsecure: true` on target port 8080; `web`'s nginx proxies to `http://api`; no Static Web Apps.
  - D4: public GHCR, one API image for `migrate`, `seed` and `api`.
  - D6: SQL authentication, with an admin used only by `migrate`.
  - D7: manual-trigger Jobs.
  - D10: cost guardrails.
  - D11: the firewall rule `AllowAllWindowsAzureIps`.
- **What the containers need.** Mirror `deploy/docker-compose.yaml`, which is what S13 verified.
  - The API image (`dotnet publish -t:PublishContainer`, listening on 8080 through the base image's `ASPNETCORE_HTTP_PORTS=8080`) takes the run mode as its first argument: `args: ['migrate']`, `args: ['seed']`, or none for the API (`src/Api/Program.cs:33-45`).
  - `migrate`: `ConnectionStrings__inventory` = the **admin** string, and `InventoryApp__Password`.
  - `seed`: `ConnectionStrings__inventory` = the **inventory_app** string, and `Seed__Password`.
  - `api`: `ConnectionStrings__inventory` = the **inventory_app** string, and `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. Never the admin string (review constraint: the API connects as `inventory_app`).
  - `web` (`web/Dockerfile`, nginx with the template in `/etc/nginx/templates`):
    - `PORT=8080`, `services__api__http__0=http://api`, external ingress on target port 8080, `allowInsecure: false`.
    - No command or args override: the image's own entrypoint and CMD apply. In compose they were restated only because compose resets CMD.
  - Connection strings, built in Bicep from `sqlServer.properties.fullyQualifiedDomainName`, stored only as Container Apps **secrets** (`secretRef`), never as plain env:
    - `Server=tcp:<fqdn>,1433;Database=inventory;User ID=<login>;Password=<pw>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60`.
    - The 60 s timeout covers resuming a paused free-offer database.
  - `migrate` needs no `InventoryApp__` connection string: `DatabasePrincipals` (prompt 02) creates the contained user `inventory_app` on Azure SQL.
- **Resources** (pin these API versions; `az bicep build` must report no warnings, and BCP037/BCP081 mean a wrong property or version):
  - **`Microsoft.OperationalInsights/workspaces@2023-09-01`**: `PerGB2018`, `retentionInDays: 30`, `workspaceCapping: { dailyQuotaGb: 1 }`.
  - **`Microsoft.App/managedEnvironments@2024-03-01`**: a Consumption environment (no workload profiles needed), with `appLogsConfiguration` set to `log-analytics` and the workspace's customerId and shared key (`listKeys()`).
  - **`Microsoft.Sql/servers@2023-08-01`**: name `'${namePrefix}-sql-${uniqueString(resourceGroup().id)}'`, `administratorLogin`/`administratorLoginPassword` from params, `minimalTlsVersion: '1.2'`, `publicNetworkAccess: 'Enabled'`.
    - Child `firewallRules` named `AllowAllWindowsAzureIps`, start and end IP `0.0.0.0`.
    - Don't set Entra-only authentication.
  - **`Microsoft.Sql/servers/databases@2023-08-01`**: name `inventory`, `sku: { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }`, `properties: { useFreeLimit: true, freeLimitExhaustionBehavior: 'AutoPause', autoPauseDelay: 60, minCapacity: json('0.5') }`.
    - If the API version rejects `useFreeLimit`, move to the newest version that has it (Learn: `Microsoft.Sql/servers/databases@2025-01-01`), and report it.
    - Microsoft documents that you can't switch back to AutoPause, so never use `BillOverUsage`.
  - **`Microsoft.App/containerApps@2024-03-01` `api`**: the name must be exactly `api`, because nginx resolves `http://api`.
    - `ingress: { external: false, targetPort: 8080, transport: 'http', allowInsecure: true }`.
    - `scale: { minReplicas: 0, maxReplicas: 1, rules: [ http, concurrentRequests '20' ] }`.
    - One container `api`, `cpu: json('0.5')`, `memory: '1Gi'`.
    - No `probes`: `/health` exists only in Development (`src/ServiceDefaults/Extensions.cs:114`), and the default TCP probes are right.
  - **`Microsoft.App/containerApps@2024-03-01` `web`**: `ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }`, the same scale, `cpu: json('0.25')`, `memory: '0.5Gi'`, `dependsOn: [api]` (nginx resolves `api` at start).
  - **`Microsoft.App/jobs@2024-03-01` `migrate` and `seed`**:
    - `configuration: { triggerType: 'Manual', replicaTimeout: 900, replicaRetryLimit: 0, manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }, secrets: [...] }`.
    - The template container uses the API image with `args`, `cpu: json('0.5')` and `memory: '1Gi'`.
  - **Parameters:**
    - `location` (default `resourceGroup().location`), `namePrefix` (default `'ims'`), `apiImage`, `webImage`, `sqlAdminLogin`.
    - `@secure()` `sqlAdminPassword`, `inventoryAppPassword`, `seedPassword`.
  - **Output:** `webUrl = 'https://${web.properties.configuration.ingress.fqdn}'`. Never output a secret.
- **`deploy.sh`** (bash, `#!/usr/bin/env bash`, `set -euo pipefail`, never `set -x`, executable `chmod +x`, run from any folder; resolve `ROOT="$(git rev-parse --show-toplevel)"`):
  1. **Load `.env`.** Load `$ROOT/deploy/azure/.env` with `set -a; . file; set +a`; if it's missing, point to `.env.example`. Require `AZ_RESOURCE_GROUP`, `AZ_LOCATION`, `IMAGE_OWNER`, `SQL_ADMIN_LOGIN`, `SQL_ADMIN_PASSWORD`, `INVENTORY_APP_PASSWORD` and `SEED_PASSWORD`. `IMAGE_TAG` defaults to `$(git rev-parse --short=12 HEAD)`.
  2. **Validate without printing values.**
     - `SQL_ADMIN_PASSWORD` and `INVENTORY_APP_PASSWORD` match `^[A-Za-z0-9]{24,128}$` and contain an upper, a lower and a digit. They sit inside connection strings, so no `;` or `=`.
     - `SEED_PASSWORD` has an upper, a lower, a digit and a symbol, and is at least 12 characters (Identity, plus the e2e suite).
     - `SQL_ADMIN_LOGIN` isn't `sa`/`admin`/`administrator`/`root`/`guest`/`public` (Azure SQL reserves them).
     - `SQL_ADMIN_PASSWORD` doesn't contain the login name.
     - Fail with a message naming the key only.
  3. **Check prerequisites.** `az account show` (else: "run `az login`"), `docker info`.
  4. **Images**, unless `--skip-build` is passed:
     - `DOTNET_SYSTEM_NET_DISABLEIPV6=1 dotnet publish "$ROOT/src/Api/Api.csproj" -c Release -t:PublishContainer -p:ContainerRegistry=ghcr.io -p:ContainerRepository="$IMAGE_OWNER/inventory-api" -p:ContainerImageTags="$IMAGE_TAG"`. This pushes with Docker's credential store; the user runs `docker login ghcr.io` first, as the README says.
     - If the SDK can't push, fall back: drop `ContainerRegistry`, then `docker tag` and `docker push`.
     - `docker build -t "ghcr.io/$IMAGE_OWNER/inventory-web:$IMAGE_TAG" "$ROOT/web" && docker push "ghcr.io/$IMAGE_OWNER/inventory-web:$IMAGE_TAG"`.
  5. **Deploy.**
     - `az group create -n "$AZ_RESOURCE_GROUP" -l "$AZ_LOCATION" -o none`.
     - Write the parameters to `params="$(mktemp)"`, then `chmod 600`, then `trap 'rm -f "$params"' EXIT`. Build it with `jq -n --arg …` if `jq` exists, else a heredoc. Use ARM's `{"$schema":…,"contentVersion":"1.0.0.0","parameters":{"x":{"value":…}}}` shape.
     - `az deployment group create -g "$AZ_RESOURCE_GROUP" -f "$ROOT/deploy/azure/main.bicep" -p @"$params" --query properties.outputs.webUrl.value -o tsv`. Secrets never go on the command line.
  6. **Jobs**, as `run_job migrate`, then `run_job seed`:
     - `exec="$(az containerapp job start -g … -n "$1" --query name -o tsv)"`.
     - Poll `az containerapp job execution show -g … -n "$1" --job-execution-name "$exec" --query properties.status -o tsv` every 10 s for up to 15 min, until `Succeeded`.
     - On `Failed` or a timeout, print the `az containerapp job logs show …` hint and exit 1.
  7. **Warm up.** Run `curl -s -o /dev/null -w '%{http_code}' "$WEB_URL/api/auth/me"` until it returns `401`, retrying every 10 s for up to 5 min (cold api plus a paused database). Then print `WEB_URL=$WEB_URL`.
  8. **Rerun safety.** Running the script twice is safe: Bicep is idempotent, `migrate` is idempotent, and `seed` is a replay (spec §13, `Seed_twice_changes_nothing`).
- **`.env.example`:** comment lines then `KEY=`. Write it in the style of `deploy/.env.example`.
  - The header says: copy to `deploy/azure/.env`, which is git-ignored through `.gitignore`'s `.env` rule, and see README `## Azure`.
  - `AZ_RESOURCE_GROUP=ims-demo`, `AZ_LOCATION=southeastasia`, `IMAGE_OWNER=` (your GitHub user or org, lower-case), `IMAGE_TAG=` (blank means the git short SHA).
  - `SQL_ADMIN_LOGIN=ims_admin`, plus `SQL_ADMIN_PASSWORD=`, `INVENTORY_APP_PASSWORD=` and `SEED_PASSWORD=`.
  - Each password gets its rule and a generator: `echo "App$(openssl rand -hex 16)Z9"` for both letters-and-digits passwords, and `echo "Seed!$(openssl rand -hex 12)A9"` for the seed.
- **Secret hygiene:** no real value in any tracked file. `deploy/azure/.env` must show as ignored.

## Do not
- Touch files outside the `files` list. In particular, leave `src/**`, `web/**`, `deploy/docker-compose.yaml` and `deploy/.env.example` alone.
- Run `az deployment`, `az group create`, `docker push` or the script itself. Prompt 05 deploys, with the user's go-ahead.
- Add Static Web Apps, ACR, Key Vault, a VNet or private endpoints, Entra-only SQL, HTTP probes, or more than 1 max replica.
- Read or print any `.env` value.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
az bicep build --file deploy/azure/main.bicep --stdout > /dev/null   # no errors, no warnings
bash -n deploy/azure/deploy.sh && test -x deploy/azure/deploy.sh
git check-ignore -q deploy/azure/.env && echo ignored
grep -nE 'secretRef|@secure' deploy/azure/main.bicep | wc -l             # secrets referenced, not inlined
grep -n 'ConnectionStrings__inventory' deploy/azure/main.bicep            # 3 hits; only migrate's references the admin secret
dotnet tool restore && dotnet aspire publish --apphost src/AppHost/AppHost.csproj --output-path deploy && git status --porcelain deploy/   # deploy/azure files only; compose output unchanged
```
- Report the resource list, and which secret each app or job receives (by name, never by value).
