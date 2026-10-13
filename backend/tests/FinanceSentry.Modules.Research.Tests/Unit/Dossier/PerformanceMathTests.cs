namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using FinanceSentry.Modules.Research.Domain.Dossier;
using FluentAssertions;
using Xunit;

public class PerformanceMathTests
{
    private static readonly DateOnly Start = new(2025, 10, 13);

    [Theory]
    [InlineData(new double[] { }, null)]
    [InlineData(new double[] { 100 }, null)]
    [InlineData(new double[] { 0, 100 }, null)]
    [InlineData(new double[] { 100, 122.2 }, 22.2)]
    [InlineData(new double[] { 200, 150 }, -25.0)]
    public void ChangePct_FirstToLast(double[] closes, double? expected)
    {
        PerformanceMath.ChangePct(closes.Select(c => (decimal)c).ToList())
            .Should().Be(expected is null ? null : (decimal)expected.Value);
    }

    [Fact]
    public void Build_ReadsTheHighLowAndDistanceFromHigh()
    {
        var closes = new[] { 100m, 140m, 90m, 133m }.Select((c, i) => (Start.AddDays(i), c)).ToList();

        var performance = PerformanceMath.Build(closes, [400m, 420m], "USD");

        performance.LastClose.Should().Be(133m);
        performance.YearChangePct.Should().Be(33m);
        performance.High52Week.Should().Be(140m);
        performance.Low52Week.Should().Be(90m);
        performance.FromHighPct.Should().Be(-5m);
        performance.BenchmarkYearChangePct.Should().Be(5m);
        performance.Currency.Should().Be("USD");
        performance.AsOf.Should().Be(Start.AddDays(3));
    }

    [Fact]
    public void Build_SubCentPrices_KeepTheirSignificantDigits()
    {
        var closes = new[] { 0.000012m, 0.0000135m, 0.0000118m }.Select((c, i) => (Start.AddDays(i), c)).ToList();

        var performance = PerformanceMath.Build(closes, [], "USD");

        performance.LastClose.Should().Be(0.0000118m);
        performance.High52Week.Should().Be(0.0000135m);
        performance.Low52Week.Should().Be(0.0000118m);
    }

    [Fact]
    public void Build_NoCloses_KeepsOnlyTheBenchmark()
    {
        var performance = PerformanceMath.Build([], [400m, 440m], null);

        performance.LastClose.Should().BeNull();
        performance.YearChangePct.Should().BeNull();
        performance.AsOf.Should().BeNull();
        performance.BenchmarkYearChangePct.Should().Be(10m);
    }
}
