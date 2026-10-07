namespace FinanceSentry.Gateway.Tests;

using FinanceSentry.Gateway;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// Every gateway response carries the security headers from <c>Gateway:SecurityHeaders</c> (its own
/// endpoints, proxied responses and rejections).
/// </summary>
public sealed class GatewaySecurityHeadersTests(GatewayPublicListenerTests.PublicListenerFactory factory)
    : IClassFixture<GatewayPublicListenerTests.PublicListenerFactory>
{
    private static readonly string[] RequiredHeaders =
    [
        "Content-Security-Policy",
        "X-Content-Type-Options",
        "X-Frame-Options",
        "Referrer-Policy",
        "Permissions-Policy",
    ];

    [Fact]
    public void Policy_ForbidsFramingTheApp()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false).Build();
        var headers = GatewaySecurityHeaders.FromConfig(config);

        Assert.Contains("frame-ancestors 'none'", headers["Content-Security-Policy"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"]);
    }

    // The Angular service worker fetches every cross-origin logo itself, and that fetch is checked
    // against connect-src, not img-src: each host img-src admits must be in connect-src too.
    [Fact]
    public void Policy_LetsTheServiceWorkerConnectToEveryImageHost()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false).Build();
        var csp = GatewaySecurityHeaders.FromConfig(config)["Content-Security-Policy"];

        var imageHosts = RemoteSources(csp, "img-src");
        var connectHosts = RemoteSources(csp, "connect-src");

        Assert.NotEmpty(imageHosts);
        Assert.All(imageHosts, host => Assert.Contains(host, connectHosts));
    }

    private static string[] RemoteSources(string csp, string directive)
        => csp.Split(';', StringSplitOptions.TrimEntries)
            .Select(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Single(parts => parts[0] == directive)
            .Where(source => source.StartsWith("https://", StringComparison.Ordinal))
            .ToArray();

    [Theory]
    [InlineData("/gateway/health", null)]           // the gateway's own endpoint
    [InlineData("/metrics", null)]
    [InlineData("/api/v1/accounts", null)]          // proxied (upstream unreachable in the test)
    [InlineData("/dashboard", null)]
    [InlineData("/dashboard", GatewayPublicListenerTests.PublicPort)]
    [InlineData("/hangfire", GatewayPublicListenerTests.PublicPort)]  // refused on the public listener
    public async Task EveryResponse_CarriesTheSecurityHeaders(string path, int? localPort)
    {
        var response = await GatewayPublicListenerTests.SendAsync(factory, path, localPort);

        foreach (var header in RequiredHeaders)
            Assert.True(response.Headers.Contains(header), $"{path} lacks {header} ({(int)response.StatusCode}).");
    }
}
