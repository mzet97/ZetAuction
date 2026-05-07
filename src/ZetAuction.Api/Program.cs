using ZetAuction.Api.Configuration;
using Serilog;

namespace ZetAuction.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Information("=== Starting ZetAuction API ===");

        var migrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Host.ConfigureSerilog(builder.Configuration);

            // A single misbehaving BackgroundService should never bring
            // down the entire web server. .NET 6+ defaults this to
            // StopHost — that turns any unhandled exception in a hosted
            // service into a graceful host shutdown (exit code 0), which
            // looks indistinguishable from "someone sent SIGTERM" and is
            // a nightmare to debug. Production posture is "log loudly,
            // keep serving HTTP traffic".
            builder.Services.Configure<HostOptions>(options =>
            {
                options.BackgroundServiceExceptionBehavior =
                    BackgroundServiceExceptionBehavior.Ignore;
            });

            // CreateBuilder already loads appsettings.json,
            // appsettings.{Env}.json, env vars and command line args. Adding
            // them again here would push them after any test-only providers
            // (e.g. WebApplicationFactory.ConfigureAppConfiguration), which
            // breaks integration tests by reverting overridden connection
            // strings.

            builder.Services.AddApiConfig(builder.Configuration);

            var app = builder.Build();

            // The integration test fixture provisions its own Testcontainers
            // Postgres and runs migrations manually via the WebApplicationFactory
            // lifecycle. Auto-migrating here would race against that and reuse
            // the production-style appsettings connection string.
            if (!app.Environment.IsEnvironment("Testing"))
            {
                await app.InitializeDatabaseAsync();
            }

            if (migrateOnly)
            {
                // Run as a one-shot migration job (Helm pre-install hook,
                // docker-compose api-migrate). Exit cleanly so the
                // orchestrator can roll the deployment forward.
                Log.Information("=== Migration completed; --migrate-only is set, exiting ===");
                return;
            }

            app.UseAppConfig();

            Log.Information("=== ZetAuction API configured successfully, starting... ===");
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "=== Application terminated unexpectedly ===");
            Environment.Exit(1);
        }
        finally
        {
            Log.Information("=== Shutting down ZetAuction API ===");
            Log.CloseAndFlush();
        }
    }
}
