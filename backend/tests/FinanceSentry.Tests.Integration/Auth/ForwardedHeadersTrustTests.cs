namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The API honours X-Forwarded-For only from the hops named in <c>ForwardedHeaders:KnownProxies</c> —
/// with none configured, only loopback — so the client address it acts on cannot be chosen by an
/// arbitrary sender.
/// </summary>
public class ForwardedHeadersTrustTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string ForwardedClient = "100.101.102.103";
    private const string GatewayAddress = "10.231.77.2";
    private const string OtherContainerAddress = "172.18.0.5";

    [Fact]
    public async Task ForwardedFor_WithNoProxyConfigured_IsIgnoredFromNonLoopbackSender()
    {
        var remote = await ResolveClientAddressAsync(factory, sender: GatewayAddress);

        remote.Should().Be(GatewayAddress);
    }

    [Fact]
    public async Task ForwardedFor_FromSenderOutsideConfiguredProxies_IsIgnored()
    {
        await using var scoped = WithKnownProxy(GatewayAddress);

        var remote = await ResolveClientAddressAsync(scoped, sender: OtherContainerAddress);

        remote.Should().Be(OtherContainerAddress);
    }

    [Fact]
    public async Task ForwardedFor_FromConfiguredProxy_IsApplied()
    {
        await using var scoped = WithKnownProxy(GatewayAddress);

        var remote = await ResolveClientAddressAsync(scoped, sender: GatewayAddress);

        remote.Should().Be(ForwardedClient);
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
