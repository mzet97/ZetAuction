namespace ZetAuction.Api.Middleware;

/// <summary>
/// Sets the canonical security-hardening response headers on every
/// outbound response. The values are tuned for a JSON API: there is no
/// HTML rendered, so the CSP can be aggressively restrictive.
/// </summary>
/// <remarks>
/// Headers set:
/// <list type="bullet">
///   <item><c>Strict-Transport-Security</c> — HSTS for one year, including
///         subdomains. Only emitted on HTTPS responses to avoid pinning
///         a non-existent cert in local <c>http</c> testing.</item>
///   <item><c>Content-Security-Policy</c> — <c>default-src 'none'</c> with
///         <c>frame-ancestors 'none'</c>. Allows nothing by default; UI
///         hosts that need their own CSP should override.</item>
///   <item><c>X-Content-Type-Options: nosniff</c> — disable MIME sniffing.</item>
///   <item><c>X-Frame-Options: DENY</c> — clickjacking defence (legacy
///         clients that ignore CSP frame-ancestors).</item>
///   <item><c>Referrer-Policy: no-referrer</c> — minimise leakage on
///         outbound links from problem responses.</item>
///   <item><c>Permissions-Policy</c> — disable every powerful feature
///         the API has no business using.</item>
///   <item><c>Cross-Origin-*</c> — opt out of cross-origin embedding so
///         we don't accidentally become a Spectre gadget.</item>
///   <item>Removes <c>Server</c> if the host added it.</item>
/// </list>
/// </remarks>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            var headers = ctx.Response.Headers;

            if (ctx.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] =
                "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        }, context);

        return _next(context);
    }
}
