---
type: prompt
plan: "[[2026-11-05-s4-master-data]]"
id: 02
unit: IMS-5
status: done
model: opus
effort: medium
depends_on: []
parallel_group: B
files:
  create:
    - src/Shared/Kernel/Tenancy/RoleNames.cs
    - src/Shared/Kernel/Hosting/SeedTenants.cs
  modify:
    - src/Shared/Kernel/Problems/ProblemTypes.cs
    - src/Modules/Platform/Platform/Identity/Roles.cs
    - src/Modules/Platform/Platform/Seeding/PlatformSeeder.cs
    - web/src/app/core/problems/problem-messages.ts
  delete: []
---
# 02 · Shared names: roles, seed tenants, problem slugs

## Task
Give the Inventory module one source for the role names and seed tenant ids, without referencing Platform. Add the two problem slugs S4 introduces, in Kernel and in the Angular map, together.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-5`; don't switch it. `Kernel` must not depend on EF Core, ASP.NET Core, SqlClient or `System.Data` (T12 `Kernel_has_no_infrastructure_dependencies`). Plain constants only.

**1. `src/Shared/Kernel/Tenancy/RoleNames.cs`** (new). Copy the doc style of `Kernel/Tenancy/TenantClaims.cs`:
```csharp
namespace Kernel.Tenancy;

/// <summary>The Identity role names every module authorizes against (spec §8). Platform's <c>Roles</c> mirror these.</summary>
public static class RoleNames
{
    public const string Admin = "Admin";

    public const string Staff = "Staff";
}
```
It is named `RoleNames`, not `Roles`: Platform files import both `Kernel.Tenancy` and `Platform.Identity` (e.g. `Platform/Tenants/TenantProvisioner.cs:1,9`), so a second `Roles` would be ambiguous.

**2. `src/Modules/Platform/Platform/Identity/Roles.cs`.** Keep the class, the namespace and the summary. Alias the values:
```csharp
using Kernel.Tenancy;

namespace Platform.Identity;

/// <summary>The Identity role names (spec §8), from <see cref="RoleNames"/>.</summary>
internal static class Roles
{
    public const string Admin = RoleNames.Admin;

    public const string Staff = RoleNames.Staff;
}
```
The callers are `TenantProvisioner.cs:52`, `PlatformSeeder.cs:30-33`, and tests in `tests/**` that use `Platform.Identity.Roles`. They stay unchanged.

**3. `src/Shared/Kernel/Hosting/SeedTenants.cs`** (new):
```csharp
namespace Kernel.Hosting;

/// <summary>The fixed tenant ids the seed uses, so a second run finds what the first created (spec §3).</summary>
public static class SeedTenants
{
    /// <summary>The retail demo tenant.</summary>
    public static readonly Guid Demo = new("00000000-0000-0000-0000-00000000d001");

    /// <summary>The second tenant, for isolation checks.</summary>
    public static readonly Guid Isolation = new("00000000-0000-0000-0000-00000000d002");
}
```

**4. `src/Modules/Platform/Platform/Seeding/PlatformSeeder.cs`** (lines 13-15). Keep both public fields; tests use them (`tests/Platform.IntegrationTests/SetupTests.cs:154,169`). Set them from `SeedTenants`:
```csharp
public static readonly Guid DemoTenantId = SeedTenants.Demo;

public static readonly Guid IsolationTenantId = SeedTenants.Isolation;
```
`Kernel.Hosting` is already imported (line 1). Update the class summary to: `Two tenants with one Admin and one Staff each. Inventory's own seeder adds branches and items.`

**5. `src/Shared/Kernel/Problems/ProblemTypes.cs`.** After `InvalidCredentials` (line 13), add these in the same doc style:
```csharp
/// <summary>The resource doesn't exist, or belongs to another tenant (spec §8: never 403).</summary>
public const string NotFound = "not-found";

/// <summary>The row changed since the client read it (a stale <c>version</c>), or retries ran out.</summary>
public const string ConcurrencyConflict = "concurrency-conflict";
```

**6. `web/src/app/core/problems/problem-messages.ts`.** Add two entries to `PROBLEM_MESSAGES` (lines 2-6), after `'invalid-credentials'`. The slugs must equal the Kernel constants (CLAUDE.md Hidden coupling):
```ts
'not-found': "That record doesn't exist, or you can't see it.",
'concurrency-conflict': 'Someone else changed this record. Reload it and try again.',
```
Keep Prettier's formatting (`web/.prettierrc`): run `npx prettier --write src/app/core/problems/problem-messages.ts` from `web/`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Rename or delete `Platform.Identity.Roles` or the `PlatformSeeder` fields, or change any Guid or role string value.
- Add `stock-not-zero` or any other slug (S5a adds it with its first use).
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `grep -rn '= "Admin"\|= "Staff"' src --include='*.cs'` shows only `src/Shared/Kernel/Tenancy/RoleNames.cs`.
- `grep -n 'd001\|d002' -r src --include='*.cs'` shows only `src/Shared/Kernel/Hosting/SeedTenants.cs`.
