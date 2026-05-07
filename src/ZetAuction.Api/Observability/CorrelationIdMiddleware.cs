using System.Diagnostics;
using Serilog.Context;

namespace ZetAuction.Api.Observability;

/// <summary>
/// Propagates a stable correlation identifier across the request
/// pipeline so structured logs, traces and downstream calls all carry
/// the same key. Reads <c>X-Correlation-Id</c> from the request when
/// present; falls back to the W3C trace id from the current Activity,
/// or generates a fresh GUID. The chosen value is echoed back in the
/// response and also added to the Serilog log context so every log
/// line emitted in the request scope tags itself with it.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private const string LogPropertyName = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Response.Headers[HeaderName] = correlationId;
        context.Items[LogPropertyName] = correlationId;

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var inbound) && !string.IsNullOrWhiteSpace(inbound))
        {
            return inbound.ToString();
        }

        var current = Activity.Current;
        if (current is not null)
        {
            var traceId = current.TraceId.ToString();
            if (!string.IsNullOrEmpty(traceId))
            {
                return traceId;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
