namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FluentAssertions;
using Xunit;

public class AssetChartRebaseTests
{
    private static readonly DateOnly Friday = new(2026, 10, 9);

    [Fact]
    public void Rebase_StartsTheBenchmarkAtTheAssetsFirstClose()
    {
        var asset = History((0, 50m), (3, 60m));
        var benchmark = History((0, 400m), (3, 440m));

        var points = GetAssetChartQueryHandler.Rebase(asset, benchmark);

        points.Select(p => p.Benchmark).Should().Equal(50m, 55m);
        points.Select(p => p.Close).Should().Equal(50m, 60m);
    }

    [Fact]
    public void Rebase_CarriesTheBenchmarkOverDaysItDidNotTrade()
    {
        // Crypto trades on the weekend; the S&P 500 does not.
        var asset = History((0, 100m), (1, 101m), (2, 102m), (3, 103m));
        var benchmark = History((0, 400m), (3, 404m));

        var points = GetAssetChartQueryHandler.Rebase(asset, benchmark);

        points.Select(p => p.Benchmark).Should().Equal(100m, 100m, 100m, 101m);
    }

    [Fact]
    public void Rebase_BenchmarkStartingLater_LeavesTheEarlyPointsEmpty()
    {
        var asset = History((0, 100m), (1, 110m));
        var benchmark = History((1, 400m));

        var points = GetAssetChartQueryHandler.Rebase(asset, benchmark);

        points[0].Benchmark.Should().BeNull();
        points[1].Benchmark.Should().Be(110m, "the first benchmark close is rebased to the asset's close on that date");
    }

    [Fact]
    public void Rebase_BenchmarkStartingLater_TracksTheAssetFromItsFirstDate()
    {
        var asset = History((0, 10m), (1, 100m), (2, 120m));
        var benchmark = History((1, 400m), (2, 440m));

        var points = GetAssetChartQueryHandler.Rebase(asset, benchmark);

        points.Select(p => p.Benchmark).Should().Equal(null, 100m, 110m);
    }

    [Fact]
    public void BenchmarkChangePct_BenchmarkCoversTheWindow_IsItsChange()
    {
        var points = GetAssetChartQueryHandler.Rebase(History((0, 50m), (3, 60m)), History((0, 400m), (3, 440m)));

        GetAssetChartQueryHandler.BenchmarkChangePct(points).Should().Be(10m);
    }

    [Fact]
    public void BenchmarkChangePct_BenchmarkStartingLater_IsNullRatherThanAShorterWindow()
    {
        var points = GetAssetChartQueryHandler.Rebase(History((0, 10m), (1, 100m), (2, 120m)), History((1, 400m), (2, 440m)));

        GetAssetChartQueryHandler.BenchmarkChangePct(points).Should().BeNull();
    }

    [Fact]
    public void Rebase_NoAssetHistory_IsEmpty()
    {
        GetAssetChartQueryHandler.Rebase(PriceHistory.Empty, History((0, 400m))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("1y", "1Y")]
    [InlineData(" ytd ", "YTD")]
    [InlineData("max", "MAX")]
    [InlineData("2W", null)]
    [InlineData(null, null)]
    public void ChartRange_Normalize(string? input, string? expected)
    {
        ChartRange.Normalize(input).Should().Be(expected);
    }

    private static PriceHistory History(params (int Day, decimal Close)[] closes)
        => new("USD", closes.Select(c => new DailyClose(Friday.AddDays(c.Day), c.Close)).ToList());
}
