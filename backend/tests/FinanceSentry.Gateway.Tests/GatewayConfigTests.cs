namespace FinanceSentry.Gateway.Tests;

using FinanceSentry.Gateway;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// Config-binding invariants for the declarative YARP gateway config (feature 025, data-model.md).
/// These guard the routing table + policy wiring without booting the proxy.
/// </summary>
public sealed class GatewayConfigTests
{
    private readonly IConfiguration _config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: false)
        .Build();

    private const string FrontendCluster = "frontend";
    private const string CatchAllPath = "/{**catch-all}";
    private const int ExpectedAuthPermit = 10;

    [Fact]
    public void EveryRouteClusterId_ResolvesToADefinedCluster()
    {
        var clusterIds = ClusterIds();
        Assert.NotEmpty(clusterIds);

        foreach (var route in _config.GetSection("ReverseProxy:Routes").GetChildren())
        {
            var clusterId = route["ClusterId"];
            Assert.False(string.IsNullOrWhiteSpace(clusterId), $"Route '{route.Key}' has no ClusterId.");
            Assert.Contains(clusterId, clusterIds);
        }
    }

    [Fact]
    public void ExactlyOne_LowestPriorityCatchAll_RoutesToFrontend()
    {
        var catchAll = _config.GetSection("ReverseProxy:Routes").GetChildren()
            .Where(r => r["Match:Path"] == CatchAllPath)
            .ToList();

        Assert.Single(catchAll);
        Assert.Equal(FrontendCluster, catchAll[0]["ClusterId"]);

        // The fallback must have the highest Order (lowest priority) of all routes.
        var maxOrder = _config.GetSection("ReverseProxy:Routes").GetChildren()
            .Max(r => int.Parse(r["Order"] ?? "0"));
        Assert.Equal(maxOrder, int.Parse(catchAll[0]["Order"]!));
    }

    [Fact]
    public void AuthRoute_CarriesTheCorrectRateLimiterPolicy()
    {
        var routes = _config.GetSection("ReverseProxy:Routes").GetChildren().ToList();

        var authRoute = Assert.Single(routes, r => r["RateLimiterPolicy"] == GatewayRateLimitPolicies.Auth);
        Assert.Equal("/api/v1/auth/{**catch-all}", authRoute["Match:Path"]);
    }

    [Fact]
    public void EveryCluster_HasAtLeastOneDestination()
    {
        var clusters = _config.GetSection("ReverseProxy:Clusters").GetChildren().ToList();
        Assert.NotEmpty(clusters);

        foreach (var cluster in clusters)
        {
            var destinations = cluster.GetSection("Destinations").GetChildren().ToList();
            Assert.NotEmpty(destinations);
            Assert.All(destinations, d => Assert.False(string.IsNullOrWhiteSpace(d["Address"])));
        }
    }

    [Fact]
    public void ApiCluster_HasActiveHealthCheckOnTheHealthPath()
    {
        var active = _config.GetSection("ReverseProxy:Clusters:api:HealthCheck:Active");
        Assert.Equal("true", active["Enabled"]);
        Assert.Equal("/api/v1/health", active["Path"]);
    }

    [Fact]
    public void RateLimitDefaults_BindFromConfig()
    {
        Assert.Equal(ExpectedAuthPermit, _config.GetValue<int>("Gateway:RateLimits:Auth:PermitPerMinute"));
    }

    private List<string> ClusterIds()
        => _config.GetSection("ReverseProxy:Clusters").GetChildren().Select(c => c.Key).ToList();
}
