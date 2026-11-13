namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Coverage;

/// <summary>One stretch of a name being covered: an add to its removal (null while still covered).</summary>
public sealed record CoverageMembership(
    string Ticker, DateTimeOffset Start, DateTimeOffset? End, CoverageReason Reason, string? ScreenTag);

/// <summary>
/// Grades the coverage universe against the benchmark from stored adjusted closes: pure arithmetic, no I/O, no model.
/// A membership's window runs from its add date to the horizon, or to its removal if that came first (a removed member
/// keeps its graded history up to the removal date). Entry is the first bar on or after the add date, exit the last bar
/// on or before the window end, each within <see cref="GraceDays"/>; the benchmark is read on the same two dates.
/// </summary>
public static class CoverageGrader
{
    public const int HeadlineHorizonDays = 90;
    public const int GraceDays = 7;
    public const string Graded = "Graded";
    public const string NotEnoughHistory = "NotEnoughHistory";
    public const string NotEnoughHistoryMessage = "not enough history yet";

    private const int FigureDecimals = 4;
    private const decimal Percent = 100m;

    public static readonly IReadOnlyList<int> Horizons = [30, HeadlineHorizonDays];

    public static CoverageVsBenchmarkDto Grade(
        IReadOnlyList<CoverageMembership> memberships,
        int currentMembers,
        IReadOnlyDictionary<string, IReadOnlyList<AdjustedClose>> closes,
        IReadOnlyList<AdjustedClose> benchmark,
        DateTimeOffset asOf,
        string benchmarkName,
        string benchmarkTicker,
        int minimumSample)
    {
        var horizons = Horizons.Select(h => new CoverageHorizonDto(
            h,
            h == HeadlineHorizonDays,
            Summarise(memberships.Select(m => GradeOne(m, h, closes, benchmark, asOf)).ToList(), minimumSample),
            [.. memberships
                .GroupBy(m => (m.Reason, m.ScreenTag))
                .OrderBy(g => g.Key.Reason).ThenBy(g => g.Key.ScreenTag, StringComparer.Ordinal)
                .Select(g => new CoverageSourceGradeDto(
                    g.Key.Reason.ToString(),
                    g.Key.ScreenTag,
                    Summarise(g.Select(m => GradeOne(m, h, closes, benchmark, asOf)).ToList(), minimumSample)))]))
            .ToList();

        return new CoverageVsBenchmarkDto(
            benchmarkName, benchmarkTicker, HeadlineHorizonDays, minimumSample, currentMembers, horizons);
    }

    private sealed record One(State Kind, bool ThroughRemoval, decimal Subject, decimal Bench);

    private enum State
    {
        Graded,
        Awaiting,
        NotPriced,
    }

    private static One GradeOne(
        CoverageMembership m,
        int horizonDays,
        IReadOnlyDictionary<string, IReadOnlyList<AdjustedClose>> closes,
        IReadOnlyList<AdjustedClose> benchmark,
        DateTimeOffset asOf)
    {
        var horizonEnd = m.Start.AddDays(horizonDays);
        var throughRemoval = m.End is { } end && end < horizonEnd;
        var windowEnd = throughRemoval ? m.End!.Value : horizonEnd;
        if (windowEnd > asOf)
        {
            return new One(State.Awaiting, false, 0m, 0m);
        }

        var series = closes.GetValueOrDefault(m.Ticker.Trim().ToUpperInvariant()) ?? [];
        var entryDay = DateOnly.FromDateTime(m.Start.UtcDateTime);
        var exitDay = DateOnly.FromDateTime(windowEnd.UtcDateTime);
        var entry = series.Where(b => b.Date >= entryDay && b.Date <= entryDay.AddDays(GraceDays)).MinBy(b => b.Date);
        var exit = series.Where(b => b.Date <= exitDay && b.Date >= exitDay.AddDays(-GraceDays)).MaxBy(b => b.Date);
        var benchEntry = entry is null ? null : benchmark.FirstOrDefault(b => b.Date == entry.Date);
        var benchExit = exit is null ? null : benchmark.FirstOrDefault(b => b.Date == exit.Date);

        if (entry is null || exit is null || benchEntry is null || benchExit is null
            || entry.Close <= 0m || benchEntry.Close <= 0m || exit.Date <= entry.Date)
        {
            // A closing bar that has not been ingested yet is a wait, not a failure.
            return exit is null && windowEnd.AddDays(GraceDays) >= asOf
                ? new One(State.Awaiting, false, 0m, 0m)
                : new One(State.NotPriced, false, 0m, 0m);
        }

        return new One(
            State.Graded,
            throughRemoval,
            (exit.Close / entry.Close - 1m) * Percent,
            (benchExit.Close / benchEntry.Close - 1m) * Percent);
    }

    private static CoverageGradeDto Summarise(IReadOnlyList<One> rows, int minimumSample)
    {
        var graded = rows.Where(r => r.Kind == State.Graded).ToList();
        var awaiting = rows.Count(r => r.Kind == State.Awaiting);
        var notPriced = rows.Count(r => r.Kind == State.NotPriced);
        var through = graded.Count(r => r.ThroughRemoval);

        if (graded.Count == 0)
        {
            return new CoverageGradeDto(
                rows.Count, 0, 0, awaiting, notPriced, null, null, null, null,
                NotEnoughHistory,
                rows.Count == 0 ? $"{NotEnoughHistoryMessage} (no coverage members)" : NotEnoughHistoryMessage,
                true,
                $"too few to tell skill from luck (0 of {minimumSample})");
        }

        var list = graded.Average(r => r.Subject);
        var bench = graded.Average(r => r.Bench);
        var beat = (decimal)graded.Count(r => r.Subject > r.Bench) / graded.Count * Percent;
        var tooFew = graded.Count < minimumSample;
        return new CoverageGradeDto(
            rows.Count, graded.Count, through, awaiting, notPriced,
            Round(list), Round(bench), Round(list - bench), Round(beat),
            Graded, null, tooFew,
            tooFew ? $"too few to tell skill from luck ({graded.Count} of {minimumSample})" : null);
    }

    private static decimal Round(decimal value) => Math.Round(value, FigureDecimals);
}
