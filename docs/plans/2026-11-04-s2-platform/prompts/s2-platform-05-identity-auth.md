---
type: prompt
plan: "[[2026-11-04-s2-platform]]"
id: 05
unit: IMS-3
status: todo
model: opus
effort: high
depends_on: [04]
parallel_group: E
files:
  create:
    - src/Modules/Platform/Platform/Identity/Roles.cs
    - src/Modules/Platform/Platform/Identity/TenantClaimsPrincipalFactory.cs
    - src/Modules/Platform/Platform/Identity/IdentityRegistration.cs
    - src/Modules/Platform/Platform/Identity/AuthEndpoints.cs
  modify:
    - src/Modules/Platform/Platform/PlatformModuleExtensions.cs
    - src/Api/Program.cs
  delete: []
---
# 05 · Identity, cookie and auth endpoints

## Task
Register ASP.NET Core Identity with the cookie scheme, a claims factory that adds `tenant_id`, and hand-written `POST /api/auth/login`, `POST /api/auth/logout` and `GET /api/auth/me`. Wire authentication and authorization into the Api pipeline around the tenant middleware.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. Existing types:
- `Platform.Identity.AppUser : IdentityUser<Guid>` with `Guid TenantId`.
- `Platform.Persistence.PlatformDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>` with `DbSet<Tenant> Tenants`. If prompt 02 moved it to `Platform.Identity`, use that namespace.
- `Kernel.Tenancy.TenantClaims.TenantId = "tenant_id"`, plus `ITenantContext` and `ICurrentUser` (filled by `Api.Tenancy.TenantMiddleware`).
- `Kernel.Problems.ProblemTypes`: `Uri(slug)`, `Validation`, `InvalidCredentials`.
- `src/Api/Program.cs` currently calls `app.UseMiddleware<TenantMiddleware>()` right after `app.UseStatusCodePages()`.

Everything here lives in namespace **`Platform.Identity`**, the only Platform namespace T12 allows to touch `System.Security.Claims` (`tests/Architecture.Tests/ModuleBoundaryTests.cs:77–90`). Every type is `internal`. `PlatformModuleExtensions` keeps exactly `AddPlatformModule` and `MapPlatformEndpoints` public (T12), so its bodies only call into the internal Identity types.

**Spec (§8 `:475–479`, §9 `:530–532`):**
- Cookie: `HttpOnly`, `Secure`, `SameSite=Strict`, 8-hour sliding expiry. Lockout after 5 failed attempts.
- Don't use `MapIdentityApi`, which maps a public `/register`.
- Roles are Identity roles `Admin` and `Staff`.
- Endpoints accept `application/json` only.

**`Roles.cs`:** `internal static class Roles { public const string Admin = "Admin"; public const string Staff = "Staff"; }`

