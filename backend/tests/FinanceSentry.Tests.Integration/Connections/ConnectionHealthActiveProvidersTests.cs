namespace FinanceSentry.Tests.Integration.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Jobs;
using FinanceSentry.Tests.Integration.Auth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The API host's committed configuration puts only IBKR under policy-driven alerting (Option B, S3);
/// every other provider stays in shadow mode until it is added to <c>ConnectionHealth:ActiveProviders</c>.
/// </summary>
public class ConnectionHealthActiveProvidersTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public void CommittedConfiguration_MakesOnlyIbkrLive()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<ConnectionHealthOptions>>().CurrentValue;

        options.IsActive("ibkr").Should().BeTrue();
        foreach (var shadow in new[] { "ibkr-flex", "binance", "revolut_x", "monobank", "truelayer", "inzhur" })
            options.IsActive(shadow).Should().BeFalse("{0} stays in shadow mode", shadow);
    }

    [Fact]
    public void ActiveProviders_IsReplacedFromConfiguration_ToFlipAProvider()
    {
        using var flipped = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionHealth:ActiveProviders:0", "binance");
            builder.UseSetting("ConnectionHealth:ActiveProviders:1", "revolut_x");
        });

        var options = flipped.Services.GetRequiredService<IOptionsMonitor<ConnectionHealthOptions>>().CurrentValue;

        options.ActiveProviders.Should().BeEquivalentTo("binance", "revolut_x");
        options.IsActive("ibkr").Should().BeFalse();
    }

    [Fact]
    public void TheAdoptingSyncJobs_ResolveFromTheHost()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IBKRSyncJob>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IbkrFlexIncrementalSyncJob>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<BinanceSyncJob>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<RevolutXSyncJob>().Should().NotBeNull();
    }
}
