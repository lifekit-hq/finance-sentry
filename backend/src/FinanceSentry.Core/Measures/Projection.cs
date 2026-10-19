namespace FinanceSentry.Core.Measures;

/// <summary>
/// A projected figure as a range (decision C8): <paramref name="Central"/> at the central return assumption,
/// <paramref name="Low"/> and <paramref name="High"/> the smallest and largest of the three scenarios, so
/// <c>Low ≤ Central ≤ High</c> always holds. Which scenario lands on which bound depends on the figure: a higher
/// return gives a higher value but fewer months to a target.
/// </summary>
public readonly record struct ProjectionRange(decimal Low, decimal Central, decimal High)
{
    /// <summary>Orders three scenario results into a range around the central one.</summary>
    public static ProjectionRange Of(decimal atLowReturn, decimal atCentralReturn, decimal atHighReturn) => new(
        Math.Min(atCentralReturn, Math.Min(atLowReturn, atHighReturn)),
        atCentralReturn,
        Math.Max(atCentralReturn, Math.Max(atLowReturn, atHighReturn)));
}

/// <summary>
/// The one projection assumption set every projection uses (M10, decision C8): a real (after-inflation) annual
/// return, shown as a low / central / high range, and the safe withdrawal rate that turns a year of spending
/// into a financial-independence target. Both are fractions (0.05 is 5%).
/// <para>
/// The range is the central return ± <see cref="ReturnSpread"/>, with the low bound floored at 0% real. The
/// cited standards (FINRA 2210(d)(1)(F) / 2214, the DOL lifetime income illustration) fix how a projection is
/// disclosed, not its bounds; the three-point spread is the one the UK FCA prescribes between its lower,
/// intermediate and higher projection rates (COBS 13 Annex 2: 2%, 5%, 8%), which on the default 5% central
/// return gives the same 2% / 5% / 8%.
/// </para>
/// </summary>
public sealed record ProjectionAssumptions(decimal RealAnnualReturn, decimal SafeWithdrawalRate)
{
    /// <summary>How far the low and high return assumptions sit from the central one: three percentage points.</summary>
    public const decimal ReturnSpread = 0.03m;

    /// <summary>The defaults a person who never changed them projects with: 5% real return, 4% withdrawal rate.</summary>
    public static ProjectionAssumptions Default { get; } = new(0.05m, 0.04m);

    /// <summary>The low, central and high real annual returns. The low one never drops below 0% (or the central one).</summary>
    public ProjectionRange RealAnnualReturns => new(
        Math.Min(RealAnnualReturn, Math.Max(0m, RealAnnualReturn - ReturnSpread)),
        RealAnnualReturn,
        RealAnnualReturn + ReturnSpread);
}

/// <summary>
/// The savings path a projection extends: the median monthly net saving over <paramref name="CompleteMonths"/>
/// complete calendar months.
/// </summary>
public sealed record SavingsPath(decimal MonthlySavingsUsd, int CompleteMonths);

/// <summary>
/// How a financial-independence projection resolved: a reachable date, or one of the two states that make a
/// date meaningless, each rendered as its own state rather than a number a reader would have to interpret.
/// </summary>
public enum FireReachability
{
    /// <summary>Reachable at a specific number of months out.</summary>
    Reachable,

    /// <summary>Current net worth already meets or exceeds the target.</summary>
    AlreadyReached,

    /// <summary>Monthly savings are zero or negative: no rate of progress to project from.</summary>
    NotSaving,
}

/// <summary>
/// <paramref name="TargetUsd"/> is always set (it needs only the annual spend and the withdrawal rate);
/// <paramref name="MonthsToTarget"/> is set whenever <paramref name="Status"/> is not
/// <see cref="FireReachability.NotSaving"/>: all zero for <see cref="FireReachability.AlreadyReached"/>, and for
/// <see cref="FireReachability.Reachable"/> the months at each return assumption (the high return is the
/// earliest, the low one the latest).
/// </summary>
public sealed record FireProjectionResult(FireReachability Status, decimal TargetUsd, ProjectionRange? MonthsToTarget);

/// <summary>
/// M10, projection (finance-framework §3.4): a savings path plus the shared return assumption, as a range.
/// Pure: no I/O, no clock. Every projection compounds the current balance and level monthly contributions at
/// the real return converted to a monthly rate, <c>(1 + r)^(1/12) − 1</c>, so the twelve-month figure and the
/// financial-independence date read off the same arithmetic and the same assumptions (gap G7).
/// </summary>
public static class Projection
{
    /// <summary>
    /// Below this many complete months a median is noise wearing a number's clothes, so there is no projection.
    /// </summary>
    public const int MinCompleteMonths = 3;

    /// <summary>How many complete months the savings path looks back over (the in-progress month is never counted).</summary>
    public const int LookbackMonths = 6;

    private const int MonthsPerYear = 12;

    /// <summary>
    /// The savings path from the net saving of each complete month, or null below <see cref="MinCompleteMonths"/>.
    /// </summary>
    public static SavingsPath? SavingsPathOf(IReadOnlyCollection<decimal> completeMonthNetUsd)
        => completeMonthNetUsd.Count < MinCompleteMonths
            ? null
            : new SavingsPath(Median(completeMonthNetUsd), completeMonthNetUsd.Count);

