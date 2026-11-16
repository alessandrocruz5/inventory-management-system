using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Ledger.GetLedgerHealth;

internal static class GetLedgerHealthEndpoint
{
    /// <summary>Admin only (spec §9); Staff gets 403.</summary>
    public static RouteHandlerBuilder MapGetLedgerHealth(this RouteGroupBuilder ledger) => ledger.MapGet(
        "/health",
        (GetLedgerHealthHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}