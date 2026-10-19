using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;

namespace FinanceSentry.Modules.Radar.Application.Services;

/// <summary>
/// Computes the brokerage book's time-weighted return (M3) versus SPY's adjusted-close return over the identical
/// window (M9) for configurable lookback windows. The book side chains the daily cash-inclusive brokerage totals
/// from net-worth snapshots, taking out the deposits and withdrawals <see cref="IBrokerageFlowReader"/> reports, so
/// a contribution is not a gain; a purchase from idle broker cash stays inside the scope. The day a provider was
/// linked is bridged by <see cref="ScopeLinks"/> (its value is capital, not return). A provider that has never
/// reported a deposit or withdrawal leaves its contributions inside the chain: the figure is still shown, as an
/// estimate (<c>UNKNOWN_FLOW</c> naming the provider). Beside it each period carries the invested value's plain
/// change (<c>Book</c>, deposits included) and the holdings-based return (R1(b)): the brokerage book's
/// flow-neutral price return from <see cref="HoldingsBasedReturnCalculator"/>, against SPY's price return (close,
/// not adjusted close) measured between the first and last capture the index covers, or the reason there is no
/// number.
/// </summary>
public sealed class BookPerformanceService(
    IDailyBarRepository bars,
    IPortfolioValueSource portfolioValues,
    IHoldingsHistorySource holdingsHistory,
    IBrokerageFlowReader brokerageFlows) : IBookPerformanceService
{
    private const string BenchmarkTicker = "SPY";

    private const decimal OutperformThreshold = 0.001m;

    private const decimal EdgeGapPeriodRatio = 0.1m;

    private const int MinEdgeGapDays = 1;

    private const int MaxEdgeGapDays = 7;

    private const int SpyLookbackDays = MaxEdgeGapDays;

    public async Task<BookPerformanceResult> GetAsync(
        Guid userId,
        IReadOnlyList<BookPerformancePeriod> periods,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliest = periods.Select(p => StartDate(today, p)).Min();

        var spyBars = await bars.GetSinceAsync(BenchmarkTicker, earliest.AddDays(-SpyLookbackDays), ct);
        var portfolioSnapshots = await portfolioValues.GetAsync(userId, earliest, today, ct);
        var captures = await holdingsHistory.GetBrokerageCapturesAsync(userId, earliest, ct);
        var flows = await brokerageFlows.ReadAsync(userId, earliest, ct);

        var results = new List<PeriodTwr>(periods.Count);
        foreach (var period in periods)
        {
            var since = StartDate(today, period);
            var periodTwr = ComputePeriod(period, since, today, spyBars, portfolioSnapshots, captures, flows);
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
        IReadOnlyList<HoldingsCapture> captures,
        SleeveFlowReading flows)
    {
        var book = ComputeBookTwr(portfolioSnapshots, flows, since, today);
        var bookTwr = Round(book?.Twr.Return.Value);
        // SPY over the book's own window when there is one (M9), else from the period's first bar with no delta.
        var spy = book is { } measured ? SpyOver(spyBars, measured.Twr) : null;
        var spyTwr = spy is null ? ComputeSpyTwr(spyBars, since) : Round(spy.BenchmarkReturn.Value);
        decimal? delta = spy is not null && bookTwr is not null && spyTwr is not null
            ? Math.Round(bookTwr.Value - spyTwr.Value, 4)
            : null;

        var splitDays = portfolioSnapshots
            .Where(s => s.BrokerageInvestedUsd is not null)
            .Select(s => new SplitDay(s.Date, s.BrokerageInvestedUsd!.Value, s.Signals))
            .ToList();
        var window = BookWindow(splitDays, since, today);
        var splitWithheld = window is null
            && portfolioSnapshots.Any(s => s.BrokerageInvestedUsd is null && s.Date >= since && s.Date <= today);
        var (holdings, span) = ComputeHoldingsBasedReturn(captures, since, today);

        if (spyTwr is null && bookTwr is null && holdings.Value is null)
        {
            return null;
        }

        var spyPriceReturn = span is { } covered
            ? ComputeSpyPriceReturn(spyBars, covered.First, covered.Last)
            : null;
        decimal? holdingsDelta = holdings.Value is not null && spyPriceReturn is not null
            ? Math.Round(holdings.Value.Value - spyPriceReturn.Value, 4)
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
            holdings.MissingPriceSymbols is { Count: > 0 } ? holdings.MissingPriceSymbols : null,
            BookHeadline(window, since, today, splitWithheld, flows),
            book?.Twr.From,
            book?.Twr.To,
            book?.Reasons,
            book?.Quality);
    }

    // M3 over the cash-inclusive brokerage totals inside [since, today], with link days bridged. Null when the
    // bridged series does not cover the period within the edge allowance (the same rule the invested figure
    // keeps) or the chain cannot be measured.
    private static BookTwrWindow? ComputeBookTwr(
        IReadOnlyList<DailyPortfolioValue> snapshots, SleeveFlowReading flows, DateOnly since, DateOnly today)
    {
        var totals = snapshots
            .Where(s => s.BrokerageTotalUsd is not null && s.Date >= since && s.Date <= today)
            .ToList();
        var bridge = ScopeLinks.Bridge(
            [.. totals.Select(s => new ValuePoint(s.Date, s.BrokerageTotalUsd))],
            flows.ClassifiedFlows,
            flows.Sources.Select(s => s.LinkedOn));
        if (bridge.Opening is not { } opening || bridge.Closing is not { } closing || opening.Date >= closing.Date)
        {
            return null;
        }

        var allowedGapDays = AllowedEdgeGapDays(today.DayNumber - since.DayNumber);
        if (opening.Date > since.AddDays(allowedGapDays) || closing.Date < today.AddDays(-allowedGapDays))
        {
            return null;
        }

        var twr = TimeWeightedReturn.Measure(bridge.Values, bridge.Flows);
        if (twr.Return.Value is null)
        {
            return null;
        }

        var edges = totals.Where(s => s.Date == opening.Date || s.Date == closing.Date)
            .Select(s => s.Signals ?? HeadlineSignals.None)
            .Aggregate(HeadlineSignals.None, (merged, s) => merged.Merge(s));
        var signals = edges with
        {
            NoSplit = false,
            Fx = edges.Fx.Worst(flows.FxWithin(opening.Date, closing.Date)),
            UnknownFlowSources = flows.SourcesWithoutFlows(closing.Date),
            ScopeChanged = bridge.ScopeChanged,
        };
        var reasons = HeadlineQualityMapper.Assess(signals, amountAvailable: true).Reasons.ToList();
        // Value that appeared from an empty scope, or vanished to zero, with no recorded flow is left out of the
        // chain (rule 4).
        if ((twr.UnexplainedDays.Count > 0 || bridge.VanishedDays.Count > 0)
            && reasons.All(r => r.Code != HeadlineReasonCode.UnknownFlow))
        {
            reasons.Add(new HeadlineReason(HeadlineReasonCode.UnknownFlow));
        }

        var quality = reasons.Select(r => HeadlineQualityMapper.QualityOf(r.Code))
            .DefaultIfEmpty(HeadlineQuality.Complete)
            .Max();
        return new BookTwrWindow(twr, reasons, quality);
    }

    // SPY's adjusted close at the book's own opening and closing dates (the last bar on or before each, so a
    // weekend snapshot reads Friday's close), as M9 compares them. Null when either date has no bar within the
    // lookback.
    private static ExcessReturnResult? SpyOver(IReadOnlyList<Domain.DailyBar> spyBars, TimeWeightedResult book)
    {
        var opening = spyBars.LastOrDefault(b => b.Date <= book.From);
        var closing = spyBars.LastOrDefault(b => b.Date <= book.To);
        if (opening is null || closing is null || opening.Date == closing.Date
            || opening.Date < book.From.AddDays(-SpyLookbackDays) || closing.Date < book.To.AddDays(-SpyLookbackDays))
        {
            return null;
        }

        return ExcessReturn.Measure(
            book, new ValuePoint(book.From, opening.AdjClose), new ValuePoint(book.To, closing.AdjClose));
    }

    private static decimal? Round(decimal? rate) => rate is { } r ? Math.Round(r, 4) : null;

    private static (HoldingsBasedReturnResult Result, CaptureSpan? Span) ComputeHoldingsBasedReturn(
        IReadOnlyList<HoldingsCapture> captures, DateOnly since, DateOnly today)
    {
        var inWindow = captures
            .Where(c => DateOnly.FromDateTime(c.CapturedAt.UtcDateTime) >= since)
            .OrderBy(c => c.CapturedAt)
            .ToList();

        if (inWindow.Count < 2)
        {
            return (HoldingsBasedReturnResult.Unavailable(HoldingsReturnUnavailableReason.InsufficientCaptures), null);
        }

        // The same edge tolerance the invested-value figure is held to, so both cover the same span as SPY.
        var allowedGapDays = AllowedEdgeGapDays(today.DayNumber - since.DayNumber);
        var first = DateOnly.FromDateTime(inWindow[0].CapturedAt.UtcDateTime);
        var last = DateOnly.FromDateTime(inWindow[^1].CapturedAt.UtcDateTime);
        if (first > since.AddDays(allowedGapDays) || last < today.AddDays(-allowedGapDays))
        {
            return (HoldingsBasedReturnResult.Unavailable(HoldingsReturnUnavailableReason.WindowNotCovered), null);
        }

        return (HoldingsBasedReturnCalculator.Measure(inWindow), new CaptureSpan(first, last));
    }

    private static decimal? ComputeSpyPriceReturn(IReadOnlyList<Domain.DailyBar> bars, DateOnly from, DateOnly to)
    {
        var startBar = bars.LastOrDefault(b => b.Date <= from);
        var endBar = bars.LastOrDefault(b => b.Date <= to);

        if (startBar is null || endBar is null || startBar.Close == 0m || startBar.Date == endBar.Date)
        {
            return null;
        }

        return Math.Round((endBar.Close - startBar.Close) / startBar.Close, 4);
    }

    // The invested value's change as M1. With no measurable window the figure is unavailable (SHORT_HISTORY, or
    // NO_SPLIT when the period has snapshot days whose split was not measured); a zero start keeps the dollar
    // change and withholds the percent (ZERO_BASE), and so does a provider linked inside the window (SCOPE_CHANGED).
    private static Headline BookHeadline(
        (SplitDay Start, SplitDay End)? window, DateOnly since, DateOnly today, bool splitWithheld,
        SleeveFlowReading flows)
    {
        if (window is not { } w)
        {
            var missing = splitWithheld ? HeadlineSignals.None with { NoSplit = true } : HeadlineSignals.None;
            return HeadlineFactory.Change(null, null, HeadlineScope.InvestedBrokerage, since, today, false, missing);
        }

        var signals = (w.Start.Signals ?? HeadlineSignals.None).Merge(w.End.Signals ?? HeadlineSignals.None);
        var scopeChanged = flows.Sources.Any(s => s.LinkedOn >= w.Start.Date && s.LinkedOn <= w.End.Date);
        return HeadlineFactory.Change(
            w.Start.InvestedUsd, w.End.InvestedUsd, HeadlineScope.InvestedBrokerage,
            w.Start.Date, w.End.Date, scopeChanged, signals);
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

    // The first and last split days of the period, or null when the history does not cover it: a missing end, an
    // edge gap past the allowance, or a single day.
    private static (SplitDay Start, SplitDay End)? BookWindow(
        IReadOnlyList<SplitDay> snapshots, DateOnly since, DateOnly today)
    {
        var startSnapshot = snapshots.FirstOrDefault(s => s.Date >= since);
        var endSnapshot = snapshots.LastOrDefault();

        if (startSnapshot is null || endSnapshot is null)
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

        return (startSnapshot, endSnapshot);
    }

    private readonly record struct CaptureSpan(DateOnly First, DateOnly Last);

    private sealed record SplitDay(DateOnly Date, decimal InvestedUsd, HeadlineSignals? Signals);

    private readonly record struct BookTwrWindow(
        TimeWeightedResult Twr, IReadOnlyList<HeadlineReason> Reasons, HeadlineQuality Quality);

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
