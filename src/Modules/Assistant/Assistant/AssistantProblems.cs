using System.Globalization;
using System.Threading.RateLimiting;

using Kernel.Problems;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant;

/// <summary>The ProblemDetails the assistant returns (spec §9, plan D13).</summary>
internal static class AssistantProblems
{
    public static ValidationProblem Validation(Dictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, type: ProblemTypes.Uri(ProblemTypes.Validation));

    public static ProblemHttpResult NotConfigured() => TypedResults.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Assistant not configured",
        type: ProblemTypes.Uri(ProblemTypes.AssistantNotConfigured));

    public static ProblemHttpResult Unavailable() => TypedResults.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Assistant unavailable",
        type: ProblemTypes.Uri(ProblemTypes.AssistantUnavailable));

    /// <summary>The rate limiter's <c>OnRejected</c>: a 429 <c>rate-limited</c> problem with <c>Retry-After</c>.</summary>
    public static async ValueTask WriteRateLimitedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter =
                ((long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Type = ProblemTypes.Uri(ProblemTypes.RateLimited),
                Title = "Too many requests",
            },
        });
    }
}