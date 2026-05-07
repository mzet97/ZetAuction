using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace ZetAuction.Api.Configuration;

/// <summary>
/// Configures Serilog as the host logger. Logs are written to console
/// for the local developer loop and pushed to the OTel Collector via
/// OTLP when <c>Otel:OtlpEndpoint</c> is configured. Resource attributes
/// match the ones declared in <see cref="ObservabilityConfig"/> so a
/// backend (Loki + Grafana) can correlate logs, traces, and metrics by
/// service name and instance id.
/// </summary>
public static class SerilogConfig
{
    public static void ConfigureSerilog(this IHostBuilder builder, IConfiguration configuration)
    {
        var serviceName = configuration["Otel:ServiceName"] ?? "zetauction-api";
        var serviceVersion = typeof(SerilogConfig).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var otlpEndpoint = configuration["Otel:OtlpEndpoint"];

        builder.UseSerilog((ctx, loggerConfig) =>
        {
            loggerConfig
                .ReadFrom.Configuration(ctx.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service.name", serviceName)
                .Enrich.WithProperty("service.version", serviceVersion)
                .Enrich.WithProperty("service.instance.id", Environment.MachineName)
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Information)
                .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug);

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                loggerConfig.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint;
                    options.Protocol = OtlpProtocol.Grpc;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = serviceName,
                        ["service.version"] = serviceVersion,
                        ["service.instance.id"] = Environment.MachineName,
                    };
                });
            }
        });
    }
}
