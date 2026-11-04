---
type: prompt
plan: "[[2026-11-04-s2-platform]]"
id: 07
unit: IMS-3
status: todo
model: sonnet
effort: high
depends_on: [06]
parallel_group: G
files:
  create:
    - web/proxy.conf.js
  modify:
    - src/AppHost/AppHost.cs
    - web/angular.json
  delete: []
---
# 07 · AppHost principals, ordering and dev proxy

## Task
Rewire the AppHost:
- `migrate` gets the server admin and runs first, then `seed` and `api` run as `inventory_app`.
- Both passwords are generated Aspire parameters.
- The Angular dev server proxies `/api` to the api.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. Aspire 13.6.4 (`src/AppHost/AppHost.csproj`: `Aspire.AppHost.Sdk/13.6.4`, `Aspire.Hosting.SqlServer`, `Aspire.Hosting.JavaScript`). Current `src/AppHost/AppHost.cs`:
```csharp
var builder = DistributedApplication.CreateBuilder(args);
var sql = builder.AddSqlServer("sql").WithImageTag("2025-latest").WithDataVolume();
var db = sql.AddDatabase("inventory");
var api = builder.AddProject<Projects.Api>("api").WithReference(db).WaitFor(db).WithHttpHealthCheck("/health");
builder.AddJavaScriptApp("web", "../../web", "start").WithHttpEndpoint(port: 4200, env: "PORT").WithReference(api).WaitFor(api);
builder.Build().Run();
```
**`api.WithReference(db)` hands the API `ConnectionStrings__inventory` as `sa`.** That violates the Review constraint "The API connects as inventory_app, never as the server admin". Remove it.

Every process reads one name, `ConnectionStrings:inventory`; the AppHost decides which principal it carries. `Api migrate` (prompt 04) needs `ConnectionStrings:inventory` (admin) and `InventoryApp:Password`. `Api seed` (prompt 06) needs `ConnectionStrings:inventory` (`inventory_app`) and `Seed:Password`.

Target shape (verify each API against the 13.6.4 package; these members exist in its XML docs: `AddParameter`, `GenerateParameterDefault`, `AddConnectionString(string, ReferenceExpression)`, `ReferenceExpression.Create`, `SqlServerServerResource.Host`/`.Port`, `WaitForCompletion`, `WithArgs`):
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql").WithImageTag("2025-latest").WithDataVolume();
var db = sql.AddDatabase("inventory");

var appPassword = builder.AddParameter("inventory-app-password",
    new GenerateParameterDefault { MinLength = 24, Special = false, MinLower = 1, MinUpper = 1, MinNumeric = 1 },
    secret: true, persist: true);
var seedPassword = builder.AddParameter("seed-password",
    new GenerateParameterDefault { MinLength = 16, MinLower = 1, MinUpper = 1, MinNumeric = 1, MinSpecial = 1 },
    secret: true, persist: true);

var appConnection = builder.AddConnectionString("inventory-app", ReferenceExpression.Create(
    $"Server={sql.Resource.Host},{sql.Resource.Port};Database=inventory;User ID=inventory_app;Password={appPassword};TrustServerCertificate=true"));

var migrate = builder.AddProject<Projects.Api>("migrate", launchProfileName: null)
    .WithArgs("migrate")
    .WithReference(db)                                   // server admin: migrate only
    .WithEnvironment("InventoryApp__Password", appPassword)
    .WaitFor(db);

var seed = builder.AddProject<Projects.Api>("seed", launchProfileName: null)
    .WithArgs("seed")
    .WithReference(appConnection, "inventory")           // inventory_app
    .WithEnvironment("Seed__Password", seedPassword)
    .WaitForCompletion(migrate);

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(appConnection, "inventory")           // inventory_app, never db
    .WaitForCompletion(seed)
    .WithHttpHealthCheck("/health");

builder.AddJavaScriptApp("web", "../../web", "start")
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
```
Notes:
- `Special = false` on the app password keeps it free of quote and semicolon characters, so the connection string never needs escaping.
- The `Min*` settings are required. They default to 0, so without them a generated value can miss a character class.
  - `inventory-app-password` must have upper, lower and digit to pass `CHECK_POLICY = ON` (3 of 4 sets).
  - `seed-password` must have upper, lower, digit and symbol to pass Identity's default password policy. A miss would be persisted to user-secrets and fail `seed` on every start.
  - Keep both lines exactly as written.
- If `AddProject<Projects.Api>` refuses two more resources from the same project, or `WithReference(appConnection, "inventory")` doesn't set `ConnectionStrings__inventory`, use the closest 13.6.4 equivalent (for example `.WithEnvironment("ConnectionStrings__inventory", appConnection)`) and report the change.
- `migrate` and `seed` must **not** get an HTTP health check. They exit, and `WaitForCompletion` needs exit code 0.
- **Port clash:** by default each `AddProject<Projects.Api>` takes its endpoints from the Api launch profile, which is fixed at `http://localhost:5062`. Three resources would collide on it. Create `migrate` and `seed` with `builder.AddProject<Projects.Api>("migrate", launchProfileName: null)` (same for `seed`), so they get no endpoints. They return before `app.Run()` and never bind.
  - Without a launch profile, `ASPNETCORE_ENVIRONMENT` is unset (Production). That's fine for the commands, which don't branch on it.
  - If the 13.6.4 overload differs, find the equivalent "no launch profile" option and report it.

**Dev proxy** (TASKS.md Downstream: `/api` → `services__api__http__0`; spec §8 `:478`: one origin, so the strict cookie works):
- `web/proxy.conf.js`:
  ```js
  // Aspire injects the api's URL; the browser sees one origin, so SameSite=Strict cookies work (spec §8).
  const target = process.env['services__api__http__0'] ?? 'http://localhost:5062';
  module.exports = { '/api': { target, secure: false, changeOrigin: true } };
  ```
  Check the current Angular dev-server proxy format first (Angular 22, `@angular/build:dev-server`). If it wants ESM (`export default`) or the `.mjs` extension, use that instead.
- `web/angular.json`: in `projects.web.architect.serve`, add `"options": { "proxyConfig": "proxy.conf.js" }` (keep `configurations` and `defaultConfiguration`). If lint picks up `proxy.conf.js`, it won't: `lintFilePatterns` is `src/**`.

Machine notes (S1 Log): `export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"`, a trusted dev cert, the sql image pre-pulled, and `DOTNET_SYSTEM_NET_DISABLEIPV6=1`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Pass `db` (the admin connection) to `seed` or `api`.
- Commit parameter values. They live in the AppHost's user-secrets (`persist: true`).
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1 SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false && cd ..
grep -c 'WithReference(db)' src/AppHost/AppHost.cs     # 1
dotnet run --project src/AppHost &                      # wait for the dashboard URL
#   dashboard: migrate Finished (exit 0) → seed Finished (exit 0) → api Healthy → web Running
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:4200/api/auth/me    # 401 via the proxy
curl -s -o /dev/null -w '%{http_code}\n' -H 'Content-Type: application/json' -d '{"email":"nobody@x.test","password":"x"}' http://localhost:4200/api/auth/login   # 401
#   dashboard → api → Environment: ConnectionStrings__inventory contains "User ID=inventory_app" (never sa)
kill %1
dotnet run --project src/AppHost &   # second start: migrate and seed exit 0 again; api Healthy
kill %1
```