    /// <summary>
    /// Median of a non-empty sample; even-length samples average the two middle values. A median rather than a
    /// mean, because the window is a handful of months deep and one artifact month (a duplicated salary) moves a
    /// mean while leaving the median where the typical month sits.
    /// </summary>
    public static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            throw new ArgumentException("A median needs at least one value.", nameof(values));

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2m
            : sorted[mid];
    }

    /// <summary>
    /// Value after <paramref name="months"/> at each of the three return assumptions, in today's money (the return
    /// is real).
    /// </summary>
    public static ProjectionRange ValueAfter(
        int months, decimal currentUsd, decimal monthlySavingsUsd, ProjectionAssumptions assumptions)
    {
        var returns = assumptions.RealAnnualReturns;
        return ProjectionRange.Of(
            FutureValue(currentUsd, monthlySavingsUsd, returns.Low, months),
            FutureValue(currentUsd, monthlySavingsUsd, returns.Central, months),
            FutureValue(currentUsd, monthlySavingsUsd, returns.High, months));
    }

    /// <summary>
    /// Financial independence: the target is <paramref name="annualSpendUsd"/> ÷ the safe withdrawal rate (a
    /// non-positive rate is guarded to a zero target, which reads as already reached), and the months to it are
    /// solved at each of the three return assumptions. Whether the target is reachable does not depend on the
    /// return: net worth at or above it is already reached, savings at or below zero is not saving.
    /// </summary>
    public static FireProjectionResult FinancialIndependence(
        decimal annualSpendUsd, decimal currentUsd, decimal monthlySavingsUsd, ProjectionAssumptions assumptions)
    {
        var target = assumptions.SafeWithdrawalRate <= 0m ? 0m : annualSpendUsd / assumptions.SafeWithdrawalRate;

        if (currentUsd >= target)
            return new FireProjectionResult(FireReachability.AlreadyReached, target, new ProjectionRange(0m, 0m, 0m));

        if (monthlySavingsUsd <= 0m)
            return new FireProjectionResult(FireReachability.NotSaving, target, null);

        var returns = assumptions.RealAnnualReturns;
        return new FireProjectionResult(
            FireReachability.Reachable,
            target,
            ProjectionRange.Of(
                MonthsToTarget(target, currentUsd, monthlySavingsUsd, returns.Low),
                MonthsToTarget(target, currentUsd, monthlySavingsUsd, returns.Central),
                MonthsToTarget(target, currentUsd, monthlySavingsUsd, returns.High)));
    }

    /// <summary>
    /// <c>NW·(1 + rm)^n + c·((1 + rm)^n − 1) / rm</c>: the current balance and a level monthly contribution, both
    /// compounding at <paramref name="realAnnualReturn"/>'s monthly rate; <c>NW + c·n</c> at a zero return.
    /// </summary>
    private static decimal FutureValue(decimal currentUsd, decimal monthlySavingsUsd, decimal realAnnualReturn, int months)
    {
        var monthlyReturn = MonthlyRateFromAnnual(realAnnualReturn);
        if (monthlyReturn == 0m)
            return currentUsd + monthlySavingsUsd * months;

        var growth = (decimal)Math.Pow(1 + (double)monthlyReturn, months);
        return currentUsd * growth + monthlySavingsUsd * (growth - 1m) / monthlyReturn;
    }

    /// <summary>
    /// <c>ln((target·rm + c) / (NW·rm + c)) / ln(1 + rm)</c>: the future-value solve for months to reach
    /// <paramref name="target"/>; <c>(target − NW) / c</c> at a zero return.
    /// </summary>
    private static decimal MonthsToTarget(
        decimal target, decimal currentUsd, decimal monthlySavingsUsd, decimal realAnnualReturn)
    {
        var monthlyReturn = MonthlyRateFromAnnual(realAnnualReturn);
        if (monthlyReturn == 0m)
            return (target - currentUsd) / monthlySavingsUsd;

        var denominator = currentUsd * monthlyReturn + monthlySavingsUsd;

        // A deeply negative net worth can drive the compounding denominator non-positive, which the closed form
        // cannot take a logarithm of. The zero-return formula is a safe, conservative fallback for that edge: it
        // ignores the (unreachable) compounding benefit rather than producing NaN.
        if (denominator <= 0m)
            return (target - currentUsd) / monthlySavingsUsd;

        var numerator = (double)(target * monthlyReturn + monthlySavingsUsd);
        return (decimal)(Math.Log(numerator / (double)denominator) / Math.Log(1 + (double)monthlyReturn));
    }

    /// <summary>Converts a real annual return to the equivalent compounding monthly rate: <c>(1 + r)^(1/12) − 1</c>.</summary>
    private static decimal MonthlyRateFromAnnual(decimal realAnnualReturn)
    {
        if (realAnnualReturn == 0m) return 0m;
        return (decimal)(Math.Pow(1 + (double)realAnnualReturn, 1.0 / MonthsPerYear) - 1);
    }
}
