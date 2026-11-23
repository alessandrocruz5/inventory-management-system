using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Assistant.Features.Status;

internal sealed record AssistantStatusResponse(bool Configured, string? Model);

internal static class GetStatusEndpoint
{
    /// <summary>Reports whether a model is configured. Never returns the endpoint or the key, and never contacts the model.</summary>
    public static RouteHandlerBuilder MapGetStatus(this RouteGroupBuilder assistant) => assistant.MapGet(
        "/status",
        (IServiceProvider services, IOptions<AssistantOptions> options) =>
        {
            var configured = services.GetKeyedService<IChatClient>(AssistantOptions.ModelServiceKey) is not null;
            return TypedResults.Ok(new AssistantStatusResponse(configured, configured ? options.Value.Model ?? "unknown" : null));
        });
}