namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// The pinned S&amp;P 500 measurement horizon (owner ruling 2026-11-15): rolling 12 months plus since
/// inception. These two windows are the headline answer to "are we beating the S&amp;P 500"; 1M and 3M
/// stay on the record as context so a short bad stretch is visible without judging the strategy on it.
/// </summary>
public static class BenchmarkHorizon
{
    public static IReadOnlyList<TrackRecordWindow> Headline { get; } =
    [
        TrackRecordWindow.OneYear,
        TrackRecordWindow.SinceInception,
    ];

    public static bool IsHeadline(TrackRecordWindow window) => Headline.Contains(window);
}
