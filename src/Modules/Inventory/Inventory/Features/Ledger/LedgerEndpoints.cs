using Inventory.Features.Ledger.GetLedgerHealth;
using Inventory.Features.Ledger.RebuildLedger;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Ledger;

internal static class LedgerEndpoints
{
    /// <summary>Maps <c>/ledger</c> under the caller's group, which already requires a signed-in user (Api Program.cs).</summary>
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder api)
    {
        var ledger = api.MapGroup("/ledger");
        ledger.MapGetLedgerHealth();
        ledger.MapRebuildLedger();
        return api;
    }
}