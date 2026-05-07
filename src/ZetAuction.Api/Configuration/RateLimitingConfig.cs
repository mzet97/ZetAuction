using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ZetAuction.Api.Configuration;

/// <summary>
/// Configures ASP.NET Core's built-in rate limiter. We protect the
/// authentication endpoints from credential-stuffing here because
/// those endpoints sit *before* the Brighter/Darker dispatch and so
/// have no other natural choke point. Bid throttling continues to use
/// the Redis-backed <c>RedisRateLimiterService</c> because it must be
/// shared across replicas.
/// </summary>
public static class RateLimitingConfig
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddRateLimitingConfig(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)TimeSpan.FromSeconds(60).TotalSeconds).ToString();
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    """{"type":"https://zetauction.dev/problems/rate-limited","title":"Too Many Requests","status":429,"detail":"Too many authentication attempts; please retry later."}""",
                    cancellationToken);
            };

            options.AddPolicy(AuthPolicy, httpContext =>
            {
                // Partition by the original client IP so a single
                // attacker can't share a window with legitimate users
                // behind the same NAT. Falls back to the connection
                // remote address when no XFF chain is configured.
                var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(5),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
        });

        return services;
    }
}
