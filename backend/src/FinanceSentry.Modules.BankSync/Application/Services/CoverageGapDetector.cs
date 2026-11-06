namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Utils;

/// <summary>What the detector found: when the quiet run started, how long it is, and the weekly spend it fell from.</summary>
public sealed record CoverageGapFinding(DateOnly QuietFrom, int QuietWeeks, decimal BaselineWeeklySpendUsd);

/// <summary>
/// The coverage sentinel's rule, kept pure: an account whose day-to-day spend used to flow and then drops below
/// <see cref="QuietFraction"/> of its trailing-3-month weekly median for <see cref="MinQuietWeeks"/> or more complete
/// weeks has gone quiet. Weeks run Monday to Sunday; only complete weeks are judged, so a part-way week never reads as
/// quiet. Whether the syncs succeeded is the caller's gate - a failing sync is a different alert.
/// </summary>
public static class CoverageGapDetector
{
    /// <summary>Day-to-day spend: what a card or cash buys in person. Rent, loans, transfers and subscriptions are not.</summary>
    public static readonly IReadOnlySet<string> DailyCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        CategoryKeys.FoodAndDrink,
        CategoryKeys.Transportation,
        CategoryKeys.GeneralMerchandise,
        CategoryKeys.Entertainment,
        CategoryKeys.PersonalCare,
    };

    /// <summary>A week is quiet when its spend is under this fraction of the baseline (a week at exactly the fraction is not).</summary>
    public const decimal QuietFraction = 0.20m;

    /// <summary>Consecutive quiet weeks before the sentinel fires.</summary>
    public const int MinQuietWeeks = 2;

    /// <summary>The trailing window the baseline median is taken over: 3 months.</summary>
    public const int BaselineWeeks = 13;

    /// <summary>
    /// Weeks of history the baseline needs. Weeks before the account's first daily spend are no data, not zeros, so a
    /// newly connected account is not judged against a median it never had.
    /// </summary>
    public const int MinBaselineWeeks = 6;

    /// <summary>A baseline under this weekly USD amount is not "spend that used to flow": too small to tell quiet from normal.</summary>
    public const decimal MinBaselineWeeklySpendUsd = 20m;

    /// <summary>The longest quiet run looked back over, so the hole's start is found without scanning all history.</summary>
    public const int MaxQuietWeeks = 26;

    public static DateOnly WeekStart(DateOnly day)
        => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>The Monday of the most recent complete week as of <paramref name="today"/>.</summary>
    public static DateOnly LatestCompleteWeek(DateOnly today) => WeekStart(today).AddDays(-7);

    public static bool IsQuiet(decimal weeklySpendUsd, decimal baselineWeeklySpendUsd)
        => weeklySpendUsd < baselineWeeklySpendUsd * QuietFraction;

    /// <summary>
    /// The gap ending at <paramref name="latestCompleteWeek"/>, or null. <paramref name="weeklySpendUsd"/> is keyed by
    /// week-start Monday (a missing week is a zero week); <paramref name="firstSpendWeek"/> is the week of the account's
    /// first daily spend. The gap reaches back to its first quiet week, judged against the median of the
    /// <see cref="BaselineWeeks"/> weeks before that week, so the baseline never includes the gap's own weeks. A stray
    /// busy week does not end the gap (see <see cref="IsContinuous"/>): the same two-week rule that closes a hole decides
    /// where it began, so one purchase in a quiet stretch cannot push the start weeks past the spend actually missing.
    /// </summary>
    public static CoverageGapFinding? Detect(
        IReadOnlyDictionary<DateOnly, decimal> weeklySpendUsd, DateOnly firstSpendWeek, DateOnly latestCompleteWeek)
    {
        CoverageGapFinding? best = null;

        for (var weeks = MinQuietWeeks; weeks <= MaxQuietWeeks; weeks++)
        {
            var from = latestCompleteWeek.AddDays(-7 * (weeks - 1));
            var baseline = Baseline(weeklySpendUsd, firstSpendWeek, from);
            if (baseline is null)
                break;

            var quiet = Enumerable.Range(0, weeks)
                .Select(i => IsQuiet(Spend(weeklySpendUsd, from.AddDays(7 * i)), baseline.Value))
                .ToArray();
            if (!IsContinuous(quiet))
                break;

            // A gap starts on a quiet week: a busy week at the edge is where the spend still flowed, not the gap.
            if (quiet[0])
                best = new CoverageGapFinding(from, weeks, baseline.Value);
        }

        return best;
    }

    /// <summary>
    /// Whether a run of weeks (oldest first, true = quiet) is one gap: it ends on <see cref="MinQuietWeeks"/> quiet weeks
    /// (the gap is still going), and every busy week in it is followed by <see cref="MinQuietWeeks"/> quiet weeks. That is
    /// the mirror of how a hole closes - it takes <see cref="MinQuietWeeks"/> consecutive weeks of normal spend to end
    /// one, so a lone purchase week inside it does not.
    /// </summary>
    public static bool IsContinuous(IReadOnlyList<bool> quietWeeks)
    {
        if (quietWeeks.Count < MinQuietWeeks || quietWeeks.Skip(quietWeeks.Count - MinQuietWeeks).Any(q => !q))
            return false;

        for (var i = 0; i < quietWeeks.Count - MinQuietWeeks; i++)
        {
            if (!quietWeeks[i] && Enumerable.Range(i + 1, MinQuietWeeks).Any(j => !quietWeeks[j]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Where an already-open hole that starts at <paramref name="holeFrom"/> really began: the first quiet week reached
    /// walking back from it, against the hole's own <paramref name="baselineWeeklySpendUsd"/>, through the same rule as
    /// <see cref="Detect"/>. Never later than <paramref name="holeFrom"/>, never before <paramref name="firstSpendWeek"/>
    /// (earlier weeks are no data) and at most <see cref="MaxQuietWeeks"/> weeks back.
    /// </summary>
    public static DateOnly FirstQuietWeek(
        IReadOnlyDictionary<DateOnly, decimal> weeklySpendUsd,
        DateOnly firstSpendWeek,
        DateOnly holeFrom,
        decimal baselineWeeklySpendUsd)
    {
        var start = holeFrom;
        var lowest = holeFrom.AddDays(-7 * MaxQuietWeeks);
        if (lowest < firstSpendWeek)
            lowest = firstSpendWeek;

        for (var week = holeFrom.AddDays(-7); week >= lowest; week = week.AddDays(-7))
        {
            var quiet = IsQuiet(Spend(weeklySpendUsd, week), baselineWeeklySpendUsd);
            if (quiet)
            {
                start = week;
                continue;
            }

            // A busy week is a stray only when MinQuietWeeks quiet weeks follow it.
            var followed = Enumerable.Range(1, MinQuietWeeks)
                .All(i => IsQuiet(Spend(weeklySpendUsd, week.AddDays(7 * i)), baselineWeeklySpendUsd));
            if (!followed)
                break;
        }

        return start;
    }

    /// <summary>
    /// The median weekly spend over the weeks before <paramref name="quietFrom"/>, or null when there is too little
    /// history or the median is too small to mean anything.
    /// </summary>
    public static decimal? Baseline(
        IReadOnlyDictionary<DateOnly, decimal> weeklySpendUsd, DateOnly firstSpendWeek, DateOnly quietFrom)
    {
        var windowStart = quietFrom.AddDays(-7 * BaselineWeeks);
        var from = windowStart > firstSpendWeek ? windowStart : firstSpendWeek;

        var weeks = new List<decimal>();
        for (var week = from; week < quietFrom; week = week.AddDays(7))
            weeks.Add(Spend(weeklySpendUsd, week));

        if (weeks.Count < MinBaselineWeeks)
            return null;

        var median = Median(weeks);
        return median >= MinBaselineWeeklySpendUsd ? median : null;
    }

    /// <summary>
    /// Weekly spend in USD by week-start Monday from an account's day-to-day debits; converted once here, where the
    /// account's currency is in scope.
    /// </summary>
    public static Dictionary<DateOnly, decimal> WeeklySpendUsd(string currency, IEnumerable<(DateTime Date, decimal Amount)> debits)
        => debits
            .GroupBy(d => WeekStart(DateOnly.FromDateTime(d.Date)))
            .ToDictionary(g => g.Key, g => g.Sum(d => CurrencyConverter.ToUsd(Math.Abs(d.Amount), currency)));

    public static decimal Spend(IReadOnlyDictionary<DateOnly, decimal> weeklySpendUsd, DateOnly week)
        => weeklySpendUsd.GetValueOrDefault(week);

    private static decimal Median(List<decimal> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2m;
    }
}
