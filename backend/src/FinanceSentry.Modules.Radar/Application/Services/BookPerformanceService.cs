using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;

namespace FinanceSentry.Modules.Radar.Application.Services;

/// <summary>
/// Computes the change in invested value (includes deposits) for the invested brokerage book versus SPY's
/// adjusted-close change over configurable lookback windows. The book side is last / first - 1 over the
/// invested brokerage value (idle broker cash excluded) from net-worth snapshots. It is not a time-weighted
/// return: deposits and withdrawals are not adjusted for, so a contribution reads as a gain, and a purchase
/// made from idle broker cash reads as a gain too. The <c>PeriodTwr</c> / <c>BookTwr</c> / <c>SpyTwr</c> names
/// are kept for contract stability and are misleading; see docs/finance-framework.md (known gaps).
/// Beside it each period carries the holdings-based return (R1(b)): the brokerage book's flow-neutral price
/// return from <see cref="HoldingsBasedReturnCalculator"/>, against SPY's price return (close, not adjusted
/// close) over the same window, or the reason there is no number.
/// </summary>
public sealed class BookPerformanceService(
    IDailyBarRepository bars,
    IPortfolioValueSource portfolioValues,
    IHoldingsHistorySource holdingsHistory) : IBookPerformanceService
{
    private const string BenchmarkTicker = "SPY";

    private const decimal OutperformThreshold = 0.001m;

    private const decimal EdgeGapPeriodRatio = 0.1m;

    private const int MinEdgeGapDays = 1;

    private const int MaxEdgeGapDays = 7;

    public async Task<BookPerformanceResult> GetAsync(
        Guid userId,
        IReadOnlyList<BookPerformancePeriod> periods,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliest = periods.Select(p => StartDate(today, p)).Min();

        var spyBars = await bars.GetSinceAsync(BenchmarkTicker, earliest, ct);
        var portfolioSnapshots = await portfolioValues.GetAsync(userId, earliest, today, ct);
        var captures = await holdingsHistory.GetBrokerageCapturesAsync(userId, earliest, ct);

        var results = new List<PeriodTwr>(periods.Count);
        foreach (var period in periods)
        {
            var since = StartDate(today, period);
            var periodTwr = ComputePeriod(period, since, today, spyBars, portfolioSnapshots, captures);
            if (periodTwr is not null)
            {
                results.Add(periodTwr);
            }
        }

        return new BookPerformanceResult(results, today);
    }

    private static PeriodTwr? ComputePeriod(
        BookPerformancePeriod period,
        DateOnly since,
        DateOnly today,
        IReadOnlyList<Domain.DailyBar> spyBars,
        IReadOnlyList<DailyPortfolioValue> portfolioSnapshots,
        IReadOnlyList<HoldingsCapture> captures)
    {
        var spyTwr = ComputeSpyTwr(spyBars, since);
        var bookTwr = ComputeBookTwr(portfolioSnapshots, since, today);
        var holdings = ComputeHoldingsBasedReturn(captures, since, today);

        if (spyTwr is null && bookTwr is null && holdings.Value is null)
        {
            return null;
        }

        var spyPriceReturn = ComputeSpyPriceReturn(spyBars, since);
        decimal? holdingsDelta = holdings.Value is not null && spyPriceReturn is not null
            ? Math.Round(holdings.Value.Value - spyPriceReturn.Value, 4)
            : null;

        decimal? delta = bookTwr is not null && spyTwr is not null
            ? Math.Round(bookTwr.Value - spyTwr.Value, 4)
            : null;

        var verdict = delta switch
        {
            > OutperformThreshold => "outperform",
            < -OutperformThreshold => "underperform",
            not null => "inline",
            _ => null,
        };

        return new PeriodTwr(
            period,
            since,
            bookTwr,
            spyTwr,
            delta,
            verdict,
            holdings.Value,
            spyPriceReturn,
            holdingsDelta,
            holdings.Reason,
            holdings.MissingPriceSymbols is { Count: > 0 } ? holdings.MissingPriceSymbols : null);
    }

    private static HoldingsBasedReturnResult ComputeHoldingsBasedReturn(
        IReadOnlyList<HoldingsCapture> captures, DateOnly since, DateOnly today)
    {
        var inWindow = captures
            .Where(c => DateOnly.FromDateTime(c.CapturedAt.UtcDateTime) >= since)
            .OrderBy(c => c.CapturedAt)
            .ToList();

        if (inWindow.Count < 2)
        {
            return HoldingsBasedReturnResult.Unavailable(HoldingsReturnUnavailableReason.InsufficientCaptures);
        }

        // The same edge tolerance the invested-value figure is held to, so both cover the same span as SPY.
        var allowedGapDays = AllowedEdgeGapDays(today.DayNumber - since.DayNumber);
        var first = DateOnly.FromDateTime(inWindow[0].CapturedAt.UtcDateTime);
        var last = DateOnly.FromDateTime(inWindow[^1].CapturedAt.UtcDateTime);
        if (first > since.AddDays(allowedGapDays) || last < today.AddDays(-allowedGapDays))
        {
            return HoldingsBasedReturnResult.Unavailable(HoldingsReturnUnavailableReason.WindowNotCovered);
        }

        return HoldingsBasedReturnCalculator.Measure(inWindow);
    }

    private static decimal? ComputeSpyPriceReturn(IReadOnlyList<Domain.DailyBar> bars, DateOnly since)
    {
        var startBar = bars.FirstOrDefault(b => b.Date >= since);
        var endBar = bars.LastOrDefault();

        if (startBar is null || endBar is null || startBar.Close == 0m || startBar.Date == endBar.Date)
        {
            return null;
        }

        return Math.Round((endBar.Close - startBar.Close) / startBar.Close, 4);
    }

    private static decimal? ComputeSpyTwr(IReadOnlyList<Domain.DailyBar> bars, DateOnly since)
    {
        var startBar = bars.FirstOrDefault(b => b.Date >= since);
        var endBar = bars.LastOrDefault();

        if (startBar is null || endBar is null || startBar.AdjClose == 0m)
        {
            return null;
        }

        if (startBar.Date == endBar.Date)
        {
            return null;
        }

        return Math.Round((endBar.AdjClose - startBar.AdjClose) / startBar.AdjClose, 4);
    }

    private static decimal? ComputeBookTwr(IReadOnlyList<DailyPortfolioValue> snapshots, DateOnly since, DateOnly today)
    {
        var startSnapshot = snapshots.FirstOrDefault(s => s.Date >= since);
        var endSnapshot = snapshots.LastOrDefault();

        if (startSnapshot is null || endSnapshot is null || startSnapshot.BrokerageInvestedUsd == 0m)
        {
            return null;
        }

        var allowedGapDays = AllowedEdgeGapDays(today.DayNumber - since.DayNumber);
        if (startSnapshot.Date > since.AddDays(allowedGapDays)
            || endSnapshot.Date < today.AddDays(-allowedGapDays))
        {
            return null;
        }

        if (startSnapshot.Date == endSnapshot.Date)
        {
            return null;
        }

        return Math.Round(
            (endSnapshot.BrokerageInvestedUsd - startSnapshot.BrokerageInvestedUsd) / startSnapshot.BrokerageInvestedUsd,
            4);
    }

    private static int AllowedEdgeGapDays(int periodDays) =>
        Math.Clamp(
            (int)Math.Round(periodDays * EdgeGapPeriodRatio, MidpointRounding.AwayFromZero),
            MinEdgeGapDays,
            MaxEdgeGapDays);

    private static DateOnly StartDate(DateOnly today, BookPerformancePeriod period) => period switch
    {
        BookPerformancePeriod.OneWeek => today.AddDays(-7),
        BookPerformancePeriod.OneMonth => today.AddMonths(-1),
        BookPerformancePeriod.ThreeMonths => today.AddMonths(-3),
        BookPerformancePeriod.OneYear => today.AddYears(-1),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
    };
}
