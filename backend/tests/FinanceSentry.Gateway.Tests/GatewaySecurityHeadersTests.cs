namespace FinanceSentry.Gateway.Tests;

using System.Text.RegularExpressions;
using FinanceSentry.Gateway;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// Every gateway response carries the security headers from <c>Gateway:SecurityHeaders</c> (its own
/// endpoints, proxied responses and rejections), and that set equals the frontend container's
/// <c>docker/nginx.security-headers.conf</c>, so the app gets one policy whichever hop answers.
/// </summary>
public sealed partial class GatewaySecurityHeadersTests(GatewayPublicListenerTests.PublicListenerFactory factory)
    : IClassFixture<GatewayPublicListenerTests.PublicListenerFactory>
{
    private const string NginxSnippet = "nginx.security-headers.conf";

    private static readonly string[] RequiredHeaders =
    [
        "Content-Security-Policy",
        "X-Content-Type-Options",
        "X-Frame-Options",
        "Referrer-Policy",
        "Permissions-Policy",
    ];

    [Fact]
    public void GatewayHeaders_EqualTheFrontendNginxHeaders()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false).Build();
        var gateway = GatewaySecurityHeaders.FromConfig(config);

        var nginx = NginxHeaderLine().Matches(File.ReadAllText(NginxSnippet))
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value);

        Assert.Equal(RequiredHeaders.Order(), nginx.Keys.Order());
        Assert.Equal(nginx.OrderBy(h => h.Key), gateway.OrderBy(h => h.Key));
    }

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

    [GeneratedRegex("""^add_header (?<name>\S+) "(?<value>[^"]*)" always;$""", RegexOptions.Multiline)]
    private static partial Regex NginxHeaderLine();
}
