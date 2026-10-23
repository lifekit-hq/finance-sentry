namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Domain;

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
    /// The quiet run ending at <paramref name="latestCompleteWeek"/>, or null. <paramref name="weeklySpendUsd"/> is keyed by
    /// week-start Monday (a missing week is a zero week); <paramref name="firstSpendWeek"/> is the week of the account's
    /// first daily spend. The run is the longest whose weeks are all quiet against the median of the
    /// <see cref="BaselineWeeks"/> weeks before it, so the baseline never includes the quiet weeks themselves.
    /// </summary>
    public static CoverageGapFinding? Detect(
        IReadOnlyDictionary<DateOnly, decimal> weeklySpendUsd, DateOnly firstSpendWeek, DateOnly latestCompleteWeek)
    {
        CoverageGapFinding? best = null;

        for (var quietWeeks = MinQuietWeeks; quietWeeks <= MaxQuietWeeks; quietWeeks++)
        {
            var quietFrom = latestCompleteWeek.AddDays(-7 * (quietWeeks - 1));
            var baseline = Baseline(weeklySpendUsd, firstSpendWeek, quietFrom);
            if (baseline is null)
                break;

            var allQuiet = Enumerable.Range(0, quietWeeks)
                .All(i => IsQuiet(Spend(weeklySpendUsd, quietFrom.AddDays(7 * i)), baseline.Value));
            if (!allQuiet)
                break;

            best = new CoverageGapFinding(quietFrom, quietWeeks, baseline.Value);
        }

        return best;
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

    public static decimal Spend(IReadOnlyDictionary<DateOnly, decimal> weeklySpendUsd, DateOnly week)
        => weeklySpendUsd.GetValueOrDefault(week);

    private static decimal Median(List<decimal> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2m;
    }
}
