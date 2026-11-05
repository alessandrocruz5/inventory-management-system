namespace Inventory.Persistence;

/// <summary>A row owned by one tenant: it gets the "Tenant" query filter, the session default and RLS (spec §8).</summary>
internal interface ITenantOwned
{
    Guid TenantId { get; set; }
}