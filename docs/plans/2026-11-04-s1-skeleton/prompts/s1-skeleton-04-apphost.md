---
type: prompt
plan: "[[2026-11-04-s1-skeleton]]"
id: 04
unit: IMS-2
status: todo
model: sonnet
effort: medium
depends_on: [03]
parallel_group: D
files:
  create:
    - src/AppHost/AppHost.csproj
    - src/AppHost/AppHost.cs
    - src/AppHost/appsettings.json
    - src/AppHost/appsettings.Development.json
    - src/AppHost/Properties/launchSettings.json
    - src/AppHost/aspire.config.json
  modify:
    - InventorySystem.slnx
  delete: []
---
# 04 · Aspire AppHost with sql and api

## Task
Create the Aspire AppHost. It orchestrates a SQL Server 2025 container with a data volume and a database named `inventory`, plus the `api` project with an HTTP health check. Make it run with only the .NET SDK and Docker installed, with no Aspire CLI.

## Context
- **Repo root:** `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-2`; do not switch. .NET SDK 10.0.112 and Docker 29.8 are installed; the Aspire CLI is **not**, and that's deliberate.
- **Already built:** `src/Api/Api.csproj` (minimal-API host with ServiceDefaults; `/health` and `/alive` in Development), `src/ServiceDefaults`, the module projects, `tests/Architecture.Tests` (T12) and `InventorySystem.slnx`. `Directory.Build.props` sets Nullable, TreatWarningsAsErrors, LangVersion latest and ImplicitUsings.
- **Spec:**
  - §12 S1: "Aspire AppHost (sql on the 2025 image with a data volume, api, web)". The `web` resource is prompt 05.
  - §2: "Aspire's SQL resource defaults to the 2022 image: set `.WithImageTag("2025-latest")`".
  - §13: `dotnet run --project src/AppHost` on a fresh clone starts everything healthy.
  - §8: connection strings and passwords come from Aspire parameters and user-secrets; nothing secret is committed.
- **Allowed packages:** `Aspire.Hosting.SqlServer`, plus what the official `aspire-apphost` template adds.
- **The CLI-bundle gotcha (verified in `Aspire.AppHost.Sdk` 13.6.4 `Sdk/Sdk.targets:25,76`):** the `aspire-apphost` template writes `<AspireUseCliBundle>true</AspireUseCliBundle>`. That skips the DCP and Dashboard NuGet references, so without the Aspire CLI the AppHost has no orchestrator. **Delete that property.** The SDK default `false` pulls DCP and Dashboard from NuGet.

### Steps
1. Install the Aspire templates if they're missing: `dotnet new install Aspire.ProjectTemplates::13.6.4`.
2. `dotnet new aspire-apphost -n AppHost -o src/AppHost -f net10.0`.
   - Keep `Sdk="Aspire.AppHost.Sdk/13.6.4"` and the generated `UserSecretsId`.
   - Delete `AspireUseCliBundle`, plus the properties `Directory.Build.props` already sets.
   - The template also writes `aspire.config.json` (`{"appHost":{"path":"AppHost.csproj"}}`), which the Aspire CLI reads. Keep it as generated, and check that its path names `AppHost.csproj`.
   - Keep the generated `Properties/launchSettings.json` profile order (`https` first). §13's `dotnet run --project src/AppHost` uses that first profile.
3. `dotnet add src/AppHost package Aspire.Hosting.SqlServer --version 13.6.4`, then `dotnet add src/AppHost reference src/Api/Api.csproj`. The SDK marks it as an Aspire project resource and generates `Projects.Api`.
4. `src/AppHost/AppHost.cs`:
   ```csharp
   var builder = DistributedApplication.CreateBuilder(args);

   var sql = builder.AddSqlServer("sql")
       .WithImageTag("2025-latest")
       .WithDataVolume();

   var db = sql.AddDatabase("inventory");

   var api = builder.AddProject<Projects.Api>("api")
       .WithReference(db)
       .WaitFor(db)
       .WithHttpHealthCheck("/health");

   builder.Build().Run();
   ```
   `api` is assigned now because prompt 05 references it for `web`. If that unused local raises a warning (TreatWarningsAsErrors), drop the `var api =` assignment; prompt 05 adds it back.
   - Resource names `sql`, `api` and database `inventory` are a locked decision. S2 reads `ConnectionStrings:inventory`.
   - The Api doesn't consume the database yet, and that's expected.
5. `dotnet sln InventorySystem.slnx add src/AppHost/AppHost.csproj`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here: no `WithLifetime(Persistent)`, no fixed SQL password, no `inventory_app` parameter (S2), no `web` resource (prompt 05).
- Commit a password anywhere. Aspire generates `sql-password` into the AppHost's user-secrets.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
! grep -n 'AspireUseCliBundle' src/AppHost/AppHost.csproj
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                                   # T12 green: AppHost references Api, not a module
which aspire || echo "no aspire CLI (expected)"
dotnet run --project src/AppHost > "${TMPDIR:-/tmp}/apphost.log" 2>&1 &   # §13's exact command (default https profile)
# If it fails on the HTTPS dev certificate, stop and report: don't reorder the profiles or switch to http.
# wait until the log shows the dashboard URL and sql is up (the first run pulls ~1.5 GB)
docker ps --format '{{.Image}} {{.Mounts}}' | grep 'mssql/server:2025-latest'   # image + a named *-sql-data volume
curl -fsS http://localhost:<api http port from src/Api/Properties/launchSettings.json>/health   # Healthy
kill %1
docker volume ls | grep -i sql-data           # volume survives the stop
git status --porcelain                         # only the files above plus bookkeeping
```
- Report the dashboard's resource states: `sql` Running/Healthy and `api` Running/Healthy. If you can't read the dashboard headlessly, report the log lines and the two checks above instead.
