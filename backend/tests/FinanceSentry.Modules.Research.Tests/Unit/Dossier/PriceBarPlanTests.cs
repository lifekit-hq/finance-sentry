namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using FinanceSentry.Modules.Research.Application.Services;
using FluentAssertions;
using Xunit;

public class PriceBarPlanTests
{
    private const long Day = 86_400;

    // 2026-10-12 00:00 UTC, a Monday.
    private static readonly long Monday = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    [Fact]
    public void EveryPeriodOffersItsDefaultInterval()
    {
        foreach (var period in PricePeriod.Values)
        {
            PriceBarPlan.AllowedIntervals(period).Should().Contain(PriceBarPlan.DefaultInterval(period));
        }
    }

    [Fact]
    public void LongPeriods_OfferNoIntradayInterval()
    {
        foreach (var period in new[] { PricePeriod.ThreeMonths, PricePeriod.YearToDate, PricePeriod.OneYear, PricePeriod.FiveYears, PricePeriod.All })
        {
            PriceBarPlan.AllowedIntervals(period).Any(PriceInterval.IsIntraday).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("1y", "1Y")]
    [InlineData(" all ", "ALL")]
    [InlineData("ytd", "YTD")]
    public void Period_NormalizesCaseAndSpaces(string input, string expected)
        => PricePeriod.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("2W")]
    [InlineData("")]
    [InlineData(null)]
    public void Period_UnknownValue_IsNull(string? input)
        => PricePeriod.Normalize(input).Should().BeNull();

    [Theory]
    [InlineData("5m", "5m")]
    [InlineData("1H", "60m")]
    [InlineData("1w", "1wk")]
    [InlineData("1mo", "1mo")]
    [InlineData("1d", "1d")]
    public void Interval_MapsToTheSourceToken(string input, string source)
        => PriceInterval.ToSource(PriceInterval.Normalize(input)!).Should().Be(source);

    [Fact]
    public void SourceRange_IntradayStaysShortAndLongHistoryReachesBack()
    {
        PriceBarPlan.SourceRange(PricePeriod.OneDay, PriceInterval.FiveMinutes).Should().Be("5d");
        PriceBarPlan.SourceRange(PricePeriod.FiveDays, PriceInterval.OneHour).Should().Be("6mo");
        PriceBarPlan.SourceRange(PricePeriod.OneYear, PriceInterval.OneDay).Should().Be("2y");
        PriceBarPlan.SourceRange(PricePeriod.FiveYears, PriceInterval.OneDay).Should().Be("10y");
        PriceBarPlan.SourceRange(PricePeriod.All, PriceInterval.OneMonth).Should().Be("max");
    }

    [Fact]
    public void Window_OneDay_IsTheLastSession()
    {
        var bars = new List<PriceBar>();
        for (var day = 0; day < 3; day++)
        {
            for (var slot = 0; slot < 4; slot++)
            {
                bars.Add(Bar(Monday + (day * Day) + (slot * 300)));
            }
        }

        var (first, truncated) = PriceBarPlan.Window(bars, PricePeriod.OneDay, PriceInterval.FiveMinutes);

        first.Should().Be(8);
        truncated.Should().BeFalse();
    }

    [Fact]
    public void Window_FiveDays_IsTheLastFiveSessions_NotFiveCalendarDays()
    {
        // Mon-Fri, a weekend gap, then Mon-Wed: the last five sessions start the Thursday before.
        var days = new[] { 0, 1, 2, 3, 4, 7, 8, 9 };
        var bars = days.Select(d => Bar(Monday + (d * Day))).ToList();

        var (first, _) = PriceBarPlan.Window(bars, PricePeriod.FiveDays, PriceInterval.OneDay);

        first.Should().Be(3);
    }

    [Fact]
    public void Window_YearToDate_StartsAtJanuaryFirst()
    {
        var bars = Enumerable.Range(0, 420).Select(i => Bar(Monday - ((419 - i) * Day))).ToList();

        var (first, truncated) = PriceBarPlan.Window(bars, PricePeriod.YearToDate, PriceInterval.OneDay);

        bars[first].Date.Should().BeOnOrAfter(new DateOnly(2026, 1, 1));
        bars[first - 1].Date.Should().BeBefore(new DateOnly(2026, 1, 1));
        truncated.Should().BeFalse();
    }

    [Fact]
    public void Window_SeriesYoungerThanThePeriod_IsTruncated_AndAllIsNever()
    {
        var bars = Enumerable.Range(0, 30).Select(i => Bar(Monday - ((29 - i) * Day))).ToList();

        PriceBarPlan.Window(bars, PricePeriod.OneYear, PriceInterval.OneDay).Truncated.Should().BeTrue();
        PriceBarPlan.Window(bars, PricePeriod.OneMonth, PriceInterval.OneDay).Truncated.Should().BeFalse();
        PriceBarPlan.Window(bars, PricePeriod.All, PriceInterval.OneDay).Truncated.Should().BeFalse();
    }

    [Fact]
    public void Trim_KeepsAtMostTwoHundredBarsBeforeTheWindow()
    {
        var bars = Enumerable.Range(0, 900).Select(i => Bar(Monday - ((899 - i) * Day))).ToList();

        var (trimmed, windowStart, _) = PriceBarPlan.Trim(bars, PricePeriod.ThreeMonths, PriceInterval.OneDay);

        var windowIndex = trimmed.ToList().FindIndex(b => b.Time == windowStart);
        windowIndex.Should().Be(PriceBarPlan.WarmupBars);
        trimmed[^1].Should().Be(bars[^1]);
    }

    [Fact]
    public void Trim_ShortSeries_KeepsEverythingBeforeTheWindow()
    {
        var bars = Enumerable.Range(0, 100).Select(i => Bar(Monday - ((99 - i) * Day))).ToList();

        var (trimmed, _, _) = PriceBarPlan.Trim(bars, PricePeriod.OneMonth, PriceInterval.OneDay);

        trimmed.Should().HaveCount(100);
    }

    [Fact]
    public void Trim_NoBars_IsEmpty()
        => PriceBarPlan.Trim([], PricePeriod.OneYear, PriceInterval.OneDay).Bars.Should().BeEmpty();

    [Fact]
    public void WeekRange_UsesHighsAndLowsOfTheLastFiftyTwoWeeks()
    {
        var bars = new List<PriceBar>
        {
            Bar(Monday - (400 * Day), high: 999m, low: 1m),   // outside the year
            Bar(Monday - (100 * Day), high: 150m, low: 90m),
            Bar(Monday - (10 * Day), high: 130m, low: 70m),
            Bar(Monday, high: 120m, low: 100m),
        };

        var range = PriceBarPlan.WeekRange(bars);

        range.Should().NotBeNull();
        range!.Value.High.Should().Be(150m);
        range.Value.Low.Should().Be(70m);
        range.Value.CoversYear.Should().BeTrue();
    }

    [Fact]
    public void WeekRange_YoungSeries_ReportsTheRangeSinceItsFirstBar_AndSaysItIsPartial()
    {
        var bars = new List<PriceBar> { Bar(Monday - (30 * Day), high: 12m, low: 8m), Bar(Monday, high: 11m, low: 9m) };

        var range = PriceBarPlan.WeekRange(bars);

        range!.Value.From.Should().Be(bars[0].Date);
        range.Value.CoversYear.Should().BeFalse();
    }

    [Fact]
    public void WeekRange_NoBars_IsNull()
        => PriceBarPlan.WeekRange([]).Should().BeNull();

    private static PriceBar Bar(long time, decimal high = 11m, decimal low = 9m)
        => new(time, 0, 10m, high, low, 10m, 100);
}