**`TenantClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>`:** override `GenerateClaimsAsync(AppUser user)`. Call `base`, then add `new Claim(TenantClaims.TenantId, user.TenantId.ToString())`. Base already adds `NameIdentifier`, `Name`, `Email` (when `ClaimsIdentity.EmailClaimType` is set, which is the default in .NET 8+; verify and add `ClaimTypes.Email` yourself if it's absent) and the role claims.

**`IdentityRegistration.AddPlatformIdentity(this IHostApplicationBuilder builder)`:**
```csharp
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<PlatformDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<TenantClaimsPrincipalFactory>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
```
`AddRoles<IdentityRole<Guid>>()` must also register `RoleManager`. If the chain above doesn't compile against Identity 10.0.14, adapt it minimally and report what changed.

**`AuthEndpoints.MapAuthEndpoints(this IEndpointRouteBuilder api)`:** maps under `api.MapGroup("/auth")`, so routes are `/api/auth/...` because Api calls `MapPlatformEndpoints` on its `/api` group. Request and response types are `internal sealed record`s in this file.
- `POST /login`, `AllowAnonymous()`, body `LoginRequest(string? Email, string? Password)`, `.Accepts<LoginRequest>("application/json")`:
  - If either field is null or whitespace, return `TypedResults.ValidationProblem(errors, type: ProblemTypes.Uri(ProblemTypes.Validation))` with an `errors` entry per field.
  - Otherwise find the user by email (`UserManager.FindByEmailAsync`).
  - If the user is null, return the **InvalidCredentials** result. Don't call `PasswordSignInAsync`. If you can, spend comparable time, but don't add complexity for it; constant time isn't required in sprint 1.
  - Otherwise `signInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true)`.
  - `Succeeded` → `TypedResults.Ok(MeResponse)`, built the same way as `/me` below, but from `user` and the role from `UserManager.GetRolesAsync`. `ITenantContext` isn't filled on this request.
  - Any other outcome (`IsLockedOut`, `IsNotAllowed`, `RequiresTwoFactor`, failure) → the InvalidCredentials result.
  - **InvalidCredentials result**, identical for every failure: `TypedResults.Problem(statusCode: 401, title: "Sign-in failed", detail: "Email or password is incorrect. After 5 failed attempts the account is locked for 15 minutes.", type: ProblemTypes.Uri(ProblemTypes.InvalidCredentials))`. No extra fields, and never say which case happened (spec audit `:39`: don't reveal which emails exist).
  - `isPersistent: false` keeps the cookie a session cookie with an 8 h sliding server-side ticket. Don't pass a date from the request.
- `POST /logout`: requires an authenticated user (inherited from the group); `await signInManager.SignOutAsync(); return TypedResults.NoContent();`.
- `GET /me`: reads `ICurrentUser` and `ITenantContext`, never claims directly. It queries `db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.Name).SingleAsync()` and returns `MeResponse(Guid Id, string Email, string Role, string TenantName)`, serialized camelCase (the default).

**`PlatformModuleExtensions`:** `AddPlatformModule` calls `builder.AddPlatformIdentity()` after the DbContext registration. `MapPlatformEndpoints` calls `endpoints.MapAuthEndpoints()` and returns `endpoints`.

**`src/Api/Program.cs`:**
- Pipeline order: `app.UseExceptionHandler(); app.UseStatusCodePages(); app.UseAuthentication(); app.UseMiddleware<TenantMiddleware>(); app.UseAuthorization();`.
- Make the group `var api = app.MapGroup("/api").RequireAuthorization();`. Login opts out with `AllowAnonymous()`. `/health`, `/alive`, OpenAPI and Scalar stay outside the group.
- Keep the `migrate` early return exactly where prompt 04 put it.

Review constraints: no request carries a tenant id; never log request bodies or passwords.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Use `MapIdentityApi`, `AddIdentity<,>` (it adds its own cookie redirects) or `AddDefaultIdentity`.
- Add antiforgery, MFA, password reset or registration.
- Read claims outside `Platform.Identity` (Api's middleware already does it).
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                                     # T12 8/8 green (claims rule included)
! grep -rn 'MapIdentityApi\|AddDefaultIdentity\|AddIdentity<' src
# Smoke (throwaway container, migrate as sa, serve as inventory_app):
docker run -d --name ims3-auth -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Str0ng!Passw0rd' -p 14334:1433 mcr.microsoft.com/mssql/server:2025-latest
until docker exec ims3-auth /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Str0ng!Passw0rd' -Q 'SELECT 1' >/dev/null 2>&1; do sleep 2; done
dotnet build src/Api
ConnectionStrings__inventory='Server=localhost,14334;Database=inventory;User ID=sa;Password=Str0ng!Passw0rd;TrustServerCertificate=true' \
InventoryApp__Password='App!Passw0rd-123' dotnet run --project src/Api --no-launch-profile -- migrate
ConnectionStrings__inventory='Server=localhost,14334;Database=inventory;User ID=inventory_app;Password=App!Passw0rd-123;TrustServerCertificate=true' \
ASPNETCORE_URLS=http://localhost:5099 ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Api --no-launch-profile --no-build &
until curl -sf http://localhost:5099/health >/dev/null; do sleep 1; done   # Development maps /health
curl -s -o /dev/null -w '%{http_code} %{redirect_url}\n' http://localhost:5099/api/auth/me                 # 401, no redirect
curl -s -w '\n%{http_code}\n' -H 'Content-Type: application/json' -d '{"email":"nobody@x.test","password":"x"}' http://localhost:5099/api/auth/login   # 401 /problems/invalid-credentials
curl -s -o /dev/null -w '%{http_code}\n' -H 'Content-Type: text/plain' -d 'x' http://localhost:5099/api/auth/login   # 415
curl -s -w '\n%{http_code}\n' -H 'Content-Type: application/json' -d '{"email":"","password":""}' http://localhost:5099/api/auth/login   # 400 /problems/validation
kill %1; docker rm -f ims3-auth
```
- A successful login is covered in prompt 08, because no user exists until the seed or test helpers create one.
