using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Utils;
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
    private readonly Mock<IHoldingsHistorySource> _holdings = new();

    public BookPerformanceServiceTests()
    {
        _portfolio.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), default))
            .ReturnsAsync([]);
        _holdings.Setup(h => h.GetBrokerageCapturesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), default))
            .ReturnsAsync([]);
    }

    private BookPerformanceService CreateSut() => new(_bars.Object, _portfolio.Object, _holdings.Object);

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

    private void SetupOneWeek(DateOnly since, DateOnly today, params DailyPortfolioValue[] values)
    {
        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([Bar("SPY", since, 400m), Bar("SPY", today, 404m)]);
        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync(values);
    }

    [Fact]
    public async Task GetAsync_DescribesTheBookSide_AsAValueChangeHeadline()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);
        SetupOneWeek(since, today, new DailyPortfolioValue(since, 100_000m), new DailyPortfolioValue(today, 103_000m));

        var period = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single();

        var book = period.Book!;
        book.Method.Should().Be(HeadlineMethod.ValueChange);
        book.Basis.Should().Be(HeadlineBasis.StartValue);
        book.Scope.Should().Be(HeadlineScope.InvestedBrokerage);
        book.Amount.Should().Be(3_000m);
        book.Percent.Should().Be(3.00m);
        book.Quality.Should().Be(HeadlineQuality.Complete);
        book.Reasons.Should().BeEmpty();
        book.Window.Should().Be(new HeadlineWindow(since, today));
        period.BookTwr.Should().Be(0.03m);
    }

    [Fact]
    public async Task GetAsync_BookHeadline_CarriesTheEndRowsSignals()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);
        SetupOneWeek(
            since, today,
            new DailyPortfolioValue(since, 100_000m, HeadlineSignals.FromSnapshot(null, true, (FxStatus?)null)),
            new DailyPortfolioValue(today, 103_000m, HeadlineSignals.FromSnapshot("brokerage", false, FxStatus.Exact)));

        var book = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single().Book!;

        book.Quality.Should().Be(HeadlineQuality.Degraded);
        book.Reasons.Should().Equal(
            new HeadlineReason(HeadlineReasonCode.StaleSource, "brokerage"),
            new HeadlineReason(HeadlineReasonCode.BackfilledEstimate),
            new HeadlineReason(HeadlineReasonCode.FxTodayRate));
        book.Amount.Should().Be(3_000m);
    }

    [Fact]
    public async Task GetAsync_BookHeadline_IsUnavailableWithShortHistory_WhenTheBookCannotBeMeasured()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);
        SetupOneWeek(since, today, new DailyPortfolioValue(today, 100_000m));

        var period = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single();

        period.BookTwr.Should().BeNull();
        period.Book!.Amount.Should().BeNull();
        period.Book.Percent.Should().BeNull();
        period.Book.Quality.Should().Be(HeadlineQuality.Unavailable);
        period.Book.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.ShortHistory));
    }

    [Fact]
    public async Task GetAsync_BookHeadline_KeepsTheChangeButNoPercent_FromAZeroStart()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);
        SetupOneWeek(since, today, new DailyPortfolioValue(since, 0m), new DailyPortfolioValue(today, 5_000m));

        var period = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single();

        period.BookTwr.Should().BeNull();
        period.Book!.Amount.Should().Be(5_000m);
        period.Book.Percent.Should().BeNull();
        period.Book.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.ZeroBase));
    }

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

    private static DailyBar BarWithClose(string ticker, DateOnly date, decimal close, decimal adjClose) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = ticker,
        Date = date,
        Open = close,
        High = close,
        Low = close,
        Close = close,
        AdjClose = adjClose,
        Volume = 1_000_000,
    };

    private static HoldingsCapture Capture(DateOnly day, params CapturedHolding[] holdings) =>
        new(new DateTimeOffset(day.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero), holdings);

    private void GivenCaptures(params HoldingsCapture[] captures) =>
        _holdings.Setup(h => h.GetBrokerageCapturesAsync(UserId, It.IsAny<DateOnly>(), default))
            .ReturnsAsync(captures);

    // Golden: 1W window, a deposit of 500 buys 5 more shares at an unchanged price of 100.
    //   Invested value 1,000 -> 1,500: the value change reads +50% (deposit counted as a gain).
    //   Holdings-based: revalue the earlier 10 shares at the later price 100 = 1,000; 1,000 / 1,000 - 1 = 0%.
    //   SPY: close 400 -> 410 = +2.5% price return; adjusted close 400 -> 420 would read +5% (dividends).
    //   Delta vs SPY price return = 0% - 2.5% = -2.5%.
    [Fact]
    public async Task GetAsync_HoldingsBasedReturnIsZero_AndSpyIsPriceReturn_ForADepositWithNoPriceChange()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                BarWithClose("SPY", since, 400m, 400m),
                BarWithClose("SPY", today, 410m, 420m),
            ]);
        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([
                new DailyPortfolioValue(since, 1_000m),
                new DailyPortfolioValue(today, 1_500m),
            ]);
        GivenCaptures(
            Capture(since, new CapturedHolding("AAA", 10m, 1_000m)),
            Capture(today, new CapturedHolding("AAA", 15m, 1_500m)));

        var period = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single();

        period.BookTwr.Should().Be(0.5m);
        period.HoldingsBasedReturn.Should().Be(0m);
        period.SpyPriceReturn.Should().Be(0.025m);
        period.SpyTwr.Should().Be(0.05m);
        period.HoldingsBasedDelta.Should().Be(-0.025m);
        period.HoldingsBasedUnavailableReason.Should().BeNull();
    }

    // Golden: 1M window, captures sit 2 days inside each edge (tolerance is 3 days), so the index covers
    // since+2 .. today-2. SPY close is measured between those dates: 410 -> 440 = +7.32%, not the nominal
    // since .. today span (400 -> 500 = +25%). Holdings: 10 shares 1,000 -> 1,100 = +10%; delta = +2.68%.
    [Fact]
    public async Task GetAsync_MeasuresSpyPriceReturnOverTheCaptureSpan_WhenCapturesSitInsideTheEdgeTolerance()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddMonths(-1);
        var firstCapture = since.AddDays(2);
        var lastCapture = today.AddDays(-2);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([
                BarWithClose("SPY", since, 400m, 400m),
                BarWithClose("SPY", firstCapture, 410m, 410m),
                BarWithClose("SPY", lastCapture, 440m, 440m),
                BarWithClose("SPY", today, 500m, 500m),
            ]);
        GivenCaptures(
            Capture(firstCapture, new CapturedHolding("AAA", 10m, 1_000m)),
            Capture(lastCapture, new CapturedHolding("AAA", 10m, 1_100m)));

        var period = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneMonth])).Periods.Single();

        period.HoldingsBasedReturn.Should().Be(0.1m);
        period.SpyPriceReturn.Should().Be(0.0732m);
        period.HoldingsBasedDelta.Should().Be(0.0268m);
    }

    // Golden: no captures at all -> a reason, never 0, while the value-change figure is untouched.
    [Fact]
    public async Task GetAsync_ReturnsInsufficientCapturesReason_WhenNoHoldingsWereCaptured()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([Bar("SPY", since, 400m), Bar("SPY", today, 404m)]);
        _portfolio.Setup(r => r.GetAsync(UserId, It.IsAny<DateOnly>(), today, default))
            .ReturnsAsync([new DailyPortfolioValue(since, 100m), new DailyPortfolioValue(today, 103m)]);

        var period = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single();

        period.BookTwr.Should().Be(0.03m);
        period.HoldingsBasedReturn.Should().BeNull();
        period.HoldingsBasedUnavailableReason.Should().Be(HoldingsReturnUnavailableReason.InsufficientCaptures);
    }

    // Golden: one capture inside the 1W window (the other is 20 days old) -> fewer than two captures.
    [Fact]
    public async Task GetAsync_IgnoresCapturesBeforeTheWindow()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([Bar("SPY", since, 400m), Bar("SPY", today, 404m)]);
        GivenCaptures(
            Capture(today.AddDays(-20), new CapturedHolding("AAA", 10m, 1_000m)),
            Capture(today, new CapturedHolding("AAA", 10m, 1_100m)));

        var period = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single();

        period.HoldingsBasedReturn.Should().BeNull();
        period.HoldingsBasedUnavailableReason.Should().Be(HoldingsReturnUnavailableReason.InsufficientCaptures);
    }

    // Golden: two captures, but the first sits 4 days after the window opens; the 1W edge tolerance is 1 day.
    [Fact]
    public async Task GetAsync_ReturnsWindowNotCoveredReason_WhenFirstCaptureIsPastTheEdgeTolerance()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([Bar("SPY", since, 400m), Bar("SPY", today, 404m)]);
        GivenCaptures(
            Capture(since.AddDays(4), new CapturedHolding("AAA", 10m, 1_000m)),
            Capture(today, new CapturedHolding("AAA", 10m, 1_100m)));

        var period = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single();

        period.HoldingsBasedReturn.Should().BeNull();
        period.HoldingsBasedUnavailableReason.Should().Be(HoldingsReturnUnavailableReason.WindowNotCovered);
    }

    // Golden: a held position (BBB, 5 shares) has no value at the later capture -> MissingPrice naming BBB.
    [Fact]
    public async Task GetAsync_ReturnsMissingPriceReasonAndSymbols_WhenAHeldPositionHasNoPrice()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);

        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([Bar("SPY", since, 400m), Bar("SPY", today, 404m)]);
        GivenCaptures(
            Capture(since, new CapturedHolding("AAA", 10m, 1_000m), new CapturedHolding("BBB", 5m, 500m)),
            Capture(today, new CapturedHolding("AAA", 10m, 1_100m), new CapturedHolding("BBB", 5m, 0m)));

        var period = (await CreateSut().GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single();

        period.HoldingsBasedReturn.Should().BeNull();
        period.HoldingsBasedUnavailableReason.Should().Be(HoldingsReturnUnavailableReason.MissingPrice);
        period.HoldingsBasedMissingPriceSymbols.Should().BeEquivalentTo(["BBB"]);
    }
}
