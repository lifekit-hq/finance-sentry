namespace FinanceSentry.Gateway.Tests;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// REST contract of the gateway's own endpoints (constitution § Testing Discipline; spec 048 US2):
/// the platform probes them unauthenticated and rejects <c>text/html</c>, so the status code and
/// content type are the contract. Boots the real host with YARP's active health checks switched off
/// (no upstreams exist in the test) and the OTLP exporter disabled.
/// </summary>
public sealed class GatewayEndpointContractTests : IClassFixture<GatewayEndpointContractTests.GatewayFactory>
{
    private readonly HttpClient _client;

    public GatewayEndpointContractTests(GatewayFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_Is200Json()
    {
        var response = await _client.GetAsync("/gateway/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Ready_Is200Json_ListingEveryConfiguredCluster()
    {
        var response = await _client.GetAsync("/gateway/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(GatewayReadiness.ReadyStatus, body.RootElement.GetProperty("status").GetString());
        var clusters = body.RootElement.GetProperty("clusters").EnumerateArray()
            .Select(c => c.GetProperty("id").GetString())
            .ToList();
        Assert.Equal(["api", "frontend", "mcp"], clusters);
    }

    [Fact]
    public async Task Metrics_Is200PrometheusText()
    {
        var response = await _client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    public sealed class GatewayFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Observability:Otlp:Endpoint", string.Empty);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:api:HealthCheck:Active:Enabled"] = "false",
                ["ReverseProxy:Clusters:frontend:HealthCheck:Active:Enabled"] = "false",
            }));
        }
    }
}
