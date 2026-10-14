namespace FinanceSentry.Tests.Unit.BankSync.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Monobank and TrueLayer stay in shadow mode until they are listed in <c>ConnectionHealth:ActiveProviders</c>; listing
/// them is the whole flip, a one-line configuration change.
/// </summary>
public class BankConnectionHealthActivationTests
{
    private static bool[] ActiveFor(IDictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new ServiceCollection()
            .AddConnectionHealthShadow(config)
            .BuildServiceProvider()
            .GetRequiredService<IOptions<ConnectionHealthOptions>>()
            .Value;

        var health = BankConnectionHealths.Create(
            Mock.Of<IBankAccountRepository>(), Mock.Of<IMonobankCredentialRepository>(), Mock.Of<ITrueLayerConnectionRepository>(),
            Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(), TimeProvider.System, options);
        return [health.IsActive("monobank"), health.IsActive("truelayer")];
    }

    [Fact]
    public void NoConfiguration_BothBanksStayInShadow()
        => ActiveFor(new Dictionary<string, string?>()).Should().Equal(false, false);

    [Fact]
    public void AnotherProviderActive_BothBanksStayInShadow()
        => ActiveFor(new Dictionary<string, string?> { ["ConnectionHealth:ActiveProviders:0"] = "ibkr" })
            .Should().Equal(false, false);

    [Fact]
    public void ListingABank_MakesItPolicyDriven_CaseInsensitively()
        => ActiveFor(new Dictionary<string, string?>
            {
                ["ConnectionHealth:ActiveProviders:0"] = "ibkr",
                ["ConnectionHealth:ActiveProviders:1"] = "Monobank",
            })
            .Should().Equal(true, false);
}
