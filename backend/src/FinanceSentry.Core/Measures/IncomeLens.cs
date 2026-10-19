namespace FinanceSentry.Core.Measures;

/// <summary>
/// P5, the monthly-income lens (finance-framework §3.7): a large total also shown as the monthly income it could
/// pay, because a lump sum reads larger than the income it buys (Goldstein, Hershfield and Benartzi, 2016; the
/// DOL lifetime income illustration turns a balance into a monthly figure for the same reason). It reads the
/// safe withdrawal rate from the one <see cref="ProjectionAssumptions"/> set (C8), the rate that also turns a
/// year of spending into the financial-independence target, so the lens and the FIRE target are each other's
/// inverse. The withdrawal rate is a single value, so the lens is one figure, not a return range. Pure: no I/O.
/// </summary>
public static class IncomeLens
{
    private const int MonthsPerYear = 12;

    /// <summary>
    /// <c>total × withdrawal rate ÷ 12</c>, in the total's units and in today's money; null when the total is zero or
    /// negative (debt pays no income) or the withdrawal rate is not positive.
    /// </summary>
    public static decimal? MonthlyIncome(decimal totalUsd, ProjectionAssumptions assumptions)
        => totalUsd <= 0m || assumptions.SafeWithdrawalRate <= 0m
            ? null
            : totalUsd * assumptions.SafeWithdrawalRate / MonthsPerYear;
}
