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
/// 5-minute Yahoo quote cache — no new live-price fetch path. Market hours are read from the quote
/// itself rather than a second calendar definition: Yahoo's <c>marketState</c> is already surfaced as
/// <see cref="QuoteCacheEntry.Session"/>, so a US equity is only evaluated in the "regular" session,
/// while a held crypto asset (no exchange session) is evaluated on every tick. A missing, stale or
/// malformed quote is silently skipped — never an alert, never a job failure — since Yahoo's quote
/// endpoint has no contract. The 20-day volatility check needs <see cref="IMarketDataService.GetDailyClosesAsync"/>
/// (already used elsewhere in Research for historical closes) only when the plain move threshold did
/// not already fire, keeping the common case to the quote cache alone. Fewer than 21 daily closes (20
/// returns) makes the z-score not evaluable — the job never fires on a thin sample. Alerts ride the
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
    ILogger<IntradayMoveSentinelJob> logger)
{
    private const string EquityInstrumentType = "STK";

    // Yahoo's marketState for an equity in its normal trading session, as surfaced onto
    // QuoteCacheEntry.Session by YahooMarketDataService. A US equity outside this session (pre/post
    // market, closed, or unknown) is not evaluated at all — crypto has no session to check.
    private const string RegularSession = "regular";

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

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IntradayMoveSentinel: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, CancellationToken ct)
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

            await EvaluateAsync(userId, tracker, quote, ct);
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
            // session gating — the watchlist adds names that aren't already covered.
            tracked.TryAdd(ticker, new TrackedTicker(ticker, IsEquity: true, MoveThreshold: WatchlistMoveThreshold));
        }

        return tracked;
    }

    private async Task EvaluateAsync(Guid userId, TrackedTicker tracker, QuoteCacheEntry quote, CancellationToken ct)
    {
        // Yahoo's own session read is the market-hours signal — a US equity outside "regular" is not
        // evaluated at all. Crypto has no session to gate on and is evaluated around the clock.
        if (tracker.IsEquity && !string.Equals(quote.Session, RegularSession, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A stale, missing or malformed quote is an ordinary case, never a signal.
        if (quote.IsStale || quote.PreviousClose is null or 0m || quote.Price <= 0m)
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
        // move threshold didn't already resolve this tick, keeping the common case to the quote cache.
        var zScore = await ComputeZScoreAsync(ticker, movePct, ct);
        if (zScore is not null && Math.Abs(zScore.Value) >= ZScoreThreshold)
        {
            await RaiseAsync(
                userId, ticker,
                $"moved {zScore.Value:0.0}σ vs its 20-day volatility (bar {ZScoreThreshold:0.0}σ)",
                ct);
        }
    }

    private Task RaiseAsync(Guid userId, string ticker, string reason, CancellationToken ct)
        => alerts.GenerateMarketStructureAlertAsync(userId, ReferenceId(ticker), ticker, reason, ct);

    private async Task<decimal?> ComputeZScoreAsync(string ticker, decimal movePct, CancellationToken ct)
    {
        IReadOnlyList<DailyClose> closes;
        try
        {
            var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-HistoryLookbackDays);
            closes = await marketData.GetDailyClosesAsync(ticker, since, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "IntradayMoveSentinel: daily-close history fetch failed for {Ticker}", ticker);
            return null;
        }

        return ZScore(closes, movePct);
    }

    /// <summary>
    /// Sample z-score of <paramref name="movePct"/> against the standard deviation of the last
    /// <see cref="VolatilityWindow"/> daily returns. Explicitly not evaluable — returns null, never a
    /// fabricated score — when there isn't a full window of history yet (a new listing, a fetch that
    /// came back thin) or the window is degenerate (zero volatility).
    /// </summary>
    private static decimal? ZScore(IReadOnlyList<DailyClose> closes, decimal movePct)
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

        return stdDev == 0m ? null : movePct / stdDev;
    }

    // Stable per-ticker reference id, independent of the nightly Radar unusual-move checker's own
    // reference — this detector's 24h silence window tracks its own trigger history per ticker.
    private static Guid ReferenceId(string ticker)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"intraday-move:{ticker.ToUpperInvariant()}")));

    private sealed record TrackedTicker(string Ticker, bool IsEquity, decimal MoveThreshold);
}
