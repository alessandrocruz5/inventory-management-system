---
type: prompt
plan: "[[2026-11-17-s13-compose-output]]"
id: 01
unit: IMS-17
status: todo
model: sonnet
effort: medium
depends_on: []
parallel_group: A
files:
  create:
    - .config/dotnet-tools.json
    - web/.nvmrc
  modify:
    - src/AppHost/AppHost.csproj
    - src/AppHost/AppHost.cs
    - CLAUDE.md
  delete: []
---
# 01 · Compose environment, static web publish, CLI tool

## Task
Make the AppHost publishable to Docker Compose, while leaving `dotnet run` unchanged:
1. Add the `Aspire.Hosting.Docker` package and declare a compose environment.
2. Make `web` publish as a YARP static website that proxies `/api` to `api`.
3. Pin the Aspire CLI as a repo-local dotnet tool, and pin Node 26 for the generated web image.
4. Record the approved package, the tool and the new coupling in CLAUDE.md.

Don't run `aspire publish` yet; prompt 02 does that.

## Context
- **Repo and branch:** the repo root is `git rev-parse --show-toplevel`. The branch must be `IMS-17`; stop if `git branch --show-current` says otherwise. Restore on this machine needs `export DOTNET_SYSTEM_NET_DISABLEIPV6=1`.
- **Spec §12 S13:** *"`aspire publish` to Docker Compose, committed under `deploy/`, verified running without Aspire."*
- **User decisions** (plan `2026-11-17-s13-compose-output`):
  - D1: `Aspire.Hosting.Docker` 13.6.5, plus `aspire.cli` 13.6.5 as a local tool.
  - D2: `PublishAsStaticWebsite`.
  - D5: `web/.nvmrc` holds `26`.
  - D8: record the drift coupling.
- **`src/AppHost/AppHost.csproj`** today: SDK `Aspire.AppHost.Sdk/13.6.5`, `<NoWarn>$(NoWarn);ASPIRE010</NoWarn>`. The package references are `Aspire.Hosting.JavaScript` and `Aspire.Hosting.SqlServer` (both 13.6.5) and `System.Security.Cryptography.Pkcs` 10.0.14.
  - Add `<PackageReference Include="Aspire.Hosting.Docker" Version="13.6.5" />` as the first package reference, before `Aspire.Hosting.JavaScript` (alphabetical order).
  - Every Aspire package stays on the same version.
- **`src/AppHost/AppHost.cs`** today (49 lines):
  - Line 1: `var builder = DistributedApplication.CreateBuilder(args);`.
  - Lines 3-5: `sql`, on `2025-latest` with a data volume.
  - Lines 9-14: the parameters `inventory-app-password` and `seed-password`.
  - Lines 16-17: the `inventory-app` connection string.
  - Lines 19-34: `migrate` (`WithReference(db)`, the admin) and `seed` (`inventory_app`).
  - Lines 36-39: `api`.
  - Lines 41-44: `web`:
    ```csharp
    builder.AddJavaScriptApp("web", "../../web", "start")
        .WithHttpEndpoint(port: 4200, env: "PORT")
        .WithReference(api)
        .WaitFor(api);
    ```
  - Make exactly two changes:
    1. After line 1, add a blank line and:
       ```csharp
       // `aspire publish` target (S13): writes deploy/docker-compose.yaml. Ignored by `dotnet run`.
       builder.AddDockerComposeEnvironment("compose");
       ```
    2. Append to the `web` chain (before the `;`):
       ```csharp
           // Publish only: a YARP image serves the production build and proxies /api, so the browser keeps one origin (spec §8).
           .PublishAsStaticWebsite("/api", api, o =>
           {
               o.OutputPath = "dist/web/browser"; // angular.json has no outputPath; @angular/build writes dist/<project>/browser
               o.StripPrefix = false;             // every API route is under /api (src/Api/Program.cs)
           });
       ```
  - Change nothing else: names, ports, parameters and `WaitFor`/`WaitForCompletion` are coupled to the e2e suite and CI (port 4200, `seed-password`).
