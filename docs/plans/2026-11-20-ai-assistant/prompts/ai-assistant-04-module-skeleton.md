---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 04
unit: IMS-34
status: done
model: sonnet
effort: medium
depends_on: [03]
parallel_group: D
files:
  create:
    - src/Modules/Assistant/Assistant/Assistant.csproj
    - src/Modules/Assistant/Assistant/AssistantModuleExtensions.cs
    - src/Modules/Assistant/Assistant/AssistantOptions.cs
  modify:
    - InventorySystem.slnx
    - src/Api/Api.csproj
    - src/Api/Program.cs
    - src/Api/appsettings.Development.json
    - CLAUDE.md
  delete: []
---
# 04 · Assistant module skeleton and wiring

## Task
Create the `Assistant` module project with the two approved packages, its options, and the model client registration. Wire it into the API. It maps no routes yet.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop. IMS-33 (`IInventoryReader`) is merged into the base.
- Restore on this machine needs `DOTNET_SYSTEM_NET_DISABLEIPV6=1` (TASKS.md Downstream IMS-7).
- Module rules (TASKS.md Downstream IMS-2):
  - One public type, `AssistantModuleExtensions`, with exactly `AddAssistantModule` and `MapAssistantEndpoints`. Everything else is `internal`.
  - Only `src/Api` and `tests/**` reference a module implementation.
  - Assistant references `Inventory.Contracts` and `Kernel` only. It never references `Inventory` or `Platform`, and never EF Core, SqlClient or `IInventoryModule` (plan D8; prompt 06 adds the T12 rules).
- Packages (plan D3, approved by the user): `Microsoft.Extensions.AI` **10.11.1** and `Microsoft.Extensions.AI.OpenAI` **10.11.1**. They bring `OpenAI` 2.14.0 transitively. Don't use 11.0.0: it pulls the 11.x `Microsoft.Extensions.*` abstractions into this net10 app, whose Microsoft packages are on 10.0.14.

### Files
1. `src/Modules/Assistant/Assistant/Assistant.csproj`: copy the shape of `src/Modules/Inventory/Inventory/Inventory.csproj`:
   - `FrameworkReference Microsoft.AspNetCore.App`.
   - ProjectReferences `..\..\Inventory\Inventory.Contracts\Inventory.Contracts.csproj` and `..\..\..\Shared\Kernel\Kernel.csproj`.
   - The two PackageReferences.
   - `InternalsVisibleTo Include="Inventory.IntegrationTests"`.
2. `AssistantOptions.cs`: `internal sealed class AssistantOptions` with:
   - `public const string Section = "Assistant";`
   - `internal const string ModelServiceKey = "assistant-model";`
   - Properties `string? Endpoint`, `string? Model`, `string? ApiKey`, `int PermitsPerMinute { get; set; } = 10`, `int TimeoutSeconds { get; set; } = 90`.
   - XML docs: the defaults sit below the nginx 120 s read timeout (`web/nginx.conf.template`); the deployed stack sets none of these, so the assistant is unconfigured there (plan D14). `ApiKey` is never logged or returned.
3. `AssistantModuleExtensions.cs` (`namespace Assistant`):
   - `AddAssistantModule(this IHostApplicationBuilder builder)`:
     - `builder.Services.AddOptions<AssistantOptions>().Bind(builder.Configuration.GetSection(AssistantOptions.Section)).Validate(o => o.PermitsPerMinute is >= 1 and <= 10_000 && o.TimeoutSeconds is >= 1 and <= 110, "Assistant:PermitsPerMinute must be 1..10000 and Assistant:TimeoutSeconds 1..110.").ValidateOnStart();`
     - Read the section once at registration (`builder.Configuration.GetSection(...).Get<AssistantOptions>() ?? new()`), as `AddInventoryModule` reads its connection string at registration (`InventoryModuleExtensions.cs:55`). Tests' `UseSetting` values are visible here.
     - When `Endpoint` and `Model` are both non-blank: require `Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)` with scheme http or https, or throw `InvalidOperationException("Assistant:Endpoint must be an absolute http(s) URL.")`. Then register `builder.Services.AddKeyedSingleton<IChatClient>(AssistantOptions.ModelServiceKey, (_, _) => new OpenAIClient(new ApiKeyCredential(string.IsNullOrWhiteSpace(o.ApiKey) ? "unused" : o.ApiKey), new OpenAIClientOptions { Endpoint = uri }).GetChatClient(o.Model).AsIChatClient());`. Ollama ignores the key, but the SDK requires one.
     - Comment: "No model configured → no keyed client → the assistant answers 503 assistant-not-configured (plan D14)."
     - Check the names against the restored packages (`OpenAI`, `OpenAI.Chat`, `System.ClientModel`, `Microsoft.Extensions.AI`). If one differs, use the package's actual API and note it in your report.
   - `MapAssistantEndpoints(this IEndpointRouteBuilder endpoints)` returns `endpoints` unchanged for now, with an XML doc saying the routes land in prompts 05 and 09 under `/assistant`.
4. `InventorySystem.slnx`: add `<Folder Name="/src/Modules/Assistant/">` with `<Project Path="src/Modules/Assistant/Assistant/Assistant.csproj" />`, after the Inventory folder.
5. `src/Api/Api.csproj`: add `<ProjectReference Include="..\Modules\Assistant\Assistant\Assistant.csproj" />`.
6. `src/Api/Program.cs`: add `using Assistant;`, call `builder.AddAssistantModule();` after `builder.AddInventoryModule();`, and call `api.MapAssistantEndpoints();` after `api.MapInventoryEndpoints();`. Change nothing else.
7. `src/Api/appsettings.Development.json`: add `"Assistant": { "Endpoint": "http://localhost:11434/v1", "Model": "qwen3:30b-a3b" }`. This is local Ollama, $0, and no data leaves the machine (plan D2, D14).
8. `CLAUDE.md`, **Allowed packages** paragraph: after `NSubstitute`, add `` `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.OpenAI` (Sprint 4, approved 2026-11-20; `OpenAI` comes with them) ``. Change nothing else in CLAUDE.md.

## Do not
- Touch the AppHost, `deploy/`, compose or Bicep. The keys stay unset there (plan D14).
- Add any other package, endpoint or config key.
- Touch files outside the `files` list. If you must, stop and report why.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
DOTNET_SYSTEM_NET_DISABLEIPV6=1 dotnet restore
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
bash .github/scripts/check-banned-packages.sh
```
- `dotnet list src/Modules/Assistant/Assistant package` shows the two packages at 10.11.1.
- The build has no NU1605/NU1608 warnings. If restore reports a version conflict, stop and report it; don't paper over it with extra pins.
