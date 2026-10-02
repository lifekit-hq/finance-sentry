namespace FinanceSentry.Gateway;

/// <summary>
/// Security response headers (CSP, nosniff, referrer, framing, permissions) on every gateway response:
/// its own endpoints, proxied ones and rejections alike. The set is declarative in
/// <c>Gateway:SecurityHeaders</c> (header name → value) and mirrors the frontend container's
/// <c>docker/nginx.security-headers.conf</c>; the gateway's value replaces any copy the upstream sent, so
/// a response never carries two policies. HSTS stays with <c>UseHsts</c> (HTTPS-only by design).
/// </summary>
public static class GatewaySecurityHeaders
{
    public const string SectionKey = "Gateway:SecurityHeaders";

    public static IReadOnlyDictionary<string, string> FromConfig(IConfiguration config)
        => config.GetSection(SectionKey).GetChildren()
            .Where(header => !string.IsNullOrWhiteSpace(header.Value))
            .ToDictionary(header => header.Key, header => header.Value!, StringComparer.OrdinalIgnoreCase);

    public static IApplicationBuilder UseGatewaySecurityHeaders(this IApplicationBuilder app, IConfiguration config)
    {
        var headers = FromConfig(config);

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                foreach (var (name, value) in headers)
                    context.Response.Headers[name] = value;
                return Task.CompletedTask;
            });
            return next(context);
        });
    }
}
