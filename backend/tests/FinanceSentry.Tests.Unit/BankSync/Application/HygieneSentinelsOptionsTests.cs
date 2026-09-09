namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// The six 044 threshold keys are deployed in appsettings under <c>HygieneSentinels</c>; the sentinels
/// now read them through <see cref="HygieneSentinelsOptions"/>. Nothing in the compiler ties the two
/// together, so renaming a property would silently orphan the deployed key and revert that sentinel to
/// its in-code default — the exact failure the options object was introduced to make impossible. These
/// tests hold the key names and the documented defaults in place.
/// </summary>
public class HygieneSentinelsOptionsTests
{
    // Spelled the way appsettings.json spells them — that is the point of the test.
    private static readonly Dictionary<string, string?> DeployedKeys = new()
    {
        ["HygieneSentinels:PriceHikeThreshold"] = "0.25",
        ["HygieneSentinels:DuplicateWindowDays"] = "9",
        ["HygieneSentinels:CategorySpikeMultiplier"] = "3.5",
        ["HygieneSentinels:FxSpreadLookbackDays"] = "7",
        ["HygieneSentinels:FxSpreadThreshold"] = "0.08",
        ["HygieneSentinels:FxSpreadMaxRateAgeHours"] = "12",
    };

    private static HygieneSentinelsOptions Bind(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new HygieneSentinelsOptions();
        config.GetSection(HygieneSentinelsOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void Bind_OverridesEveryThreshold_FromTheDeployedKeyNames()
    {
        var options = Bind(DeployedKeys);

        options.PriceHikeThreshold.Should().Be(0.25m);
        options.DuplicateWindowDays.Should().Be(9);
        options.CategorySpikeMultiplier.Should().Be(3.5m);
        options.FxSpreadLookbackDays.Should().Be(7);
        options.FxSpreadThreshold.Should().Be(0.08m);
        options.FxSpreadMaxRateAgeHours.Should().Be(12);
    }

    /// <summary>
    /// An absent section must leave every sentinel on the setting spec.md documents — the defaults are
    /// the contract, not a placeholder, because no environment file sets all six.
    /// </summary>
    [Fact]
    public void Bind_LeavesDocumentedDefaults_WhenSectionAbsent()
    {
        var options = Bind([]);

        options.PriceHikeThreshold.Should().Be(0.15m);
        options.DuplicateWindowDays.Should().Be(5);
        options.CategorySpikeMultiplier.Should().Be(2.0m);
        options.FxSpreadLookbackDays.Should().Be(3);
        options.FxSpreadThreshold.Should().Be(0.03m);
        options.FxSpreadMaxRateAgeHours.Should().Be(48);
    }
}
