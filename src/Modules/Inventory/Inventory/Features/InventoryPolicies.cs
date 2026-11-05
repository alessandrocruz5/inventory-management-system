using Kernel.Tenancy;

using Microsoft.AspNetCore.Authorization;

namespace Inventory.Features;

/// <summary>Role policies for Inventory routes. The /api group already requires a signed-in user (Api Program.cs).</summary>
internal static class InventoryPolicies
{
    public static readonly AuthorizationPolicy Admin = new AuthorizationPolicyBuilder().RequireRole(RoleNames.Admin).Build();
}