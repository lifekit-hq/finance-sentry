namespace FinanceSentry.Modules.Wealth.Application.Services;

using System.Globalization;
using FinanceSentry.Modules.Wealth.Application.Queries;

/// <summary>The Telegram-bound monthly brief: <c>Headline</c> is the alert title, <c>Body</c> the message.</summary>
public sealed record FireBrief(string Headline, string Body);

/// <summary>
/// Composes the monthly FIRE brief (#433 S7) from a <see cref="FireProjectionResponse"/>: the same
/// gauge and the same spelled-out assumptions the dashboard tile shows. Pure — no I/O, no clock.
/// Stays within the Ledger message-format rule (headline included, at most twelve lines).
/// </summary>
public static class FireBriefComposer
{
    private const int GaugeCells = 10;
    private const int PercentScale = 100;
    private const int MonthsPerYear = 12;
    private const decimal ThousandThreshold = 1_000m;
    private const decimal MillionThreshold = 1_000_000m;
    private const char FilledCell = '█';
    private const char EmptyCell = '░';

    /// <summary>
    /// Returns <c>null</c> for <see cref="FireProjectionStatus.InsufficientHistory"/>: with under three
    /// complete months of flow there is no honest figure to brief, and the tile is hidden for the same reason.
    /// </summary>
    public static FireBrief? Compose(FireProjectionResponse projection)
    {
        if (projection.Status == FireProjectionStatus.InsufficientHistory)
        {
            return null;
        }

        var lines = new List<string>
        {
            BuildGauge(projection),
            $"Net worth {FormatUsd(projection.CurrentNetWorth)} of a {FormatUsd(projection.Target)} target.",
            BuildOutcomeLine(projection),
            BuildAssumptionsLine(projection),
        };

        if (projection.HasStaleSleeves)
        {
            lines.Add("Some holdings have not synced recently, so net worth may lag.");
        }

        return new FireBrief(BuildHeadline(projection), string.Join("\n", lines));
    }

    private static string BuildHeadline(FireProjectionResponse projection) => projection.Status switch
    {
        FireProjectionStatus.AlreadyReached => "FIRE brief: target reached",
        FireProjectionStatus.NotSaving => "FIRE brief: not saving enough to reach the target",
        _ => $"FIRE brief: projected {FormatDate(projection.ProjectedDate)}",
    };

    private static string BuildGauge(FireProjectionResponse projection)
    {
        var fraction = projection.Target <= 0m
            ? 1m
            : Math.Clamp(projection.CurrentNetWorth / projection.Target, 0m, 1m);
        var filled = (int)Math.Floor(fraction * GaugeCells);
        var percent = (int)Math.Floor(fraction * PercentScale);
        return $"[{new string(FilledCell, filled)}{new string(EmptyCell, GaugeCells - filled)}] {percent}% of target";
    }

    private static string BuildOutcomeLine(FireProjectionResponse projection) => projection.Status switch
    {
        FireProjectionStatus.AlreadyReached => "Your net worth already covers the target.",
        FireProjectionStatus.NotSaving =>
            $"At {FormatUsd(projection.MonthlySavings)}/month saved the target is out of reach.",
        _ => $"About {FormatDuration(projection.MonthsToFire)} away at {FormatUsd(projection.MonthlySavings)}/month saved.",
    };

    private static string BuildAssumptionsLine(FireProjectionResponse projection) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Assumes {FormatPercent(projection.SafeWithdrawalRate)} safe withdrawal rate, " +
            $"{FormatPercent(projection.RealAnnualReturn)} real annual return, and " +
            $"{FormatUsd(projection.AnnualSpend)}/year spend (12 × median monthly outflow).");

    private static string FormatPercent(decimal fraction) =>
        string.Create(CultureInfo.InvariantCulture, $"{fraction * PercentScale:0.##}%");

    private static string FormatDate(DateOnly? date) =>
        date?.ToString("MMMM yyyy", CultureInfo.InvariantCulture) ?? "date unavailable";

    private static string FormatDuration(decimal? months)
    {
        if (months is not { } value)
        {
            return "an unknown time";
        }

        var total = (int)Math.Ceiling(value);
        var years = total / MonthsPerYear;
        var remainder = total % MonthsPerYear;
        return years == 0 ? $"{remainder}m"
            : remainder == 0 ? $"{years}y"
            : $"{years}y {remainder}m";
    }

    private static string FormatUsd(decimal usd)
    {
        var abs = Math.Abs(usd);
        var culture = CultureInfo.InvariantCulture;
        return abs >= MillionThreshold ? $"${(usd / MillionThreshold).ToString("0.#", culture)}m"
            : abs >= ThousandThreshold ? $"${(usd / ThousandThreshold).ToString("0.#", culture)}k"
            : $"${usd.ToString("0", culture)}";
    }
}
