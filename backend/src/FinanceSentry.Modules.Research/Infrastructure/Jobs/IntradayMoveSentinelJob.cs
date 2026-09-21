namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hangfire job (P1, ledger-heartbeat design), every 15 minutes: raises a MarketStructure alert when a
/// held ticker moves ≥5%, a watchlist ticker moves ≥8%, or either moves ≥3σ against its 20-day daily
/// return volatility. Prices come from <see cref="IMarketDataService.GetQuotesAsync"/>, the existing
/// 5-minute Yahoo quote cache — no new live-price fetch path. Yahoo's chart endpoint does not report a
/// <c>marketState</c>, so market hours are an explicit NYSE regular-session check (weekday,
/// 09:30–16:00 America/New_York) plus the quote's own <see cref="QuoteCacheEntry.RegularMarketTime"/>
/// being from today's session (which keeps exchange holidays quiet); a held crypto asset (no exchange
/// session) is evaluated on every tick as long as its price is recent. A missing, stale or malformed
/// quote is silently skipped — never an alert, never a job failure — since Yahoo's quote endpoint has
/// no contract. The 20-day volatility check needs <see cref="IMarketDataService.GetDailyClosesAsync"/>
/// (already used elsewhere in Research for historical closes) only when the plain move threshold did
/// not already fire, and each ticker's volatility is fetched at most once per run, shared across users.
/// Today's in-progress bar is excluded from the window so the move being tested never dampens its own
/// z-score. Fewer than 21 prior daily closes (20 returns) makes the z-score not evaluable — the job
/// never fires on a thin sample. Alerts ride the
/// existing <see cref="IAlertGeneratorService.GenerateMarketStructureAlertAsync"/> and its already
/// declared 24-hour, per-reference silence window — one per-ticker reference derived here, so a name
/// that keeps moving announces itself once a day, not once every 15 minutes. Rare by design
/// (~0.1 fires/day, per the design's observed volume).
/// </summary>
public sealed class IntradayMoveSentinelJob(
    IBankingTotalsReader banking,
    IBrokerageHoldingsReader brokerage,
    ICryptoHoldingsReader crypto,
    IWatchlistReader watchlist,
    IMarketDataService marketData,
    IAlertGeneratorService alerts,
    TimeProvider clock,
    ILogger<IntradayMoveSentinelJob> logger)
{
    private const string EquityInstrumentType = "STK";

    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeSpan RegularOpen = new(9, 30, 0);
    private static readonly TimeSpan RegularClose = new(16, 0, 0);

    // A crypto quote whose source price is older than this is stale — Yahoo prices crypto continuously.
    private static readonly TimeSpan CryptoMaxPriceAge = TimeSpan.FromHours(1);

    private const decimal HoldingMoveThreshold = 0.05m;
    private const decimal WatchlistMoveThreshold = 0.08m;
    private const decimal ZScoreThreshold = 3.0m;

    // 20 daily returns need 21 closes; the extra calendar-day buffer absorbs weekends/holidays.
    private const int VolatilityWindow = 20;
    private const int HistoryLookbackDays = 45;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogDebug("IntradayMoveSentinel: no active users found, skipping.");
            return;
        }

        var volatility = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, volatility, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IntradayMoveSentinel: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, Dictionary<string, decimal?> volatility, CancellationToken ct)
    {
        var tracked = await BuildTrackedTickersAsync(userId, ct);
        if (tracked.Count == 0)
        {
            return;
        }

        var quotes = await marketData.GetQuotesAsync(tracked.Keys, ct);

        foreach (var (ticker, tracker) in tracked)
        {
            if (!quotes.TryGetValue(ticker, out var quote))
            {
                continue;
            }

            await EvaluateAsync(userId, tracker, quote, volatility, ct);
        }
    }

    private async Task<Dictionary<string, TrackedTicker>> BuildTrackedTickersAsync(Guid userId, CancellationToken ct)
    {
        // Uppercase throughout — IMarketDataService.GetQuotesAsync always normalizes and returns its
        // dictionary keyed by the upper-invariant ticker, so tracking keys must match that exactly.
        var tracked = new Dictionary<string, TrackedTicker>(StringComparer.Ordinal);

        foreach (var holding in await brokerage.GetHoldingsAsync(userId, ct))
        {
            if (string.Equals(holding.InstrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase))
            {
                var ticker = holding.Symbol.ToUpperInvariant();
                tracked[ticker] = new TrackedTicker(ticker, IsEquity: true, MoveThreshold: HoldingMoveThreshold);
            }
        }

        foreach (var holding in await crypto.GetHoldingsAsync(userId, ct))
        {
            if (holding.IsVenueFiat)
            {
                continue;
            }

            var ticker = $"{holding.Asset.ToUpperInvariant()}-USD";
            tracked[ticker] = new TrackedTicker(ticker, IsEquity: false, MoveThreshold: HoldingMoveThreshold);
        }

        foreach (var rawTicker in await watchlist.ListTickersAsync(userId, ct))
        {
            var ticker = rawTicker.ToUpperInvariant();

            // A ticker already tracked as a holding keeps the holding's stricter threshold and
            // market-hours gating — the watchlist adds names that aren't already covered.
            tracked.TryAdd(ticker, new TrackedTicker(ticker, IsEquity: true, MoveThreshold: WatchlistMoveThreshold));
        }

        return tracked;
    }

    private async Task EvaluateAsync(
        Guid userId, TrackedTicker tracker, QuoteCacheEntry quote, Dictionary<string, decimal?> volatility,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var isLive = tracker.IsEquity ? IsInUsRegularSession(quote, now) : IsFreshCryptoQuote(quote, now);

        // Outside market hours, or a stale, missing or malformed quote, is an ordinary case, never a signal.
        if (!isLive || quote.PreviousClose is null or 0m || quote.Price <= 0m)
        {
            return;
        }

        var ticker = tracker.Ticker;
        var movePct = (quote.Price - quote.PreviousClose.Value) / quote.PreviousClose.Value;

        if (Math.Abs(movePct) >= tracker.MoveThreshold)
        {
            var thresholdKind = tracker.MoveThreshold == HoldingMoveThreshold ? "holding" : "watchlist";
            await RaiseAsync(
                userId, ticker,
                $"moved {movePct:P1} intraday, above the {thresholdKind} {tracker.MoveThreshold:P0} bar",
                ct);
            return;
        }

        // The z-score check needs a second Yahoo call for daily history — only made when the plain
        // move threshold didn't already resolve this tick, and at most once per ticker per run.
        if (!volatility.TryGetValue(ticker, out var stdDev))
        {
            stdDev = await ComputeStdDevAsync(ticker, DateOnly.FromDateTime(now.UtcDateTime), ct);
            volatility[ticker] = stdDev;
        }

        if (stdDev is not null && Math.Abs(movePct / stdDev.Value) >= ZScoreThreshold)
        {
            await RaiseAsync(
                userId, ticker,
                $"moved {movePct / stdDev.Value:0.0}σ vs its 20-day volatility (bar {ZScoreThreshold:0.0}σ)",
                ct);
        }
    }

    /// <summary>
    /// NYSE regular session (weekday, 09:30–16:00 New York time) and the quote's last regular-market
    /// trade is from today's New York date — on an exchange holiday that time is a previous session's.
    /// </summary>
    private static bool IsInUsRegularSession(QuoteCacheEntry quote, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, NewYork);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
            local.TimeOfDay < RegularOpen || local.TimeOfDay >= RegularClose)
        {
            return false;
        }

        return quote.RegularMarketTime is { } traded &&
            TimeZoneInfo.ConvertTime(traded, NewYork).Date == local.Date;
    }

    private static bool IsFreshCryptoQuote(QuoteCacheEntry quote, DateTimeOffset now)
        => quote.SourcePriceTime is { } priced && now - priced <= CryptoMaxPriceAge;

    private Task RaiseAsync(Guid userId, string ticker, string reason, CancellationToken ct)
        => alerts.GenerateMarketStructureAlertAsync(userId, ReferenceId(ticker), ticker, reason, ct);

    private async Task<decimal?> ComputeStdDevAsync(string ticker, DateOnly today, CancellationToken ct)
    {
        IReadOnlyList<DailyClose> closes;
        try
        {
            closes = await marketData.GetDailyClosesAsync(ticker, today.AddDays(-HistoryLookbackDays), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "IntradayMoveSentinel: daily-close history fetch failed for {Ticker}", ticker);
            return null;
        }

        return StdDev(closes.Where(c => c.Date < today).ToList());
    }

    /// <summary>
    /// Sample standard deviation of the last <see cref="VolatilityWindow"/> daily returns. Explicitly not
    /// evaluable — returns null, never a fabricated figure — when there isn't a full window of history
    /// yet (a new listing, a fetch that came back thin) or the window is degenerate (zero volatility).
    /// </summary>
    private static decimal? StdDev(IReadOnlyList<DailyClose> closes)
    {
        if (closes.Count < VolatilityWindow + 1)
        {
            return null;
        }

        var ordered = closes.OrderBy(c => c.Date).Select(c => c.Close).ToArray();
        var returns = new List<decimal>(ordered.Length - 1);
        for (var i = 1; i < ordered.Length; i++)
        {
            var prev = ordered[i - 1];
            if (prev != 0m)
            {
                returns.Add((ordered[i] - prev) / prev);
            }
        }

        if (returns.Count < VolatilityWindow)
        {
            return null;
        }

        var slice = returns.Skip(returns.Count - VolatilityWindow).ToArray();
        var mean = slice.Average();
        var variance = slice.Sum(r => (r - mean) * (r - mean)) / (VolatilityWindow - 1);
        var stdDev = (decimal)Math.Sqrt((double)variance);

        return stdDev == 0m ? null : stdDev;
    }

    // Stable per-ticker reference id, independent of the nightly Radar unusual-move checker's own
    // reference — this detector's 24h silence window tracks its own trigger history per ticker.
    private static Guid ReferenceId(string ticker)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"intraday-move:{ticker.ToUpperInvariant()}")));

    private sealed record TrackedTicker(string Ticker, bool IsEquity, decimal MoveThreshold);
}
