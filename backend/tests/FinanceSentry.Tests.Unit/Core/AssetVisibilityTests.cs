namespace FinanceSentry.Tests.Unit.Core;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FluentAssertions;
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
}
