using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using ZetAuction.Api.Common;
using ZetAuction.Api.Middleware;
using ZetAuction.Api.Observability;
using ZetAuction.Infrastructure.Persistence;

namespace ZetAuction.Api.Configuration;

public static class AppConfig
{
    public static WebApplication UseAppConfig(this WebApplication app)
    {
        // The ProblemDetails exception handler replaces the legacy
        // ExceptionHandlingMiddleware. Registered as IExceptionHandler in
        // ApiConfig.cs and wired here via UseExceptionHandler so it runs
        // before authentication/authorization, ensuring even
        // pre-authentication failures emit RFC 7807 responses.
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        // Correlation-id sits at the very front of the pipeline so every
        // log line, every span, every problem response and every outbound
        // request carries the same id without any handler having to opt
        // in.
        app.UseMiddleware<CorrelationIdMiddleware>();

        // Security headers run before authentication so that even
        // 401/403 responses ship with the canonical hardening set.
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // Rate limiter must come before auth so the limiter sees the
        // request before any auth side-effects (DB hit on login, hash
        // verification cost, etc.) are paid. Skipped in the Testing
        // environment because the integration tests share an IP and
        // would saturate the 5/5min budget within a few methods.
        if (!app.Environment.IsEnvironment("Testing"))
        {
            app.UseRateLimiter();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapOpenApi();

        app.MapScalarApiReference(options =>
        {
            options.Title = "ZetAuction API";
            options.Theme = ScalarTheme.Mars;
        });

        app.UseHealthCheckConfiguration();

        // Prometheus scrape endpoint exposed at /metrics. The OTLP
        // exporter additionally pushes the same data to the OTel
        // Collector / Jaeger / Grafana stack started by docker-compose.
        app.MapPrometheusScrapingEndpoint();

        app.MapEndpoints();

        return app;
    }

    /// <summary>
    /// Applies any pending EF Core migrations to the configured Postgres
    /// database. Replaces the previous <c>EnsureCreatedAsync</c> bootstrap so
    /// schema changes are versioned and reproducible across environments.
    /// </summary>
    /// <remarks>
    /// In multi-instance deployments the migration runner should be a
    /// dedicated init container or job (see <c>infra/</c>) rather than every
    /// API replica. This method is idempotent and safe under concurrent
    /// invocations because EF Core acquires an advisory lock per migration.
    /// </remarks>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ZetAuctionDbContext>();
            await dbContext.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogCritical(ex, "Failed to apply database migrations on startup");
            throw;
        }
    }
}
