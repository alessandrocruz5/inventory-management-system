using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Assistant.Features.Chat;

internal static class PostChatEndpoint
{
    /// <summary>
    /// One stateless turn. Limited per user (plan D12) and to <see cref="AssistantLimits.MaxBodyBytes"/> of request body.
    /// The body carries no tenant or user id: the tenant comes from the signed-in user.
    /// </summary>
    public static RouteHandlerBuilder MapPostChat(this RouteGroupBuilder assistant) => assistant
        .MapPost(
            "/chat",
            (AssistantChatRequest request, ChatHandler handler, HttpContext http, CancellationToken ct) =>
                handler.HandleAsync(request, http, ct))
        .RequireRateLimiting(AssistantRateLimit.PolicyName)
        .WithMetadata(new RequestSizeLimitAttribute(AssistantLimits.MaxBodyBytes));
}