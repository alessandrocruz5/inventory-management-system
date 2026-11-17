using Kernel.Tenancy;

namespace Platform.Identity;

/// <summary>The Identity role names (spec §8), from <see cref="RoleNames"/>.</summary>
internal static class Roles
{
    public const string Admin = RoleNames.Admin;

    public const string Staff = RoleNames.Staff;
}