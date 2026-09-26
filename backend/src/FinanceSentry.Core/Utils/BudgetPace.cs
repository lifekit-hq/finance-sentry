namespace FinanceSentry.Core.Utils;

/// <summary>
/// One number viewed twice, plus the fraction it is derived from: elapsed fraction of the
/// month, pace ratio, and projected month-end spend.
/// </summary>
/// <param name="ElapsedFraction">Day-of-month ÷ days-in-month (UTC). 1.0 when
/// <paramref name="AsOfDate"/> lands on the last day of its month — the way a past month
/// (evaluated as of its own last day) collapses <see cref="PaceRatio"/> to the plain
/// <c>spent / limit</c> ratio.</param>
/// <param name="PaceRatio">How fast the budget is burning relative to the limit:
/// <c>spentUsd / (limitUsd × elapsedFraction)</c>. Zero when <c>limitUsd</c> is not positive —
/// there is no rate to be "off" relative to a limit that does not exist.</param>
/// <param name="ProjectedMonthEndSpend">Where spend lands if the current pace holds for the
/// rest of the month: <c>spentUsd / elapsedFraction</c>. Identical information to
/// <see cref="PaceRatio"/>, expressed as a currency figure a reader can act on directly
/// rather than a ratio they must multiply against the limit.</param>
public readonly record struct BudgetPaceResult(
    decimal ElapsedFraction,
    decimal PaceRatio,
    decimal ProjectedMonthEndSpend);

/// <summary>
/// Separates "how much has been spent" from "how fast it is being spent" by dividing through
/// the elapsed fraction of the month. The daily budget job's plain <c>spent / limit</c> ratio
/// fires on day 28 of a perfectly-paced budget and never fires for a budget that burned most of
/// its limit in the first week and then stopped — pace catches the second case, which is the
/// one worth catching.
/// </summary>
public static class BudgetPace
{
    /// <summary>
    /// Computes elapsed fraction, pace ratio, and projected month-end spend for
    /// <paramref name="asOfDate"/>'s month. Pure; callers pass the day they want evaluated as
    /// of — a past month reads as complete by passing its last day, where elapsed fraction is 1
    /// and pace ratio collapses to the plain <c>spentUsd / limitUsd</c> ratio.
    /// </summary>
    /// <param name="limitUsd">Non-positive values are guarded rather than thrown on — mirrors
    /// the budget breach job's own <c>limitUsd &lt;= 0</c> guard — and yield a pace ratio of 0
    /// rather than a division blow-up.</param>
    public static BudgetPaceResult Calculate(decimal spentUsd, decimal limitUsd, DateTime asOfDate)
    {
        var elapsedFraction = ElapsedMonthFraction(asOfDate);
        var projectedMonthEndSpend = spentUsd / elapsedFraction;
        var paceRatio = limitUsd <= 0m ? 0m : spentUsd / (limitUsd * elapsedFraction);

        return new BudgetPaceResult(elapsedFraction, paceRatio, projectedMonthEndSpend);
    }

    private static decimal ElapsedMonthFraction(DateTime asOfDate)
    {
        var daysInMonth = DateTime.DaysInMonth(asOfDate.Year, asOfDate.Month);
        return (decimal)asOfDate.Day / daysInMonth;
    }
}
