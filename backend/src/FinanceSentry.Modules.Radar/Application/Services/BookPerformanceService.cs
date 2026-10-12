using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;

namespace FinanceSentry.Modules.Radar.Application.Services;

/// <summary>
/// Computes time-weighted return (HPR approximation) for the invested brokerage book versus SPY over
/// configurable lookback windows. Uses adjusted close from daily bars for SPY and the invested brokerage
/// value (idle broker cash excluded) from net-worth snapshots for the portfolio. Deposits and withdrawals
/// are not adjusted for, so a contribution reads as a gain.
/// </summary>
public sealed class BookPerformanceService(
    IDailyBarRepository bars,
    IPortfolioValueSource portfolioValues) : IBookPerformanceService
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

        var results = new List<PeriodTwr>(periods.Count);
        foreach (var period in periods)
        {
            var since = StartDate(today, period);
            var periodTwr = ComputePeriod(period, since, today, spyBars, portfolioSnapshots);
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
        IReadOnlyList<DailyPortfolioValue> portfolioSnapshots)
    {
        var spyTwr = ComputeSpyTwr(spyBars, since);
        var bookTwr = ComputeBookTwr(portfolioSnapshots, since, today);

        if (spyTwr is null && bookTwr is null)
        {
            return null;
        }

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

        return new PeriodTwr(period, since, bookTwr, spyTwr, delta, verdict);
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
