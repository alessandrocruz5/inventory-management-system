using Kernel.Problems;
using Kernel.Tenancy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using Platform.Persistence;

namespace Platform.Identity;

internal sealed record LoginRequest(string? Email, string? Password);

internal sealed record MeResponse(Guid Id, string Email, string Role, string TenantName);

/// <summary>Hand-written login, logout and me (spec §8). The group inherits RequireAuthorization from Api's /api group.</summary>
internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/login", LoginAsync).AllowAnonymous().Accepts<LoginRequest>("application/json");
        auth.MapPost("/logout", LogoutAsync);
        auth.MapGet("/me", MeAsync);
        return api;
    }

    private static async Task<Results<Ok<MeResponse>, ValidationProblem, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        PlatformDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["Email is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors["password"] = ["Password is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors, type: ProblemTypes.Uri(ProblemTypes.Validation));
        }

        // Every failure (unknown email, wrong password, locked out, not allowed) returns the same problem.
        var user = await userManager.FindByEmailAsync(request.Email!);
        if (user is null)
        {
            return InvalidCredentials();
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password!, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return InvalidCredentials();
        }

        // The principal is only in the cookie being written, so ITenantContext isn't filled on this request.
        var roles = await userManager.GetRolesAsync(user);
        var tenantName = await TenantNameAsync(db, user.TenantId, cancellationToken);
        return TypedResults.Ok(new MeResponse(user.Id, user.Email ?? request.Email!, roles.First(), tenantName));
    }

    private static async Task<NoContent> LogoutAsync(SignInManager<AppUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<MeResponse>> MeAsync(
        ICurrentUser user,
        ITenantContext tenant,
        PlatformDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantName = await TenantNameAsync(db, tenant.TenantId, cancellationToken);
        return TypedResults.Ok(new MeResponse(user.UserId, user.Email, user.Role, tenantName));
    }

    private static Task<string> TenantNameAsync(PlatformDbContext db, Guid tenantId, CancellationToken cancellationToken) =>
        db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).SingleAsync(cancellationToken);

    private static ProblemHttpResult InvalidCredentials() => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Sign-in failed",
        detail: "Email or password is incorrect. After 5 failed attempts the account is locked for 15 minutes.",
        type: ProblemTypes.Uri(ProblemTypes.InvalidCredentials));
}