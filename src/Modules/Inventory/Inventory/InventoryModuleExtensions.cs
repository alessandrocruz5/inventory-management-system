using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Inventory;

public static class InventoryModuleExtensions
{
    public static IHostApplicationBuilder AddInventoryModule(this IHostApplicationBuilder builder) => builder;

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}