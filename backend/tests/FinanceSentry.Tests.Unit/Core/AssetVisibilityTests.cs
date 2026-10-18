namespace FinanceSentry.Tests.Unit.Core;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FluentAssertions;
using Moq;
using Xunit;

public class AssetVisibilityTests
{
    private static readonly UserAssetVisibility Default = new(true, false, 1m);
    private static readonly UserAssetVisibility Small = new(true, true, 1m);

    [Fact]
    public void Zero_is_empty_when_hiding_empty_is_on() => AssetVisibility.Classify(0m, false, Default).Should().Be("empty");

    [Fact]
    public void Zero_stays_visible_when_hiding_empty_is_off()
        => AssetVisibility.Classify(0m, false, Default with { HideEmptyBalances = false }).Should().BeNull();

    [Fact]
    public void Zero_is_empty_not_small_even_with_small_hiding_on() => AssetVisibility.Classify(0m, false, Small).Should().Be("empty");

    [Fact]
    public void Zero_owed_is_empty() => AssetVisibility.Classify(0m, true, Small).Should().Be("empty");

    [Fact]
    public void Below_threshold_is_small_only_when_small_hiding_is_on()
    {
        AssetVisibility.Classify(0.99m, false, Small).Should().Be("small");
        AssetVisibility.Classify(0.99m, false, Default).Should().BeNull();
    }

    [Fact]
    public void Threshold_itself_and_above_stay_visible()
    {
        AssetVisibility.Classify(1m, false, Small).Should().BeNull();
        AssetVisibility.Classify(250m, false, Small).Should().BeNull();
    }

    [Fact]
    public void Negative_value_is_owed_and_never_small() => AssetVisibility.Classify(-0.5m, false, Small).Should().BeNull();

    [Fact]
    public void Owed_balance_is_never_small() => AssetVisibility.Classify(0.5m, true, Small).Should().BeNull();

    [Fact]
    public void Unknown_value_is_never_hidden()
    {
        AssetVisibility.Classify(null, false, Small).Should().BeNull();
        AssetVisibility.Classify(null, true, Small).Should().BeNull();
    }

    [Fact]
    public void Position_with_a_quantity_but_no_value_is_unpriced_and_never_hidden()
        => AssetVisibility.ClassifyPosition(5m, 0m, Small).Should().BeNull();

    [Fact]
    public void Position_below_the_threshold_is_small_and_a_short_never_is()
    {
        AssetVisibility.ClassifyPosition(3m, 0.4m, Small).Should().Be("small");
        AssetVisibility.ClassifyPosition(-3m, -0.4m, Small).Should().BeNull();
    }

    [Fact]
    public void Position_with_no_quantity_is_empty() => AssetVisibility.ClassifyPosition(0m, 0m, Default).Should().Be("empty");

    [Fact]
    public async Task Threshold_is_converted_from_the_base_currency_to_usd_once()
    {
        var reader = new Mock<IUserAssetVisibilityReader>();
        reader.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Small with { SmallBalanceThreshold = 10m });
        var currency = new Mock<IUserBaseCurrencyReader>();
        currency.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("UAH");

        var prefs = await AssetVisibility.ResolveInUsdAsync(reader.Object, currency.Object, Guid.NewGuid(), CancellationToken.None);

        prefs.SmallBalanceThreshold.Should().Be(CurrencyConverter.ToUsd(10m, "UAH"));
        prefs.SmallBalanceThreshold.Should().BeLessThan(10m);
    }

    [Fact]
    public async Task Without_stored_preferences_the_defaults_apply()
    {
        var prefs = await AssetVisibility.ResolveInUsdAsync(null, null, Guid.NewGuid(), CancellationToken.None);

        prefs.Should().Be(new UserAssetVisibility(true, false, 1m));
    }
}
