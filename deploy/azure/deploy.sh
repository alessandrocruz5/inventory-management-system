#!/usr/bin/env bash
# Deploys the demo to Azure Container Apps (S14): builds and pushes the images to GHCR, deploys main.bicep,
# runs the migrate and seed jobs, warms up, and prints the URL. Safe to rerun: Bicep and migrate are idempotent,
# and seed is a replay (spec §13). Usage: deploy/azure/deploy.sh [--skip-build]. See README "## Azure".
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
ENV_FILE="$ROOT/deploy/azure/.env"

die() {
  echo "error: $*" >&2
  exit 1
}

skip_build=false
for arg in "$@"; do
  case "$arg" in
    --skip-build) skip_build=true ;;
    -h | --help)
      echo "usage: $0 [--skip-build]"
      exit 0
      ;;
    *) die "unknown argument: $arg" ;;
  esac
done

# 1. Load .env.
[[ -f "$ENV_FILE" ]] || die "$ENV_FILE is missing: copy deploy/azure/.env.example to it and fill it in"
set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a

for key in AZ_RESOURCE_GROUP AZ_LOCATION IMAGE_OWNER SQL_ADMIN_LOGIN SQL_ADMIN_PASSWORD INVENTORY_APP_PASSWORD SEED_PASSWORD; do
  [[ -n "${!key:-}" ]] || die "$key is not set in $ENV_FILE"
done
IMAGE_TAG="${IMAGE_TAG:-$(git -C "$ROOT" rev-parse --short=12 HEAD)}"

# 2. Validate without printing values: every message names the key only.
check_alnum_password() {
  local key="$1" value="${!1}"
  [[ "$value" =~ ^[A-Za-z0-9]{24,128}$ ]] || die "$key must be 24-128 letters and digits (no ';' or '=': it sits inside a connection string)"
  [[ "$value" =~ [A-Z] && "$value" =~ [a-z] && "$value" =~ [0-9] ]] || die "$key needs an upper-case letter, a lower-case letter and a digit"
}
check_alnum_password SQL_ADMIN_PASSWORD
check_alnum_password INVENTORY_APP_PASSWORD

