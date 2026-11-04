using System.Security.Claims;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Mvc;

namespace Api.Tenancy;

/// <summary>
/// Fills <see cref="ITenantContext"/> and <see cref="ICurrentUser"/> from the signed-in principal's claims.
/// The tenant comes only from the auth cookie, never from the request (spec §8).
/// </summary>
internal sealed class TenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenant, CurrentUser user)
    {
        var principal = context.User;

        // Anonymous: authorization decides later whether the endpoint allows it.
        if (principal.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        if (!TryParseId(principal.FindFirstValue(TenantClaims.TenantId), out var tenantId)
            || !TryParseId(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || principal.FindFirstValue(ClaimTypes.Email) is not { Length: > 0 } email
            || principal.FindFirstValue(ClaimTypes.Role) is not { Length: > 0 } role)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            var problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
            await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" },
            });
            return;
        }

        tenant.Set(tenantId);
        user.Set(userId, email, role);
        await next(context);
    }

    private static bool TryParseId(string? value, out Guid id) => Guid.TryParse(value, out id) && id != Guid.Empty;
}