namespace FinanceSentry.Gateway;

using Yarp.ReverseProxy.Model;

/// <summary>
/// The public listener: a second Kestrel port (<c>Gateway:PublicListener:Port</c>, also listed in
/// <c>ASPNETCORE_URLS</c>) that serves only the YARP routes named in <c>Gateway:PublicListener:Routes</c> —
/// the app and its API. Everything else answers 404 on that port: every other proxy route (Hangfire, MCP,
/// the API's detailed readiness, the frontend's probes) and the gateway's own endpoints (health, ready,
/// metrics), as well as any path that is not in normalized form. The allow-list is by route id, so a
/// route added later stays off the public port until it is named here. The original listener keeps the
/// full surface unchanged. Port unset or 0: no public listener.
/// </summary>
public sealed class GatewayPublicListener
{
    public const string PortKey = "Gateway:PublicListener:Port";
    public const string RoutesKey = "Gateway:PublicListener:Routes";

    private GatewayPublicListener(int port, IReadOnlySet<string> routeIds)
    {
        Port = port;
        RouteIds = routeIds;
    }

    public int Port { get; }

    public IReadOnlySet<string> RouteIds { get; }

    public static GatewayPublicListener? FromConfig(IConfiguration config)
    {
        var port = config.GetValue(PortKey, 0);
        if (port <= 0)
            return null;

        var routeIds = config.GetSection(RoutesKey).Get<string[]>() ?? [];
        return new GatewayPublicListener(port, routeIds.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// True when the request arrived on another listener, or has a normalized path and matched an
    /// allow-listed proxy route. Routing matches the path as sent, but the frontend's nginx merges slashes
    /// and decodes <c>%2F</c>/<c>%5C</c>, so <c>//metrics</c> would reach the catch-all route here and
    /// the exact <c>/metrics</c>, <c>/healthz</c> and <c>/readyz</c> locations there.
    /// </summary>
    public bool Admits(HttpContext context)
        => context.Connection.LocalPort != Port
           || (IsNormalized(context.Request.Path.Value)
               && context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>() is { } route
               && RouteIds.Contains(route.Config.RouteId));

    private static bool IsNormalized(string? path)
        => string.IsNullOrEmpty(path)
           || (!path.Contains("//", StringComparison.Ordinal)
               && !path.Contains('\\')
               && !path.Contains("%2F", StringComparison.OrdinalIgnoreCase)
               && !path.Contains("%5C", StringComparison.OrdinalIgnoreCase));
}

public static class GatewayPublicListenerExtensions
{
    /// <summary>Must run after routing, so the matched endpoint (and its YARP route) is known.</summary>
    public static IApplicationBuilder UseGatewayPublicListener(this IApplicationBuilder app, IConfiguration config)
    {
        var listener = GatewayPublicListener.FromConfig(config);
        if (listener is null)
            return app;

        return app.Use((context, next) =>
        {
            if (listener.Admits(context))
                return next(context);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
    }
}
