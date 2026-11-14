namespace FinanceSentry.Modules.Research.Tests.Valuation;

using FinanceSentry.Modules.Research.Domain.Valuation;
using FluentAssertions;
using Xunit;

public sealed class CompsMathTests
{
    [Fact]
    public void Describe_odd_count_takes_the_middle_value_and_exact_quartiles()
    {
        var d = CompsMath.Describe([5m, 1m, 3m, 2m, 4m])!;

        d.Count.Should().Be(5);
        (d.Min, d.P25, d.Median, d.P75, d.Max).Should().Be((1m, 2m, 3m, 4m, 5m));
    }

    [Fact]
    public void Describe_even_count_interpolates_median_and_quartiles()
    {
        var d = CompsMath.Describe([4m, 1m, 3m, 2m])!;

        (d.Min, d.P25, d.Median, d.P75, d.Max).Should().Be((1m, 1.75m, 2.5m, 3.25m, 4m));
    }

    [Fact]
    public void Median_is_not_the_mean_when_one_value_is_extreme()
    {
        var d = CompsMath.Describe([10m, 11m, 12m, 90m])!;

        d.Median.Should().Be(11.5m);
        d.Median.Should().NotBe(30.75m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Describe_needs_at_least_three_values(int count)
    {
        CompsMath.Describe(Enumerable.Repeat(10m, count).ToList()).Should().BeNull();
    }

    [Fact]
    public void PercentileRank_counts_peers_below_and_half_the_ties()
    {
        CompsMath.PercentileRank(12m, [8m, 10m, 14m, 16m]).Should().Be(50m);
        CompsMath.PercentileRank(12m, [12m, 12m, 12m, 12m]).Should().Be(50m);
        CompsMath.PercentileRank(1m, [8m, 10m, 14m]).Should().Be(0m);
        CompsMath.PercentileRank(99m, [8m, 10m, 14m]).Should().Be(100m);
        CompsMath.PercentileRank(10m, []).Should().BeNull();
    }

    [Fact]
    public void PerShareFromEnterpriseValue_bridges_through_net_debt()
    {
        CompsMath.PerShareFromEnterpriseValue(1200m, 200m, 10m).Should().Be(100m);
        CompsMath.PerShareFromEnterpriseValue(1200m, -300m, 10m).Should().Be(150m, "net cash adds to equity");
    }

    [Fact]
    public void PerShareFromEnterpriseValue_is_null_without_shares_or_equity()
    {
        CompsMath.PerShareFromEnterpriseValue(1200m, 200m, 0m).Should().BeNull();
        CompsMath.PerShareFromEnterpriseValue(100m, 200m, 10m).Should().BeNull("a negative equity value is not a share price");
    }
}
