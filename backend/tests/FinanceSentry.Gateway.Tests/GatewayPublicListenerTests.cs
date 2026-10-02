namespace FinanceSentry.Gateway.Tests;

using System.Net;
using FinanceSentry.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The public listener serves the app and its API and nothing else; the original listener keeps the full
/// surface. Requests are tagged with the port they "arrived on" (the in-memory test server has no
/// sockets), and upstreams point at a closed local port, so a proxied request answers 502/503 while a
/// refused one answers 404. Every configured route outside the allow-list is probed, so a route added
/// later is refused on the public port until it is deliberately allow-listed (and this test updated).
/// </summary>
public sealed class GatewayPublicListenerTests(GatewayPublicListenerTests.PublicListenerFactory factory)
    : IClassFixture<GatewayPublicListenerTests.PublicListenerFactory>
{
    public const int PublicPort = 8081;
    private const string LocalPortHeader = "X-Test-Local-Port";
    private const string ClosedUpstream = "http://127.0.0.1:9/";
    private const string CatchAll = "{**catch-all}";

    private static readonly string[] PublicRoutes = ["auth-route", "api-route", "frontend-route"];

    [Fact]
    public void AllowList_IsExactlyTheAppAndTheApi()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false).Build();

        var allowed = config.GetSection(GatewayPublicListener.RoutesKey).Get<string[]>() ?? [];

        Assert.Equal(PublicRoutes.Order(), allowed.Order());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/dashboard")]
    [InlineData("/main.js")]
    [InlineData("/api/v1/accounts")]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/health")]
    [InlineData("/api/v1/healthcheck")]
    public async Task PublicListener_ProxiesTheAppAndTheApi(string path)
    {
        var response = await SendAsync(factory, path, PublicPort);

        AssertProxied(response, path);
    }

    [Theory]
    [InlineData("/hangfire")]
    [InlineData("/hangfire/jobs/enqueued")]
    [InlineData("/mcp")]
    [InlineData("/mcp/sse")]
    [InlineData("/metrics")]
    [InlineData("/gateway/health")]
    [InlineData("/gateway/ready")]
    [InlineData("/api/v1/health/ready")]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    [InlineData("/api/v1/health/ready/")]
    [InlineData("/api/v1/health/ready/x")]
    [InlineData("/API/V1/HEALTH/READY")]
    [InlineData("/healthz/")]
    [InlineData("/readyz/")]
    public async Task PublicListener_RefusesTheAdminSurface(string path)
    {
        var response = await SendAsync(factory, path, PublicPort);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("//metrics")]
    [InlineData("//healthz")]
    [InlineData("//readyz")]
    [InlineData("///readyz")]
    [InlineData("//gateway/health")]
    [InlineData("/%2Fmetrics")]
    [InlineData("/%2fhealthz")]
    [InlineData("/%5Cmetrics")]
    public async Task PublicListener_RefusesPathsNginxWouldNormalizeToTheAdminSurface(string path)
    {
        var response = await SendAsync(factory, path, PublicPort);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicListener_RefusesEveryRouteOutsideTheAllowList()
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var routes = config.GetSection("ReverseProxy:Routes").GetChildren().ToList();
        Assert.NotEmpty(routes);

        foreach (var route in routes)
        {
            var path = route["Match:Path"]!.Replace(CatchAll, "probe", StringComparison.Ordinal);
            var response = await SendAsync(factory, path, PublicPort);

            if (PublicRoutes.Contains(route.Key))
                AssertProxied(response, $"{route.Key} ({path})");
            else
                Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                    $"{route.Key} ({path}) is reachable on the public listener: {(int)response.StatusCode}.");
        }
    }

    [Theory]
    [InlineData("/hangfire")]
    [InlineData("/mcp/sse")]
    [InlineData("/api/v1/health/ready")]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    [InlineData("//healthz")]
    [InlineData("/api/v1/health/ready/")]
    [InlineData("/dashboard")]
    [InlineData("/api/v1/accounts")]
    public async Task AdminListener_KeepsProxyingTheFullSurface(string path)
    {
        var response = await SendAsync(factory, path, localPort: null);

        AssertProxied(response, path);
    }

    [Theory]
    [InlineData("/gateway/health")]
    [InlineData("/gateway/ready")]
    [InlineData("/metrics")]
    public async Task AdminListener_KeepsTheGatewayEndpoints(string path)
    {
        var response = await SendAsync(factory, path, localPort: null);

        // Served by the gateway itself (ready may report 503 once the closed upstreams are marked down).
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, response.StatusCode);
    }

    public static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> host, string path, int? localPort)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (localPort is { } port)
            request.Headers.Add(LocalPortHeader, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return await host.CreateClient().SendAsync(request);
    }

    /// <summary>The proxy forwarded it: the closed upstream answers 502, or 503 once marked unhealthy.</summary>
    private static void AssertProxied(HttpResponseMessage response, string what)
        => Assert.True(
            response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable,
            $"{what} was not proxied: {(int)response.StatusCode}.");

    public sealed class PublicListenerFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Observability:Otlp:Endpoint", string.Empty);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GatewayPublicListener.PortKey] = PublicPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ReverseProxy:Clusters:api:HealthCheck:Active:Enabled"] = "false",
                ["ReverseProxy:Clusters:frontend:HealthCheck:Active:Enabled"] = "false",
                ["ReverseProxy:Clusters:api:Destinations:api-1:Address"] = ClosedUpstream,
                ["ReverseProxy:Clusters:mcp:Destinations:mcp-1:Address"] = ClosedUpstream,
                ["ReverseProxy:Clusters:frontend:Destinations:frontend-1:Address"] = ClosedUpstream,
                ["Gateway:RateLimits:Auth:PermitPerMinute"] = "1000",
            }));
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, LocalPortFromHeader>());
        }
    }

    /// <summary>Sets the connection's local port from a test header, ahead of the whole pipeline.</summary>
    private sealed class LocalPortFromHeader : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (int.TryParse(context.Request.Headers[LocalPortHeader], out var port))
                    context.Connection.LocalPort = port;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
