using System.Security.Claims;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Platform.Identity;

/// <summary>Adds the user's tenant to the cookie principal. The tenant never comes from the request (spec §8).</summary>
internal sealed class TenantClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(TenantClaims.TenantId, user.TenantId.ToString()));
        return identity;
    }
}