namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// The six 044 threshold keys are deployed in appsettings under <c>HygieneSentinels</c>; the sentinels
/// read them through <see cref="HygieneSentinelsOptions"/>. Nothing in the compiler ties the two
/// together, so renaming a property would silently orphan the deployed key and leave that sentinel on
/// its in-code default in production — the exact failure the options object exists to prevent.
/// </summary>
public class HygieneSentinelsOptionsTests
{
    // Spelled the way appsettings.json spells them, paired with a value distinct from both the shipped
    // setting and the in-code default, so an override that fails to land is visible.
    private static readonly Dictionary<string, string?> Overrides = new()
    {
        ["HygieneSentinels:PriceHikeThreshold"] = "0.25",
        ["HygieneSentinels:DuplicateWindowDays"] = "9",
        ["HygieneSentinels:CategorySpikeMultiplier"] = "3.5",
        ["HygieneSentinels:FxSpreadLookbackDays"] = "7",
        ["HygieneSentinels:FxSpreadThreshold"] = "0.08",
        ["HygieneSentinels:FxSpreadMaxRateAgeHours"] = "12",
    };

    private static HygieneSentinelsOptions Bind(IConfiguration config)
    {
        var options = new HygieneSentinelsOptions();
        config.GetSection(HygieneSentinelsOptions.SectionName).Bind(options);
        return options;
    }

    /// <summary>Walks up from the test assembly to the API project's deployed appsettings.json.</summary>
    private static string DeployedAppSettingsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FinanceSentry.API", "appsettings.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            $"src/FinanceSentry.API/appsettings.json not found above {AppContext.BaseDirectory}");
    }

    /// <summary>
    /// Layers the overrides over the file that actually ships, so the test is not circular: a renamed
    /// property leaves its key unbound and the corresponding assertion sees the shipped value instead.
    /// </summary>
    [Fact]
    public void Bind_MapsEveryDeployedKey_ToItsProperty()
    {
        var shipped = new ConfigurationBuilder().AddJsonFile(DeployedAppSettingsPath()).Build();
        shipped.GetSection(HygieneSentinelsOptions.SectionName).Exists().Should().BeTrue();
        foreach (var key in Overrides.Keys)
        {
            shipped[key].Should().NotBeNull($"{key} is deployed and must stay spelled that way");
        }

        var options = Bind(new ConfigurationBuilder()
            .AddJsonFile(DeployedAppSettingsPath())
            .AddInMemoryCollection(Overrides)
            .Build());

        options.PriceHikeThreshold.Should().Be(0.25m);
        options.DuplicateWindowDays.Should().Be(9);
        options.CategorySpikeMultiplier.Should().Be(3.5m);
        options.FxSpreadLookbackDays.Should().Be(7);
        options.FxSpreadThreshold.Should().Be(0.08m);
        options.FxSpreadMaxRateAgeHours.Should().Be(12);
    }

    /// <summary>
    /// An absent section must leave every sentinel on the setting spec.md documents — the defaults are
    /// the contract, not a placeholder, because no environment file overrides all six.
    /// </summary>
    [Fact]
    public void Bind_LeavesDocumentedDefaults_WhenSectionAbsent()
    {
        var options = Bind(new ConfigurationBuilder().Build());

        options.PriceHikeThreshold.Should().Be(0.15m);
        options.DuplicateWindowDays.Should().Be(5);
        options.CategorySpikeMultiplier.Should().Be(2.0m);
        options.FxSpreadLookbackDays.Should().Be(3);
        options.FxSpreadThreshold.Should().Be(0.03m);
        options.FxSpreadMaxRateAgeHours.Should().Be(48);
    }

    /// <summary>The shipped file must state the documented defaults, so no environment silently differs.</summary>
    [Fact]
    public void ShippedAppSettings_StatesTheDocumentedDefaults()
    {
        var options = Bind(new ConfigurationBuilder().AddJsonFile(DeployedAppSettingsPath()).Build());

        options.Should().BeEquivalentTo(new HygieneSentinelsOptions());
    }
}
