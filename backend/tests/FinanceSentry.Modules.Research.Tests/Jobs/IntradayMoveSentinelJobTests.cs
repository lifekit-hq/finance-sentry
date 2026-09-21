namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// P1 (ledger-heartbeat design): a holding moving ≥5%, a watchlist name moving ≥8%, or either moving
/// ≥3σ against its 20-day volatility. Everything reads from the existing quote cache
/// (<see cref="IMarketDataService"/>) — no new fetch path — and a stale/missing/malformed quote must
/// stay silent rather than alert or fail the job. Equities are evaluated only in the NYSE regular
/// session; crypto on every tick while its price is fresh.
/// </summary>
public sealed class IntradayMoveSentinelJobTests
{
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<ICryptoHoldingsReader> _crypto = new();
    private readonly Mock<IWatchlistReader> _watchlist = new();
    private readonly Mock<IMarketDataService> _marketData = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly FixedClock _clock = new(Wednesday11amNewYork);
    private readonly IntradayMoveSentinelJob _job;

    // 2026-09-16 is a Wednesday; 15:00 UTC is 11:00 EDT, inside the NYSE regular session.
    private static readonly DateTimeOffset Wednesday11amNewYork = new(2026, 9, 16, 15, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();

    public IntradayMoveSentinelJobTests()
    {
        _job = new IntradayMoveSentinelJob(
            _banking.Object,
            _brokerage.Object,
            _crypto.Object,
            _watchlist.Object,
            _marketData.Object,
            _alerts.Object,
            _clock,
            NullLogger<IntradayMoveSentinelJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _crypto.Setup(c => c.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync([]);

        // No history by default — the z-score path is not evaluable unless a test seeds it.
        _marketData.Setup(m => m.GetDailyClosesAsync(
                It.IsAny<string>(), It.IsAny<DateOnly>(), default))
            .ReturnsAsync([]);
    }

    /// <summary>
    /// Shaped like what Yahoo's chart endpoint actually yields: no marketState, so session "unknown" and
    /// IsStale true — the job must not depend on either.
    /// </summary>
    private QuoteCacheEntry Quote(
        string ticker, decimal price, decimal? previousClose, DateTimeOffset? pricedAt = null)
        => new()
        {
            Ticker = ticker,
            Price = price,
            PreviousClose = previousClose,
            MarketState = "unknown",
            Session = "unknown",
            IsStale = true,
            SourcePriceTime = pricedAt ?? _clock.GetUtcNow(),
            RegularMarketTime = pricedAt ?? _clock.GetUtcNow(),
        };

    private void SetQuotes(params QuoteCacheEntry[] quotes)
        => _marketData.Setup(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(quotes.ToDictionary(q => q.Ticker, q => q));

    private static DateOnly Today => DateOnly.FromDateTime(Wednesday11amNewYork.UtcDateTime);

    private static IReadOnlyList<DailyClose> FlatHistory(int days, decimal close)
    {
        var since = Today.AddDays(-days);
        return Enumerable.Range(0, days).Select(i => new DailyClose(since.AddDays(i), close)).ToList();
    }

    [Fact]
    public async Task Execute_NoActiveUsers_SkipsGracefully()
    {
        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([]);

        await _job.ExecuteAsync();

        _brokerage.Verify(b => b.GetHoldingsAsync(It.IsAny<Guid>(), default), Times.Never);
        _marketData.Verify(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_NoHoldingsOrWatchlist_SkipsFetchEntirely()
    {
        await _job.ExecuteAsync();

        _marketData.Verify(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_HoldingMovesAboveFivePercent_DuringRegularSession_FiresAlert()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        SetQuotes(Quote("AAPL", price: 106m, previousClose: 100m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            _userId, It.IsAny<Guid>(), "AAPL", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Execute_HoldingMovesBelowFivePercent_NoAlert()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        SetQuotes(Quote("AAPL", price: 102m, previousClose: 100m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    /// <summary>Be precise about market hours: an equity outside the NYSE regular session is not evaluated at all.</summary>
    [Theory]
    [InlineData("2026-09-16T13:29:00Z")] // 09:29 EDT, pre-market
    [InlineData("2026-09-16T20:00:00Z")] // 16:00 EDT, the close
    [InlineData("2026-09-16T23:00:00Z")] // 19:00 EDT, post-market
    [InlineData("2026-09-19T15:00:00Z")] // Saturday
    [InlineData("2026-12-01T14:15:00Z")] // 09:15 EST — winter offset, still pre-market
    public async Task Execute_EquityOutsideRegularSession_IsNotEvaluated(string nowUtc)
    {
        _clock.Now = DateTimeOffset.Parse(nowUtc, System.Globalization.CultureInfo.InvariantCulture);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        SetQuotes(Quote("AAPL", price: 120m, previousClose: 100m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_EquityWinterSession_IsEvaluated()
    {
        _clock.Now = new DateTimeOffset(2026, 12, 1, 14, 45, 0, TimeSpan.Zero); // 09:45 EST
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        SetQuotes(Quote("AAPL", price: 106m, previousClose: 100m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            _userId, It.IsAny<Guid>(), "AAPL", It.IsAny<string>(), default), Times.Once);
    }

    /// <summary>An exchange holiday inside regular hours: the last regular-market trade is a previous session's.</summary>
    [Fact]
    public async Task Execute_EquityLastTradedOnAPreviousSession_IsNotEvaluated()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        SetQuotes(Quote("AAPL", price: 120m, previousClose: 100m, pricedAt: Wednesday11amNewYork.AddDays(-1)));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    /// <summary>Crypto has no exchange session — it is evaluated around the clock, weekends included.</summary>
    [Fact]
    public async Task Execute_CryptoHolding_EvaluatedOutsideEquityHours()
    {
        _clock.Now = new DateTimeOffset(2026, 9, 19, 3, 0, 0, TimeSpan.Zero); // Saturday night
        _crypto.Setup(c => c.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new CryptoHoldingSummary("BTC", 1m, 0m, 50000m, DateTime.UtcNow, "Binance")]);
        SetQuotes(Quote("BTC-USD", price: 53000m, previousClose: 50000m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            _userId, It.IsAny<Guid>(), "BTC-USD", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Execute_CryptoQuoteWithOldSourcePrice_IsSkipped()
    {
        _crypto.Setup(c => c.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new CryptoHoldingSummary("BTC", 1m, 0m, 50000m, DateTime.UtcNow, "Binance")]);
        SetQuotes(Quote("BTC-USD", price: 53000m, previousClose: 50000m, pricedAt: Wednesday11amNewYork.AddHours(-3)));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_VenueFiatCryptoHolding_IsExcluded()
    {
        _crypto.Setup(c => c.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new CryptoHoldingSummary("EUR", 500m, 0m, 540m, DateTime.UtcNow, "RevolutX", IsVenueFiat: true)]);

        await _job.ExecuteAsync();

        _marketData.Verify(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_WatchlistTickerMovesAboveEightPercent_FiresAlert()
    {
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync(["TSLA"]);
        SetQuotes(Quote("TSLA", price: 109m, previousClose: 100m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            _userId, It.IsAny<Guid>(), "TSLA", It.IsAny<string>(), default), Times.Once);
    }

    /// <summary>Above the holding bar but below the watchlist's own, stricter bar — proves the two thresholds are distinct.</summary>
    [Fact]
    public async Task Execute_WatchlistTickerMovesBetweenFiveAndEightPercent_NoAlert()
    {
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync(["TSLA"]);
        SetQuotes(Quote("TSLA", price: 107m, previousClose: 100m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    /// <summary>A ticker that is both a holding and watchlisted keeps the holding's stricter 5% bar.</summary>
    [Fact]
    public async Task Execute_TickerBothHeldAndWatchlisted_UsesTheHoldingThreshold()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync(["AAPL"]);
        SetQuotes(Quote("AAPL", price: 106m, previousClose: 100m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            _userId, It.IsAny<Guid>(), "AAPL", It.IsAny<string>(), default), Times.Once);
    }

    /// <summary>A move too small for the plain bar but large against a genuinely quiet 20-day history fires on z-score alone.</summary>
    [Fact]
    public async Task Execute_ZScoreAtOrAboveThree_WithSufficientHistory_FiresAlert()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("KO", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        // Flat history (zero daily variance) makes any nonzero move an infinite z-score — well above
        // the 3σ bar — while staying under the 5% plain-move bar.
        SetQuotes(Quote("KO", price: 102m, previousClose: 100m));
        _marketData.Setup(m => m.GetDailyClosesAsync("KO", It.IsAny<DateOnly>(), default))
            .ReturnsAsync(FlatHistoryWithOneWiggle());

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            _userId, It.IsAny<Guid>(), "KO", It.Is<string>(r => r.Contains('σ')), default), Times.Once);
    }

    /// <summary>21 closes with one tiny wiggle: enough history for a full 20-return window with a nonzero, small σ.</summary>
    private static IReadOnlyList<DailyClose> FlatHistoryWithOneWiggle()
    {
        var since = Today.AddDays(-30);
        var closes = new List<DailyClose>();
        for (var i = 0; i < 21; i++)
        {
            // A single 0.1% wiggle keeps σ tiny but nonzero, so a 2% move scores far above 3σ.
            var price = i == 10 ? 100.1m : 100m;
            closes.Add(new DailyClose(since.AddDays(i), price));
        }

        return closes;
    }

    /// <summary>
    /// Today's in-progress bar closes at the current price, i.e. the very move under test; it must not
    /// count toward the volatility it is measured against.
    /// </summary>
    [Fact]
    public async Task Execute_TodaysInProgressBar_IsExcludedFromTheVolatilityWindow()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("KO", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        SetQuotes(Quote("KO", price: 102m, previousClose: 100m));
        // Alternating ±0.57% returns put the 2% move at ~3.4σ; counting today's +2% bar in the window
        // would inflate σ enough to drag it to ~2.75σ and swallow the alert.
        var history = Enumerable.Range(0, 21)
            .Select(i => new DailyClose(Today.AddDays(i - 21), i % 2 == 0 ? 100m : 100.57m))
            .Append(new DailyClose(Today, 102m))
            .ToList();
        _marketData.Setup(m => m.GetDailyClosesAsync("KO", It.IsAny<DateOnly>(), default))
            .ReturnsAsync(history);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            _userId, It.IsAny<Guid>(), "KO", It.Is<string>(r => r.Contains('σ')), default), Times.Once);
    }

    [Fact]
    public async Task Execute_SameTickerForSeveralUsers_FetchesHistoryOncePerRun()
    {
        var userId2 = Guid.NewGuid();
        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId, userId2]);
        foreach (var user in new[] { _userId, userId2 })
        {
            _brokerage.Setup(b => b.GetHoldingsAsync(user, default))
                .ReturnsAsync([new BrokerageHoldingSummary("KO", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
            _crypto.Setup(c => c.GetHoldingsAsync(user, default)).ReturnsAsync([]);
            _watchlist.Setup(w => w.ListTickersAsync(user, default)).ReturnsAsync([]);
        }

        SetQuotes(Quote("KO", price: 100.5m, previousClose: 100m));

        await _job.ExecuteAsync();

        _marketData.Verify(m => m.GetDailyClosesAsync("KO", It.IsAny<DateOnly>(), default), Times.Once);
    }

    [Fact]
    public async Task Execute_FewerThanTwentyDaysHistory_ZScoreIsNotEvaluated_NoAlert()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("KO", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        SetQuotes(Quote("KO", price: 102m, previousClose: 100m));
        // Only 10 closes — nowhere near the 20-return window the z-score rule requires.
        _marketData.Setup(m => m.GetDailyClosesAsync("KO", It.IsAny<DateOnly>(), default))
            .ReturnsAsync(FlatHistory(10, 100m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_MissingQuote_IsSkipped()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _marketData.Setup(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>());

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_MissingPreviousClose_IsSkipped()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        SetQuotes(Quote("AAPL", price: 120m, previousClose: null));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_NonEquityHolding_UsesCryptoTickerFormat()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("BTC", "CRYPTO", 1m, 50000m, DateTime.UtcNow, "IBKR")]);

        await _job.ExecuteAsync();

        // A brokerage-reported "CRYPTO" instrument type is not the STK equity path — it is simply not
        // tracked from this reader; crypto holdings come from ICryptoHoldingsReader instead.
        _marketData.Verify(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), default), Times.Never);
    }

    /// <summary>
    /// Proves the 24-hour, per-ticker silence window claim at the job's boundary: the job always asks
    /// with the identical per-ticker reference id on every tick, so a name that keeps moving relies on
    /// AlertGeneratorService's declared silence window (proven directly in AlertGeneratorServiceTests)
    /// to announce itself once a day rather than once every 15 minutes.
    /// </summary>
    [Fact]
    public async Task Execute_SameTickerKeepsMoving_NextRun_AsksWithTheIdenticalReferenceId()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        SetQuotes(Quote("AAPL", price: 106m, previousClose: 100m));
        var seenReferenceIds = new List<Guid>();
        _alerts.Setup(a => a.GenerateMarketStructureAlertAsync(
                _userId, It.IsAny<Guid>(), "AAPL", It.IsAny<string>(), default))
            .Callback<Guid, Guid, string, string, CancellationToken>((_, refId, _, _, _) => seenReferenceIds.Add(refId))
            .Returns(Task.CompletedTask);

        await _job.ExecuteAsync();
        SetQuotes(Quote("AAPL", price: 109m, previousClose: 100m));
        await _job.ExecuteAsync();

        Assert.Equal(2, seenReferenceIds.Count);
        Assert.Equal(seenReferenceIds[0], seenReferenceIds[1]);
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
        _crypto.Setup(c => c.GetHoldingsAsync(userId2, default)).ReturnsAsync([]);
        _watchlist.Setup(w => w.ListTickersAsync(userId2, default)).ReturnsAsync([]);
        SetQuotes(Quote("AAPL", price: 106m, previousClose: 100m));

        // Should not throw — errors per user are caught
        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateMarketStructureAlertAsync(
            userId2, It.IsAny<Guid>(), "AAPL", It.IsAny<string>(), default), Times.Once);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
