namespace FinanceSentry.Gateway;

using Yarp.ReverseProxy.Model;

/// <summary>
/// The public listener: a second Kestrel port (<c>Gateway:PublicListener:Port</c>, also listed in
/// <c>ASPNETCORE_URLS</c>) that serves only the YARP routes named in <c>Gateway:PublicListener:Routes</c> —
/// the app and its API. Everything else answers 404 on that port: every other proxy route (Hangfire, MCP,
/// the API's detailed readiness, the frontend's probes) and the gateway's own endpoints (health, ready,
/// metrics). The allow-list is by route id, so a route added later stays off the public port until it is
/// named here. The original listener keeps the full surface unchanged. Port unset or 0: no public listener.
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

    /// <summary>True when the request arrived on another listener, or matched an allow-listed proxy route.</summary>
    public bool Admits(HttpContext context)
        => context.Connection.LocalPort != Port
           || (context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>() is { } route
               && RouteIds.Contains(route.Config.RouteId));
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
