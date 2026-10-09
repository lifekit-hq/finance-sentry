using FluentAssertions;
using Moq;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Xunit;

namespace FinanceSentry.Tests.Unit.Radar;

public sealed class BookPerformanceServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IDailyBarRepository> _bars = new();
    private readonly Mock<IPortfolioValueSource> _portfolio = new();

    private BookPerformanceService CreateSut() => new(_bars.Object, _portfolio.Object);

    private static DailyBar Bar(string ticker, DateOnly date, decimal adjClose) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = ticker,
        Date = date,
        Open = adjClose,
        High = adjClose,
        Low = adjClose,
        Close = adjClose,
        AdjClose = adjClose,
        Volume = 1_000_000,
    };

    [Fact]
    public async Task GetAsync_ReturnsOutperform_WhenBookBeatsSpy()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", since, 400m),
                Bar("SPY", today, 404m),    // +1% SPY
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(since, 100_000m),
                new DailyPortfolioValue(today, 103_000m),  // +3% book
            ]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        result.Periods.Should().HaveCount(1);
        var period = result.Periods[0];
        period.Verdict.Should().Be("outperform");
        period.BookTwr.Should().BeApproximately(0.03m, 0.0001m);
        period.SpyTwr.Should().BeApproximately(0.01m, 0.0001m);
        period.Delta.Should().BeApproximately(0.02m, 0.0001m);
    }

    [Fact]
    public async Task GetAsync_ReturnsUnderperform_WhenSpyBeatsBook()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", since, 400m),
                Bar("SPY", today, 420m),    // +5% SPY
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(since, 100_000m),
                new DailyPortfolioValue(today, 102_000m),  // +2% book
            ]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        var period = result.Periods.Single();
        period.Verdict.Should().Be("underperform");
        period.Delta.Should().BeLessThan(0m);
    }

    [Fact]
    public async Task GetAsync_ReturnsInline_WhenDeltaBelowThreshold()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", since, 400m),
                Bar("SPY", today, 404m),   // +1%
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(since, 100_000m),
                new DailyPortfolioValue(today, 100_050m),  // +0.05% — within 0.1% threshold
            ]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        result.Periods.Single().Verdict.Should().Be("underperform");
    }

    [Fact]
    public async Task GetAsync_ReturnsPartialPeriod_WhenNoSpyBarsButPortfolioExists()
    {
        // SPY absent but book data present: still return the period so the caller can see book return.
        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([]);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);
        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(since, 100_000m),
                new DailyPortfolioValue(today, 103_000m),
            ]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        result.Periods.Should().HaveCount(1);
        result.Periods[0].SpyTwr.Should().BeNull();
        result.Periods[0].BookTwr.Should().BeApproximately(0.03m, 0.0001m);
        result.Periods[0].Verdict.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_OmitsPeriod_WhenBothSpyAndPortfolioMissing()
    {
        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), default))
            .ReturnsAsync([]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        result.Periods.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_OmitsPeriod_WhenNoPortfolioSnapshots()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", since, 400m),
                Bar("SPY", today, 404m),
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        // SPY-only: period is kept, book TWR is null, verdict is null
        result.Periods.Should().HaveCount(1);
        result.Periods[0].BookTwr.Should().BeNull();
        result.Periods[0].SpyTwr.Should().NotBeNull();
        result.Periods[0].Verdict.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_OmitsPeriod_WhenStartEqualsEnd()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([Bar("SPY", today, 400m)]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([new DailyPortfolioValue(today, 100_000m)]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        // A single bar gives start == end; both TWR are null ⇒ period omitted.
        result.Periods.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_ReturnsMultiplePeriods_WhenRequestedAndDataExists()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var week = today.AddDays(-7);
        var month = today.AddMonths(-1);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", month, 380m),
                Bar("SPY", week, 390m),
                Bar("SPY", today, 400m),
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(month, 90_000m),
                new DailyPortfolioValue(week, 95_000m),
                new DailyPortfolioValue(today, 100_000m),
            ]);

        var result = await CreateSut().GetAsync(UserId,
            [BookPerformancePeriod.OneWeek, BookPerformancePeriod.OneMonth]);

        result.Periods.Should().HaveCount(2);
        result.Periods.Select(p => p.Period).Should()
            .Contain(BookPerformancePeriod.OneWeek)
            .And.Contain(BookPerformancePeriod.OneMonth);
    }

    [Fact]
    public async Task GetAsync_TwrIsZero_WhenPricesUnchanged()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", since, 400m),
                Bar("SPY", today, 400m),  // flat
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(since, 100_000m),
                new DailyPortfolioValue(today, 100_000m),  // flat
            ]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        var period = result.Periods.Single();
        period.BookTwr.Should().Be(0m);
        period.SpyTwr.Should().Be(0m);
        period.Delta.Should().Be(0m);
        period.Verdict.Should().Be("inline");
    }

    [Fact]
    public async Task GetAsync_ReturnsSpyOnly_WhenOnlyOneInvestedDayInWindow()
    {
        // The source skips days with no cash/invested split, so one split day is all that is left:
        // the same "too little history" outcome as a single snapshot, not a new state.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", since, 400m),
                Bar("SPY", today, 404m),
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([new DailyPortfolioValue(today, 100_000m)]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        var period = result.Periods.Single();
        period.BookTwr.Should().BeNull();
        period.Delta.Should().BeNull();
        period.Verdict.Should().BeNull();
        period.SpyTwr.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAsync_MeasuresFirstToLastInvestedDay_WhenEarlierDaysHaveNoSplit()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstSplitDay = today.AddDays(-6);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([]);

        // Days before firstSplitDay are absent from the series; the return runs from the first one present.
        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(firstSplitDay, 80_000m),
                new DailyPortfolioValue(today, 84_000m),
            ]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        result.Periods.Single().BookTwr.Should().Be(0.05m);
    }

    [Theory]
    [InlineData(BookPerformancePeriod.OneWeek, 1, true)]
    [InlineData(BookPerformancePeriod.OneWeek, 2, false)]
    [InlineData(BookPerformancePeriod.OneMonth, 3, true)]
    [InlineData(BookPerformancePeriod.OneMonth, 4, false)]
    [InlineData(BookPerformancePeriod.OneYear, 7, true)]
    [InlineData(BookPerformancePeriod.OneYear, 8, false)]
    public async Task GetAsync_ComparesBookOnlyWhenFirstInvestedDayWithinToleranceOfPeriodStart(
        BookPerformancePeriod period, int daysAfterStart, bool compared)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = PeriodStart(today, period);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", since, 400m),
                Bar("SPY", today, 404m),
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(since.AddDays(daysAfterStart), 100_000m),
                new DailyPortfolioValue(today, 103_000m),
            ]);

        var result = await CreateSut().GetAsync(UserId, [period]);

        AssertBookCompared(result.Periods.Single(), compared);
    }

    [Theory]
    [InlineData(BookPerformancePeriod.OneWeek, 1, true)]
    [InlineData(BookPerformancePeriod.OneWeek, 2, false)]
    [InlineData(BookPerformancePeriod.OneMonth, 3, true)]
    [InlineData(BookPerformancePeriod.OneMonth, 4, false)]
    [InlineData(BookPerformancePeriod.OneYear, 7, true)]
    [InlineData(BookPerformancePeriod.OneYear, 8, false)]
    public async Task GetAsync_ComparesBookOnlyWhenLatestInvestedDayWithinToleranceOfToday(
        BookPerformancePeriod period, int daysBeforeToday, bool compared)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = PeriodStart(today, period);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", since, 400m),
                Bar("SPY", today, 404m),
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(since, 100_000m),
                new DailyPortfolioValue(today.AddDays(-daysBeforeToday), 103_000m),
            ]);

        var result = await CreateSut().GetAsync(UserId, [period]);

        AssertBookCompared(result.Periods.Single(), compared);
    }

    [Fact]
    public async Task GetAsync_LeavesBookUnavailable_WhenOneWeekBookHoldsOnlyTheLastDay()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                Bar("SPY", since, 400m),
                Bar("SPY", today, 404m),
            ]);

        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(today.AddDays(-1), 100_000m),
                new DailyPortfolioValue(today, 103_000m),
            ]);

        var result = await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek]);

        AssertBookCompared(result.Periods.Single(), compared: false);
    }

    private static DateOnly PeriodStart(DateOnly today, BookPerformancePeriod period) => period switch
    {
        BookPerformancePeriod.OneWeek => today.AddDays(-7),
        BookPerformancePeriod.OneMonth => today.AddMonths(-1),
        BookPerformancePeriod.OneYear => today.AddYears(-1),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
    };

    private static void AssertBookCompared(PeriodTwr period, bool compared)
    {
        period.SpyTwr.Should().Be(0.01m);
        if (compared)
        {
            period.BookTwr.Should().Be(0.03m);
            period.Verdict.Should().Be("outperform");
        }
        else
        {
            period.BookTwr.Should().BeNull();
            period.Delta.Should().BeNull();
            period.Verdict.Should().BeNull();
        }
    }
}
