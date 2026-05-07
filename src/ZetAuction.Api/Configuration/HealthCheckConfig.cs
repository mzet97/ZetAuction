using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ZetAuction.Api.Configuration;

public static class HealthCheckConfig
{
    public static IServiceCollection AddHealthCheckConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var npgsqlConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is required for the Postgres health check.");

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException(
                "Connection string 'Redis' is required for the Redis health check.");

        services.AddHealthChecks()
            .AddNpgSql(
                npgsqlConnectionString,
                name: "postgres",
                tags: ["db", "ready"])
            .AddRedis(
                redisConnectionString,
                name: "redis",
                tags: ["cache", "ready"]);

        // HealthChecks UI is intentionally NOT registered. Its
        // HealthCheckReportCollector hosted service resolves the local
        // /health URL via the ASPNETCORE_URLS prefix, which under
        // `http://+:8080` (the docker compose default) parses to
        // 0.0.0.0/[::] — an unspecified address Dns refuses to dial.
        // The resulting flood of catched-but-noisy exceptions clutters
        // logs and, depending on the host options, can interact badly
        // with the lifetime. We expose /health, /health/ready and
        // /health/live directly, plus the Grafana stack already gives
        // us a dashboard for liveness/readiness — UI add-on is
        // redundant for our scope.

        return services;
    }

    public static IApplicationBuilder UseHealthCheckConfiguration(this IApplicationBuilder app)
    {
        app.UseHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteHealthCheckResponse,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        app.UseHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthCheckResponse,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        app.UseHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthCheckResponse
        });

        // UseHealthChecksUI removed — see AddHealthCheckConfiguration
        // for rationale. The /health, /health/ready and /health/live
        // endpoints stay (they're plain JSON, no UI dependency).

        return app;
    }

    private static Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                exception = e.Value.Exception?.Message,
                duration = e.Value.Duration.ToString()
            }),
            totalDuration = report.TotalDuration.ToString()
        };
        return context.Response.WriteAsJsonAsync(result);
    }
}
