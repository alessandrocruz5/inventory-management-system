using System.ClientModel;
using System.Threading.RateLimiting;

using Assistant.Drafts;
using Assistant.Features.Chat;
using Assistant.Features.Status;
using Assistant.Tools;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using OpenAI;

namespace Assistant;

internal static class AssistantRateLimit
{
    public const string PolicyName = "assistant";
}

public static class AssistantModuleExtensions
{
    public static IHostApplicationBuilder AddAssistantModule(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<AssistantOptions>()
            .Bind(builder.Configuration.GetSection(AssistantOptions.Section))
            .Validate(
                o => o.PermitsPerMinute is >= 1 and <= 10_000 && o.TimeoutSeconds is >= 1 and <= 110,
                "Assistant:PermitsPerMinute must be 1..10000 and Assistant:TimeoutSeconds 1..110.")
            .ValidateOnStart();

        // Read once at registration, as AddInventoryModule reads its connection string.
        var o = builder.Configuration.GetSection(AssistantOptions.Section).Get<AssistantOptions>() ?? new();

        // No model configured → no keyed client → the assistant answers 503 assistant-not-configured (plan D14).
        if (!string.IsNullOrWhiteSpace(o.Endpoint) && !string.IsNullOrWhiteSpace(o.Model))
        {
            if (!Uri.TryCreate(o.Endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Assistant:Endpoint must be an absolute http(s) URL.");

            // Ollama ignores the key, but the SDK requires one.
            builder.Services.AddKeyedSingleton<IChatClient>(AssistantOptions.ModelServiceKey, (_, _) =>
                new OpenAIClient(
                        new ApiKeyCredential(string.IsNullOrWhiteSpace(o.ApiKey) ? "unused" : o.ApiKey),
                        new OpenAIClientOptions { Endpoint = uri })
                    .GetChatClient(o.Model)
                    .AsIChatClient());
        }

        builder.Services.AddScoped<InventoryTools>();
        builder.Services.AddScoped<DraftValidator>();
        builder.Services.AddScoped<DraftCollector>();
        builder.Services.AddScoped<DraftTool>();
        builder.Services.AddScoped<ToolCatalog>();
        builder.Services.AddSingleton<AssistantModel>();
        builder.Services.AddScoped<ChatHandler>();

        // Per-user fixed window (plan D12). No global limiter: only endpoints that opt in are limited.
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = AssistantProblems.WriteRateLimitedAsync;
            limiter.AddPolicy(AssistantRateLimit.PolicyName, http =>
            {
                var user = http.RequestServices.GetRequiredService<ICurrentUser>();
                var permits = http.RequestServices.GetRequiredService<IOptions<AssistantOptions>>().Value.PermitsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(
                    user.IsAuthenticated ? user.UserId.ToString() : "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permits,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });

        return builder;
    }

    /// <summary>Maps the routes under <c>/assistant</c>: status and chat.</summary>
    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var assistant = endpoints.MapGroup("/assistant");
        assistant.MapGetStatus();
        assistant.MapPostChat();
        return endpoints;
    }
}