namespace FinanceSentry.Gateway.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

/// <summary>
/// Outside Development the gateway sends <c>Strict-Transport-Security</c> on HTTPS responses, so a browser
/// that reached it over TLS never falls back to plain HTTP for this host. Development (plain HTTP on
/// localhost) sends none.
/// </summary>
public sealed class GatewayHstsTests(GatewayEndpointContractTests.GatewayFactory factory)
    : IClassFixture<GatewayEndpointContractTests.GatewayFactory>
{
    private const string HstsHeader = "Strict-Transport-Security";
    private const string HealthPath = "/gateway/health";
    private static readonly Uri HttpsHost = new("https://finance-sentry.example");
    private static readonly Uri HttpHost = new("http://finance-sentry.example");

    [Fact]
    public async Task Production_HttpsResponse_CarriesHsts()
    {
        await using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpsHost });

        var response = await client.GetAsync(HealthPath);

        Assert.True(response.Headers.TryGetValues(HstsHeader, out var values));
        Assert.Contains("max-age=31536000", Assert.Single(values));
    }

    [Fact]
    public async Task Production_PlainHttpResponse_HasNoHsts()
    {
        await using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpHost });

        var response = await client.GetAsync(HealthPath);

        Assert.False(response.Headers.Contains(HstsHeader));
    }

    [Fact]
    public async Task Development_HttpsResponse_HasNoHsts()
    {
        await using var development = factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));
        var client = development.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpsHost });

        var response = await client.GetAsync(HealthPath);

        Assert.False(response.Headers.Contains(HstsHeader));
    }
}
