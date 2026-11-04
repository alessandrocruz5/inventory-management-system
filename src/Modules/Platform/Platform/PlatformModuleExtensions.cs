using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Platform;

public static class PlatformModuleExtensions
{
    public static IHostApplicationBuilder AddPlatformModule(this IHostApplicationBuilder builder) => builder;

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}