- **API facts** (from the package XML docs, `Aspire.Hosting.JavaScript` 13.6.5):
  - The `PublishAsStaticWebsite<T>(IResourceBuilder<T>, string apiPath, IResourceBuilder<IResourceWithServiceDiscovery> apiTarget, Action<PublishAsStaticWebsiteOptions>?)` overload exists.
  - `PublishAsStaticWebsiteOptions` has `OutputPath`, `StripPrefix` and `TargetEndpointName`.
  - If passing `api` (an `IResourceBuilder<ProjectResource>`) doesn't compile against `IResourceBuilder<IResourceWithServiceDiscovery>`, report the exact compiler error. Don't hand-roll a wrapper.
  - `AddDockerComposeEnvironment(string name)` lives in `Aspire.Hosting.Docker`, namespace `Aspire.Hosting`. AppHost files use implicit usings, so you shouldn't need a `using`.
- **TreatWarningsAsErrors is on** (`Directory.Build.props`). The JavaScript package carries the experimental id `ASPIREJAVASCRIPT001`, and the Docker package may carry a compute or pipeline id.
  - If `dotnet build -warnaserror` fails with an `ASPIRE…` experimental diagnostic from these calls, add **exactly that id** to the existing `<NoWarn>` (`$(NoWarn);ASPIRE010;<ID>`).
  - Add an XML comment above the property group's `NoWarn` line naming why: `<!-- ASPIRE010: … existing; <ID>: experimental API used for the S13 compose publish -->`. Keep any existing wording.
  - Don't suppress anything else, and don't use `#pragma`.
- **Tool manifest:** run `dotnet new tool-manifest` at the repo root (it creates `.config/dotnet-tools.json`), then `dotnet tool install aspire.cli --version 13.6.5`.
  - Check that the manifest lists `"aspire.cli"` at `"13.6.5"` with the command `aspire`.
  - If the package id or command name differs at that version, report it and use what `dotnet tool install` resolves.
  - Check `dotnet aspire --version` prints 13.6.5. If the CLI asks to opt out of telemetry or to update, decline. Don't change any global config.
- **`web/.nvmrc`:** a single line, `26` (CI's `node-version: '26'`, README "Node 26"). The generated web Dockerfile reads the Node version from the project's config files.
- **`CLAUDE.md`:**
  - Line 20 (`**Allowed packages.** …`): insert `` `Aspire.Hosting.Docker` (S13 compose publish), `` right after `` `Aspire.Hosting.SqlServer`, ``.
  - In the same paragraph, before `; npm:`, add ``, and the dotnet tool `aspire.cli` (pinned in `.config/dotnet-tools.json` at the Aspire package version)``.
  - Under `## Hidden coupling` (line 77), after the `GenerateParameterDefault` bullet (line 84), add:
    `` - `deploy/` is `aspire publish` output of `src/AppHost/AppHost.cs` (S13): a change to the AppHost's resources, parameters, ports or Aspire version re-runs the publish in the same PR. Compose takes passwords from `deploy/.env`, not `GenerateParameterDefault`, so the README's password rules apply there. ``
  - Touch no other line.
- **Review constraints that apply:**
  - No package outside the allowed list without asking (D1 is the approval).
  - The API connects as `inventory_app`, never as the server admin, so leave every `WithReference` as is.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Run `aspire publish`, `aspire deploy` or `docker compose` (prompt 02 does).
- Add `WithExternalHttpEndpoints`, `PublishAsDockerComposeService`, `WithDashboard` or any other call beyond the two above (prompt 02 owns those contingencies).
- Add any other package or tool, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
(cd web && npm run lint && npm run build && npm test -- --watch=false)
bash .github/scripts/check-banned-packages.sh        # "No banned packages (…)"
dotnet tool restore && dotnet aspire --version       # 13.6.5
grep -n 'Aspire.Hosting.Docker' src/AppHost/AppHost.csproj CLAUDE.md
# R6: run mode unchanged. Nothing may already listen on :4200; the suite starts the AppHost itself.
export E2E_PASSWORD="$(dotnet user-secrets list --project src/AppHost | sed -n 's/^Parameters:seed-password = //p')"   # never echo it
(cd tests/e2e && npm ci && npx playwright test)      # all specs pass
```
- `git status --porcelain` lists only the five files above.
- Report the `NoWarn` id you added, or "none".
