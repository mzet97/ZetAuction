using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ZetAuction.Api.Observability;
using ZetAuction.Application.Common.Observability;

namespace ZetAuction.Api.Configuration;

/// <summary>
/// Configures the OpenTelemetry pipeline (traces + metrics) and the
/// Prometheus scrape endpoint. Logs flow through Serilog with an OTLP
/// sink (configured in <see cref="SerilogConfig"/>); the three signals
/// share the same Resource attributes so a backend (Jaeger, Prometheus,
/// Loki, Grafana) can correlate them by service name and instance.
/// </summary>
public static class ObservabilityConfig
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var serviceName = configuration["Otel:ServiceName"] ?? "zetauction-api";
        var serviceVersion = typeof(ObservabilityConfig).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var otlpEndpoint = configuration["Otel:OtlpEndpoint"];

        services.AddSingleton<ZetAuctionMetrics>();
        services.AddSingleton<IAuctionMetrics>(sp => sp.GetRequiredService<ZetAuctionMetrics>());

        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName)
            .AddTelemetrySdk();

        services.AddOpenTelemetry()
            .ConfigureResource(builder => builder.AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName).AddTelemetrySdk())
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Health probes generate a lot of spans for no value
                        // and would dilute sampling; skip them.
                        options.Filter = httpContext =>
                            !httpContext.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                        options.SetDbStatementForStoredProcedure = true;
                    })
                    .AddRedisInstrumentation()
                    .AddSource("Npgsql")
                    .AddSource("Paramore.Brighter")
                    .AddSource("Paramore.Darker");

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(ZetAuctionMetrics.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddPrometheusExporter();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return services;
    }
}
