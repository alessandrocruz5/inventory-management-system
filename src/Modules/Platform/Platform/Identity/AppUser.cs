using Microsoft.AspNetCore.Identity;

namespace Platform.Identity;

/// <summary>An Identity user. Each user belongs to exactly one tenant.</summary>
internal sealed class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
}