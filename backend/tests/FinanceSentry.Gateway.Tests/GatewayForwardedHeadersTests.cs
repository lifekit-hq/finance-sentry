namespace FinanceSentry.Gateway.Tests;

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The gateway honours X-Forwarded-For only from the hops named in <c>ForwardedHeaders:KnownProxies</c>
/// (FR-006) — with none configured, only loopback — so the client address the rate limiter partitions on
/// and the API receives cannot be chosen by an arbitrary sender.
/// </summary>
public sealed class GatewayForwardedHeadersTests(GatewayEndpointContractTests.GatewayFactory factory)
    : IClassFixture<GatewayEndpointContractTests.GatewayFactory>
{
    private const string ForwardedClient = "100.101.102.103";
    private const string EdgeBridge = "10.231.77.1";
    private const string OtherSender = "172.18.0.5";

    [Fact]
    public async Task ForwardedFor_WithNoProxyConfigured_IsIgnoredFromNonLoopbackSender()
    {
        Assert.Equal(EdgeBridge, await ResolveClientAddressAsync(factory, EdgeBridge));
    }

    [Fact]
    public async Task ForwardedFor_FromSenderOutsideConfiguredProxies_IsIgnored()
    {
        await using var scoped = WithKnownProxy(EdgeBridge);

        Assert.Equal(OtherSender, await ResolveClientAddressAsync(scoped, OtherSender));
    }

    [Fact]
    public async Task ForwardedFor_FromConfiguredProxy_IsApplied()
    {
        await using var scoped = WithKnownProxy(EdgeBridge);

        Assert.Equal(ForwardedClient, await ResolveClientAddressAsync(scoped, EdgeBridge));
    }

    private WebApplicationFactory<Program> WithKnownProxy(string proxy) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("ForwardedHeaders:KnownProxies:0", proxy));

    /// <summary>Runs the host's configured forwarded-headers middleware over a request from <paramref name="sender"/>.</summary>
    private static async Task<string?> ResolveClientAddressAsync(WebApplicationFactory<Program> host, string sender)
    {
        var options = host.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>();
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask, host.Services.GetRequiredService<ILoggerFactory>(), options);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(sender);
        context.Request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName] = ForwardedClient;

        await middleware.Invoke(context);

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
