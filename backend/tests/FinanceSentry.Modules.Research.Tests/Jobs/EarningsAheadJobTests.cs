namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// T3 (ledger-heartbeat design): earnings/ex-dividend within the lookahead window on a holding or
/// watchlist ticker. Yahoo quoteSummary has no contract, so a missing field, a null date, or a failed
/// fetch must produce silence, never an alert storm or a thrown exception.
/// </summary>
public sealed class EarningsAheadJobTests
{
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<IWatchlistReader> _watchlist = new();
    private readonly Mock<IEarningsCalendarService> _earningsCalendar = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly EarningsAheadJob _job;

    private readonly Guid _userId = Guid.NewGuid();

    public EarningsAheadJobTests()
    {
        _job = new EarningsAheadJob(
            _banking.Object,
            _brokerage.Object,
            _watchlist.Object,
            _earningsCalendar.Object,
            _alerts.Object,
            NullLogger<EarningsAheadJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync([]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
    }

    [Fact]
    public async Task Execute_NoActiveUsers_SkipsGracefully()
    {
        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([]);

        await _job.ExecuteAsync();

        _brokerage.Verify(b => b.GetHoldingsAsync(It.IsAny<Guid>(), default), Times.Never);
        _earningsCalendar.Verify(e => e.GetForTickersAsync(
            It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
            It.IsAny<string?>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_NoHoldingsOrWatchlist_SkipsFetchEntirely()
    {
        await _job.ExecuteAsync();

        _earningsCalendar.Verify(e => e.GetForTickersAsync(
            It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
            It.IsAny<string?>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_HoldingWithEarningsInThreeDays_EmitsOnce()
    {
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _earningsCalendar.Setup(e => e.GetForTickersAsync(
                It.Is<IReadOnlyCollection<string>>(t => t.Contains("AAPL")),
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default))
            .ReturnsAsync([new EarningsEvent("AAPL", EarningsEventType.Earnings, eventDate, false, "yahoo")]);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateEarningsAheadAlertAsync(
            _userId, "AAPL", EarningsAheadEventType.Earnings, eventDate, false, default), Times.Once);
    }

    [Fact]
    public async Task Execute_WatchlistTickerWithExDividend_Emits()
    {
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync(["KO"]);
        _earningsCalendar.Setup(e => e.GetForTickersAsync(
                It.Is<IReadOnlyCollection<string>>(t => t.Contains("KO")),
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default))
            .ReturnsAsync([new EarningsEvent("KO", EarningsEventType.ExDividend, eventDate, false, "yahoo")]);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateEarningsAheadAlertAsync(
            _userId, "KO", EarningsAheadEventType.ExDividend, eventDate, false, default), Times.Once);
    }

    /// <summary>
    /// The job asks the same (ticker, event type, event date) key on every run — the actual "never
    /// twice" dedup lives in <c>AlertGeneratorService</c> (covered there), but that only holds if the
    /// job keeps feeding it the same identifying arguments as the event stays in the lookahead window.
    /// </summary>
    [Fact]
    public async Task Execute_SameHolding_NextRun_AsksForTheIdenticalEvent()
    {
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _earningsCalendar.Setup(e => e.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default))
            .ReturnsAsync([new EarningsEvent("AAPL", EarningsEventType.Earnings, eventDate, false, "yahoo")]);

        await _job.ExecuteAsync();
        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateEarningsAheadAlertAsync(
            _userId, "AAPL", EarningsAheadEventType.Earnings, eventDate, false, default), Times.Exactly(2));
    }

    [Fact]
    public async Task Execute_NonEquityHolding_IsNotIncludedInTheFetch()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("BTC", "CRYPTO", 1m, 50000m, DateTime.UtcNow, "Binance")]);

        await _job.ExecuteAsync();

        _earningsCalendar.Verify(e => e.GetForTickersAsync(
            It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
            It.IsAny<string?>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_CalendarReturnsNoEvents_EmitsNothing()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _earningsCalendar.Setup(e => e.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default))
            .ReturnsAsync([]);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateEarningsAheadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.IsAny<bool>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_CalendarFetchThrows_EmitsNothingAndDoesNotThrow()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _earningsCalendar.Setup(e => e.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default))
            .ThrowsAsync(new HttpRequestException("Yahoo quoteSummary unreachable"));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateEarningsAheadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.IsAny<bool>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_DividendPaymentDate_IsNotEarningsOrExDividend_IsIgnored()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("KO", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _earningsCalendar.Setup(e => e.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default))
            .ReturnsAsync([new EarningsEvent(
                "KO", EarningsEventType.Dividend, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2), false, "yahoo")]);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateEarningsAheadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.IsAny<bool>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_UserThrows_ContinuesToNextUser()
    {
        var userId2 = Guid.NewGuid();
        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId, userId2]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ThrowsAsync(new InvalidOperationException("simulated error"));
        _brokerage.Setup(b => b.GetHoldingsAsync(userId2, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _watchlist.Setup(w => w.ListTickersAsync(userId2, default)).ReturnsAsync([]);
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        _earningsCalendar.Setup(e => e.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default))
            .ReturnsAsync([new EarningsEvent("AAPL", EarningsEventType.Earnings, eventDate, false, "yahoo")]);

        // Should not throw — errors per user are caught
        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateEarningsAheadAlertAsync(
            userId2, "AAPL", EarningsAheadEventType.Earnings, eventDate, false, default), Times.Once);
    }
}
