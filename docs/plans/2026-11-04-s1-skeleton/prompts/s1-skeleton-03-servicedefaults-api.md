---
type: prompt
plan: "[[2026-11-04-s1-skeleton]]"
id: 03
unit: IMS-2
status: done
model: sonnet
effort: medium
depends_on: [02]
parallel_group: C
files:
  create:
    - src/ServiceDefaults/ServiceDefaults.csproj
    - src/ServiceDefaults/Extensions.cs
    - src/Api/Api.csproj
    - src/Api/Program.cs
    - src/Api/appsettings.json
    - src/Api/appsettings.Development.json
    - src/Api/Properties/launchSettings.json
  modify:
    - InventorySystem.slnx
  delete: []
---
# 03 · ServiceDefaults and Api host

## Task
Create the Aspire ServiceDefaults project and the minimal-API `Api` host. The host wires ServiceDefaults, the ProblemDetails baseline, dev-only OpenAPI + Scalar, and both modules' registration methods under `/api`. Add no endpoints of your own.

## Context
- **Repo root:** `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-2`; do not switch. .NET SDK 10.0.112; `global.json` pins it and sets the MTP test runner.
- **Already built:**
  - `Directory.Build.props` (Nullable, TreatWarningsAsErrors, LangVersion latest, ImplicitUsings) and `InventorySystem.slnx`.
  - Modules from prompt 01:
    - `src/Modules/Inventory/Inventory/Inventory.csproj`, which exposes `Inventory.InventoryModuleExtensions.AddInventoryModule(this IHostApplicationBuilder)` and `MapInventoryEndpoints(this IEndpointRouteBuilder)`.
    - `src/Modules/Platform/Platform/Platform.csproj`, which exposes `Platform.PlatformModuleExtensions.AddPlatformModule` and `MapPlatformEndpoints` in the same shape.
  - `tests/Architecture.Tests` (T12, from prompt 02). Its `ProjectReferenceTests` allow only `src/Api/Api.csproj` and `tests/**` to reference module implementations, so Api must live at exactly that path.
- **Spec:**
  - §2 Stack: minimal APIs, endpoint groups per module, built-in OpenAPI + Scalar UI (no Swashbuckle).
  - CLAUDE.md conventions: errors are RFC 9457 ProblemDetails.
  - §8: errors outside Development return ProblemDetails with no stack trace.
  - All routes sit under `/api` (§9).
- **Allowed packages used here:** `Microsoft.AspNetCore.OpenApi` and `Scalar.AspNetCore`, plus whatever the official `aspire-servicedefaults` template adds. Banned: MediatR, AutoMapper, MassTransit, Swashbuckle (not allowed), FluentAssertions 8+.

### Steps
1. Install the Aspire templates if `dotnet new list aspire-servicedefaults` finds nothing: `dotnet new install Aspire.ProjectTemplates::13.6.4`. This is a machine-level template pack, not a repo dependency.
2. `dotnet new aspire-servicedefaults -n ServiceDefaults -o src/ServiceDefaults -f net10.0`.
   - Keep the template's package references and versions, and its `IsAspireSharedProject`.
   - Delete only the csproj properties `Directory.Build.props` already sets (`Nullable`, `ImplicitUsings`).
   - Keep `Extensions.cs` as generated. Its `MapDefaultEndpoints` maps `/health` and `/alive` in Development only; leave that as is.
3. `dotnet new web -n Api -o src/Api -f net10.0` (ASP.NET Core Empty). Then:
   - `dotnet add src/Api reference src/ServiceDefaults src/Modules/Inventory/Inventory src/Modules/Platform/Platform`.
   - `dotnet add src/Api package Microsoft.AspNetCore.OpenApi`, using the newest `10.0.x` that restores (10.0.14 at planning time).
   - `dotnet add src/Api package Scalar.AspNetCore --version 2.18.7`.
   - Remove csproj properties duplicated by `Directory.Build.props`.
4. Replace `src/Api/Program.cs` with:
   ```csharp
   using Inventory;
   using Platform;
   using Scalar.AspNetCore;

   var builder = WebApplication.CreateBuilder(args);

   builder.AddServiceDefaults();
   builder.Services.AddProblemDetails();
   builder.Services.AddOpenApi();

   builder.AddPlatformModule();
   builder.AddInventoryModule();

   var app = builder.Build();

   app.UseExceptionHandler();
   app.UseStatusCodePages();

   if (app.Environment.IsDevelopment())
   {
       app.MapOpenApi();
       app.MapScalarApiReference();
   }

   app.MapDefaultEndpoints();

   var api = app.MapGroup("/api");
   api.MapPlatformEndpoints();
   api.MapInventoryEndpoints();

   app.Run();
   ```
   Don't add `public partial class Program;`. ASP.NET Core 10 generates it for `WebApplicationFactory<Program>`, and S2 confirms that when it first uses it. If the build reports an analyzer warning on any line above (TreatWarningsAsErrors is on), fix the cause; don't suppress it.
5. Keep the generated `appsettings*.json` and `Properties/launchSettings.json`.
6. `dotnet sln InventorySystem.slnx add src/ServiceDefaults/ServiceDefaults.csproj src/Api/Api.csproj`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here. That includes a DbContext, auth, CORS, a dev proxy and sample endpoints; S2 owns them.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                                   # T12 still green; Api referencing both modules is allowed
dotnet run --project src/Api --launch-profile http &   # note the "Now listening on" URL as $API
curl -fsS "$API/health"                       # 200 Healthy
curl -fsS "$API/alive"                        # 200 Healthy
curl -fsS "$API/openapi/v1.json" | head -c 200
curl -s -o /dev/null -w '%{http_code}\n' "$API/scalar"        # 200 (or a 30x to the Scalar page)
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' "$API/api/nope"   # 404 application/problem+json
kill %1
git status --porcelain                         # only the files above plus bookkeeping
```
