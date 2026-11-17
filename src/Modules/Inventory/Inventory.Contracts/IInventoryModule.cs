namespace Inventory.Contracts;

/// <summary>What other modules may ask of Inventory.</summary>
public interface IInventoryModule
{
    /// <summary>Inserts the virtual locations SUP, CUS and ADJ for the current scope's tenant; does nothing for codes that exist.</summary>
    Task ProvisionTenantAsync(CancellationToken cancellationToken);
}