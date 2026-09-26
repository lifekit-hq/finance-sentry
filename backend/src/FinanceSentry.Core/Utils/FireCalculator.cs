namespace FinanceSentry.Core.Utils;

/// <summary>
/// How a FIRE (financial independence) projection resolved: a reachable date, or one of the
/// two states that make a date meaningless — each rendered as its own tile state rather than
/// a number a reader would have to interpret.
/// </summary>
public enum FireReachability
{
    /// <summary>Reachable at a specific number of months out.</summary>
    Reachable,

    /// <summary>Current net worth already meets or exceeds the target.</summary>
    AlreadyReached,

    /// <summary>Monthly savings are zero or negative — no rate of progress to project from.</summary>
    NotSaving,
}

/// <summary>
/// <paramref name="Target"/> is always populated (it needs only the annual spend and the
/// withdrawal rate); <paramref name="MonthsToFire"/> is populated whenever
/// <paramref name="Status"/> is not <see cref="FireReachability.NotSaving"/> — zero for
/// <see cref="FireReachability.AlreadyReached"/>.
/// </summary>
public readonly record struct FireCalculationResult(
    FireReachability Status,
    decimal Target,
    decimal? MonthsToFire);

/// <summary>
/// Months to financial independence from the standard future-value solve for an annuity with
/// a lump sum already in hand, compounding future contributions at a real (inflation-adjusted)
/// monthly return — deliberately unlike the 12-month net-worth projection tile, which does not
/// compound contributions because a rounding error at a 1-year horizon is most of the answer at
/// a FIRE horizon (years to decades).
/// </summary>
public static class FireCalculator
{
    /// <param name="annualSpend">Today's annual spend in the target currency; the FIRE target
    /// is <c>annualSpend / safeWithdrawalRate</c>.</param>
    /// <param name="safeWithdrawalRate">Fraction of the target portfolio withdrawn per year
    /// (e.g. 0.04 for the classic 4% rule). Non-positive is guarded to a zero target rather
    /// than dividing by zero.</param>
    /// <param name="monthlySavings">Median monthly net savings (contributions) over complete
    /// months. Non-positive yields <see cref="FireReachability.NotSaving"/> — there is no rate
    /// to compound.</param>
    /// <param name="currentNetWorth">Current total net worth.</param>
    /// <param name="realAnnualReturn">Real (inflation-adjusted) annual return assumption on the
    /// whole portfolio, applied to both the current balance and future contributions.</param>
    public static FireCalculationResult Calculate(
        decimal annualSpend,
        decimal safeWithdrawalRate,
        decimal monthlySavings,
        decimal currentNetWorth,
        decimal realAnnualReturn)
    {
        var target = safeWithdrawalRate <= 0m ? 0m : annualSpend / safeWithdrawalRate;

        if (currentNetWorth >= target)
            return new FireCalculationResult(FireReachability.AlreadyReached, target, 0m);

        if (monthlySavings <= 0m)
            return new FireCalculationResult(FireReachability.NotSaving, target, null);

        var monthlyReturn = MonthlyRateFromAnnual(realAnnualReturn);

        var monthsToFire = monthlyReturn == 0m
            ? (target - currentNetWorth) / monthlySavings
            : ClosedFormMonths(target, currentNetWorth, monthlySavings, monthlyReturn);

        return new FireCalculationResult(FireReachability.Reachable, target, monthsToFire);
    }

    /// <summary>
    /// <c>ln((target·rm + c) / (NW·rm + c)) / ln(1 + rm)</c> — the future-value solve for months
    /// to reach <paramref name="target"/> given a starting balance and a level monthly
    /// contribution, both compounding at <paramref name="monthlyReturn"/>.
    /// </summary>
    private static decimal ClosedFormMonths(
        decimal target, decimal currentNetWorth, decimal monthlySavings, decimal monthlyReturn)
    {
        var denominator = currentNetWorth * monthlyReturn + monthlySavings;

        // A deeply negative net worth can drive the compounding denominator non-positive, which
        // the closed form cannot take a logarithm of. The zero-return formula is a safe,
        // conservative fallback for that edge — it ignores the (unreachable) compounding benefit
        // rather than producing NaN.
        if (denominator <= 0m)
            return (target - currentNetWorth) / monthlySavings;

        var numerator = (double)(target * monthlyReturn + monthlySavings);
        return (decimal)(Math.Log(numerator / (double)denominator) / Math.Log(1 + (double)monthlyReturn));
    }

    /// <summary>Converts a real annual return to the equivalent compounding monthly rate: <c>(1 + r)^(1/12) − 1</c>.</summary>
    private static decimal MonthlyRateFromAnnual(decimal realAnnualReturn)
    {
        if (realAnnualReturn == 0m) return 0m;
        return (decimal)(Math.Pow(1 + (double)realAnnualReturn, 1.0 / 12) - 1);
    }
}