[[ ${#SEED_PASSWORD} -ge 12 ]] || die "SEED_PASSWORD must be at least 12 characters"
[[ "$SEED_PASSWORD" =~ [A-Z] && "$SEED_PASSWORD" =~ [a-z] && "$SEED_PASSWORD" =~ [0-9] && "$SEED_PASSWORD" =~ [^A-Za-z0-9] ]] ||
  die "SEED_PASSWORD needs an upper-case letter, a lower-case letter, a digit and a symbol"

case "${SQL_ADMIN_LOGIN,,}" in
  sa | admin | administrator | root | guest | public) die "SQL_ADMIN_LOGIN is a name Azure SQL reserves" ;;
esac
# SQL's password policy (error 40632) rejects a password containing any part of the user's name: three or more
# consecutive letters or digits between delimiters such as '_'. For inventory_app that rules out "inv", "app", ...
check_not_in_login() {
  local key="$1" login="${2,,}" value="${!1}" part i
  value="${value,,}"
  for part in ${login//[^a-z0-9]/ }; do
    for ((i = 0; i + 3 <= ${#part}; i++)); do
      [[ "$value" != *"${part:i:3}"* ]] || die "$key must not contain three or more consecutive characters of the user name $2"
    done
  done
}
check_not_in_login SQL_ADMIN_PASSWORD "$SQL_ADMIN_LOGIN"
check_not_in_login INVENTORY_APP_PASSWORD inventory_app

# 3. Prerequisites.
command -v az > /dev/null || die "the az CLI is missing: install azure-cli and run 'az bicep install'"
az account show -o none 2> /dev/null || die "not signed in to Azure: run 'az login'"

API_IMAGE="ghcr.io/$IMAGE_OWNER/inventory-api:$IMAGE_TAG"
WEB_IMAGE="ghcr.io/$IMAGE_OWNER/inventory-web:$IMAGE_TAG"

# 4. Images (public GHCR, plan D4). Pushing uses Docker's credential store: run 'docker login ghcr.io' first.
if [[ "$skip_build" == false ]]; then
  docker info > /dev/null 2>&1 || die "docker is not running"

  echo "==> Building and pushing $API_IMAGE"
  if ! DOTNET_SYSTEM_NET_DISABLEIPV6=1 dotnet publish "$ROOT/src/Api/Api.csproj" -c Release -t:PublishContainer \
    -p:ContainerRegistry=ghcr.io -p:ContainerRepository="$IMAGE_OWNER/inventory-api" -p:ContainerImageTags="$IMAGE_TAG"; then
    echo "==> The SDK could not push; building into the local Docker daemon and pushing with docker"
    DOTNET_SYSTEM_NET_DISABLEIPV6=1 dotnet publish "$ROOT/src/Api/Api.csproj" -c Release -t:PublishContainer \
      -p:ContainerRepository="$IMAGE_OWNER/inventory-api" -p:ContainerImageTags="$IMAGE_TAG"
    docker tag "$IMAGE_OWNER/inventory-api:$IMAGE_TAG" "$API_IMAGE"
    docker push "$API_IMAGE"
  fi

  echo "==> Building and pushing $WEB_IMAGE"
  # Two commands, not `a && b`: set -e ignores a failure on the left of &&, so a failed build would deploy anyway.
  docker build -t "$WEB_IMAGE" "$ROOT/web"
  docker push "$WEB_IMAGE"
else
  echo "==> --skip-build: using $API_IMAGE and $WEB_IMAGE"
fi

# 5. Deploy. Secrets go through a private parameters file, never the command line.
echo "==> Ensuring resource group $AZ_RESOURCE_GROUP in $AZ_LOCATION"
az group create -n "$AZ_RESOURCE_GROUP" -l "$AZ_LOCATION" -o none

params="$(mktemp)"
chmod 600 "$params"
trap 'rm -f "$params"' EXIT

if command -v jq > /dev/null; then
  jq -n \
    --arg location "$AZ_LOCATION" \
    --arg apiImage "$API_IMAGE" \
    --arg webImage "$WEB_IMAGE" \
    --arg sqlAdminLogin "$SQL_ADMIN_LOGIN" \
    --arg sqlAdminPassword "$SQL_ADMIN_PASSWORD" \
    --arg inventoryAppPassword "$INVENTORY_APP_PASSWORD" \
    --arg seedPassword "$SEED_PASSWORD" \
    '{
      "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
      contentVersion: "1.0.0.0",
      parameters: {
        location: { value: $location },
        apiImage: { value: $apiImage },
        webImage: { value: $webImage },
        sqlAdminLogin: { value: $sqlAdminLogin },
        sqlAdminPassword: { value: $sqlAdminPassword },
        inventoryAppPassword: { value: $inventoryAppPassword },
        seedPassword: { value: $seedPassword }
      }
    }' > "$params"
else
  # Without jq: escape backslashes and quotes (the other values are validated or come from git).
  json_escape() {
    local s="${1//\\/\\\\}"
    printf '%s' "${s//\"/\\\"}"
  }
  cat > "$params" << JSON
{
  "\$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "location": { "value": "$(json_escape "$AZ_LOCATION")" },
    "apiImage": { "value": "$(json_escape "$API_IMAGE")" },
    "webImage": { "value": "$(json_escape "$WEB_IMAGE")" },
    "sqlAdminLogin": { "value": "$(json_escape "$SQL_ADMIN_LOGIN")" },
    "sqlAdminPassword": { "value": "$(json_escape "$SQL_ADMIN_PASSWORD")" },
    "inventoryAppPassword": { "value": "$(json_escape "$INVENTORY_APP_PASSWORD")" },
    "seedPassword": { "value": "$(json_escape "$SEED_PASSWORD")" }
  }
}
JSON
fi

echo "==> Deploying main.bicep to $AZ_RESOURCE_GROUP"
WEB_URL="$(az deployment group create -g "$AZ_RESOURCE_GROUP" -f "$ROOT/deploy/azure/main.bicep" -p @"$params" \
  --query properties.outputs.webUrl.value -o tsv)"
rm -f "$params"
[[ -n "$WEB_URL" ]] || die "the deployment returned no webUrl"

# 6. Jobs: migrate, then seed; each waits for its execution to finish.
run_job() {
  local job="$1" exec status deadline
  echo "==> Starting job $job"
  exec="$(az containerapp job start -g "$AZ_RESOURCE_GROUP" -n "$job" --query name -o tsv)"
  [[ -n "$exec" ]] || die "job $job returned no execution name"
  deadline=$((SECONDS + 900))
  while :; do
    status="$(az containerapp job execution show -g "$AZ_RESOURCE_GROUP" -n "$job" --job-execution-name "$exec" \
      --query properties.status -o tsv 2> /dev/null || true)"
    case "$status" in
      Succeeded)
        echo "==> Job $job succeeded ($exec)"
        return 0
        ;;
      Failed | Stopped | Degraded)
        echo "error: job $job ended as $status ($exec). Logs:" >&2
        echo "  az containerapp job logs show -g $AZ_RESOURCE_GROUP -n $job --execution $exec --container $job" >&2
        exit 1
        ;;
    esac
    if ((SECONDS >= deadline)); then
      echo "error: job $job did not finish within 15 minutes ($exec, last status: ${status:-unknown}). Logs:" >&2
      echo "  az containerapp job logs show -g $AZ_RESOURCE_GROUP -n $job --execution $exec --container $job" >&2
      exit 1
    fi
    echo "    $job: ${status:-pending}"
    sleep 10
  done
}

run_job migrate
run_job seed

# 7. Warm up: a cold api plus a paused database can take minutes; 401 from /api/auth/me means the whole path is up.
echo "==> Warming up $WEB_URL"
deadline=$((SECONDS + 300))
while :; do
  code="$(curl -4 -s -m 20 -o /dev/null -w '%{http_code}' "$WEB_URL/api/auth/me" || true)"
  [[ "$code" == 401 ]] && break
  ((SECONDS < deadline)) || die "$WEB_URL/api/auth/me did not return 401 within 5 minutes (last: ${code:-none})"
  echo "    /api/auth/me: ${code:-no response}"
  sleep 10
done

echo "WEB_URL=$WEB_URL"
