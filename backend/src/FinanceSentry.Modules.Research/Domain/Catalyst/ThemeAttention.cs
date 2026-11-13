namespace FinanceSentry.Modules.Research.Domain.Catalyst;

/// <summary>
/// The attention a theme phrase is getting in the news, as a fact: the newest complete day's GDELT article count against the mean of
/// the days before it. A fact, not a verdict: nothing here penalises or promotes a name, and a high ratio is the crowded lane the
/// add rule should not chase (finance framework 3.4), not a reason to add.
/// </summary>
/// <param name="Theme">The reference list key (<c>theme:memory</c>).</param>
/// <param name="AsOf">The newest complete UTC day the figures are for.</param>
/// <param name="Articles">Articles matching the phrase on <paramref name="AsOf"/>.</param>
/// <param name="BaselineDailyArticles">Mean articles a day over the <paramref name="BaselineDays"/> complete days before <paramref name="AsOf"/>; null when fewer than the minimum were stored.</param>
/// <param name="Ratio"><paramref name="Articles"/> over the baseline; null when there is no baseline or it is zero.</param>
/// <param name="HistoryDays">Days stored for the phrase.</param>
public sealed record ThemeAttentionReading(
    string Theme,
    string Phrase,
    DateOnly AsOf,
    long Articles,
    decimal? BaselineDailyArticles,
    decimal? Ratio,
    int BaselineDays,
    int HistoryDays,
    DateTimeOffset MeasuredAt);

/// <summary>
/// The attention facts of a candidate: one reading per phrase of every theme the ticker belongs to. Attached to the scorecard
/// evidence at score time; <paramref name="Note"/> says why it is empty (no theme, or no stored reading yet).
/// </summary>
public sealed record AttentionFact(IReadOnlyList<ThemeAttentionReading> Readings, string? Note = null)
{
    public static AttentionFact None(string note) => new([], note);
}

/// <summary>Pure derivation of a <see cref="ThemeAttentionReading"/> from the stored days of one phrase.</summary>
public static class AttentionMath
{
    /// <summary>
    /// The reading as of <paramref name="today"/>: the newest day before today is the latest complete day (today's count is partial),
    /// the baseline is the mean of up to <paramref name="baselineDays"/> complete days before it, and withheld when fewer than
    /// <paramref name="minBaselineDays"/> were stored. Null when no complete day is stored.
    /// </summary>
    public static ThemeAttentionReading? Reading(
        IReadOnlyList<ThemeAttentionDay> phraseDays, DateOnly today, int baselineDays, int minBaselineDays)
    {
        var complete = phraseDays.Where(d => d.Day < today).OrderBy(d => d.Day).ToList();
        if (complete.Count == 0)
        {
            return null;
        }

        var latest = complete[^1];
        var baseline = complete.Take(complete.Count - 1).TakeLast(Math.Max(1, baselineDays)).ToList();
        decimal? mean = baseline.Count >= Math.Max(1, minBaselineDays) ? (decimal)baseline.Average(d => d.Articles) : null;
        decimal? ratio = mean is > 0 ? Math.Round(latest.Articles / mean.Value, 2) : null;

        return new ThemeAttentionReading(
            latest.Theme,
            latest.Phrase,
            latest.Day,
            latest.Articles,
            mean is null ? null : Math.Round(mean.Value, 1),
            ratio,
            baseline.Count,
            phraseDays.Count,
            phraseDays.Max(d => d.MeasuredAt));
    }
}
