---
type: prompt
plan: "[[2026-11-17-s14-azure]]"
id: 03
unit: IMS-18
status: todo
model: sonnet
effort: medium
depends_on: []
parallel_group: A
files:
  create: []
  modify:
    - web/nginx.conf.template
    - tests/e2e/playwright.config.ts
  delete: []
---
# 03 · nginx upstream Host and e2e base URL

## Task
1. Make the `web` nginx proxy send the **upstream's** Host header, forward the original host as `X-Forwarded-Host`, and allow 120 s reads. Azure Container Apps routes internal calls by Host, and a cold start can be slow.
2. Let the Playwright suite target a deployed URL through an optional `E2E_BASE_URL`.
3. Prove compose and run mode still pass.

## Context
- **Repo and branch:** the repo root is `git rev-parse --show-toplevel`. Work on the branch you were given (`IMS-18`), and stop if `git branch --show-current` isn't it. Use `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`.
- **Why** (plan `2026-11-17-s14-azure`, D2, D8, D9):
  - On Azure, nginx proxies `/api/` to `http://api`, an internal-ingress Container App. The environment's Envoy picks the target app from the Host header, so forwarding the browser's host (`web`'s FQDN) loops back to `web`.
  - A first request after idle wakes the api and a paused Azure SQL database, which can take ~60 s, and that is nginx's default `proxy_read_timeout`.
- **`web/nginx.conf.template`** today:
  ```nginx
  location /api/ {
      proxy_pass ${services__api__http__0};
      proxy_set_header Host $host;
      proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
      proxy_set_header X-Forwarded-Proto $scheme;
  }
  ```
  - Change `proxy_set_header Host $host;` to `proxy_set_header Host $proxy_host;`.
  - Add `proxy_set_header X-Forwarded-Host $host;` and `proxy_read_timeout 120s;`.
  - Add a one-line comment: the upstream routes by Host (Container Apps), and a cold api plus a paused database can exceed the 60 s default.
  - Leave everything else as it is. The official nginx image renders the template with the container's env (`envsubst` touches only the variables that are defined, so `$proxy_host` and `$host` survive; `$host` already does today).
- **Safe for the API:** `Location` headers are relative (`CreateItem/Handler.cs:53`, `DocumentPoster.cs:157`), `AllowedHosts` is `*` (`src/Api/appsettings.json:8`), and nothing reads `Request.Host`. In compose the API now sees `Host: api:8080`.
- **`tests/e2e/playwright.config.ts`** today: `use.baseURL: 'http://localhost:4200'`, and a `webServer` block that always starts `dotnet run --project ../../src/AppHost` (lines 21-34).
  - Add `const baseURL = process.env['E2E_BASE_URL'];` next to `password`.
  - Set `use.baseURL: baseURL ?? 'http://localhost:4200'`.
  - Use `webServer: baseURL ? undefined : { …unchanged… }`.
  - Add a comment: with `E2E_BASE_URL` set, the suite runs against an already deployed stack (S14).
  - CI never sets it, so CI behaviour is unchanged. The specs (`tests/e2e/specs/**`, `support/app.ts`) use relative URLs; check with `grep -rn 'localhost' tests/e2e/specs tests/e2e/support`. If any spec hard-codes `localhost`, stop and report.
- **Compose check:** `deploy/.env` exists locally and holds secrets, so never print it. `deploy/.env` points `WEB_IMAGE` at `web:latest` (README `## Docker Compose`). Stop any running AppHost first, because both use port 4200.

## Do not
- Touch files outside the `files` list, `src/AppHost/**` or `deploy/**`. If you must, stop and report why.
- Print the contents of `deploy/.env` or any password.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test --max-parallel-test-modules 1
(cd web && npm run lint && npm run build && npm test -- --watch=false)

docker build -t web:latest web
(cd deploy && docker compose up -d && docker compose ps -a)                    # migrate, seed Exited (0); api, web, sql up
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:4200/api/auth/me     # 401
curl -s http://localhost:4200/stock | grep -c '<app-root'                     # >= 1
docker compose -f deploy/docker-compose.yaml exec web sh -c 'grep -E "proxy_host|X-Forwarded-Host|proxy_read_timeout" /etc/nginx/conf.d/default.conf'
(unset CI; export E2E_PASSWORD="$(sed -n 's/^SEED_PASSWORD=//p' deploy/.env)"; cd tests/e2e && npx playwright test)   # 4 passed
(cd deploy && docker compose down)
(unset CI; export E2E_PASSWORD="$(dotnet user-secrets list --project src/AppHost | sed -n 's/^Parameters:seed-password = //p')"; cd tests/e2e && npx playwright test)   # run mode (README "End-to-end tests"): 4 passed
```
- Report both Playwright summaries and the curl codes.
