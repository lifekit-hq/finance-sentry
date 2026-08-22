namespace FinanceSentry.Modules.Liquidity.Application.Services;

using FinanceSentry.Core.Interfaces;

/// <summary>
/// Pure arithmetic projection — no DB access, no DI, trivially testable.
/// Projects an account's balance 30 days forward by subtracting recurring outflows
/// on their expected dates (same currency only).
/// </summary>
public static class CashFlowProjectionService
{
    private const int ProjectionDays = 30;

    /// <param name="balance">Current account balance (must not be null).</param>
    /// <param name="accountCurrency">ISO currency code of the account.</param>
    /// <param name="subscriptions">Active subscriptions (all currencies; cross-currency are filtered).</param>
    /// <param name="from">The projection start date (typically today).</param>
    /// <returns>First-shortfall result, or null when the balance stays non-negative throughout.</returns>
    public static ShortfallResult? Project(
        decimal balance,
        string accountCurrency,
        IReadOnlyList<ActiveSubscriptionSummary> subscriptions,
        DateOnly from)
    {
        var sameCurrencyOutflows = subscriptions
            .Where(s => string.Equals(s.Currency, accountCurrency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var running = balance;

        for (var i = 1; i <= ProjectionDays; i++)
        {
            var day = from.AddDays(i);

            foreach (var outflow in sameCurrencyOutflows)
            {
                if (outflow.NextExpectedDate == day)
                    running -= outflow.AverageAmount;
            }

            if (running < 0)
                return new ShortfallResult(day, Math.Abs(running));
        }

        return null;
    }
}

/// <param name="Date">The first calendar day the projected balance goes negative.</param>
/// <param name="Amount">How much money is needed on that day (positive value).</param>
public record ShortfallResult(DateOnly Date, decimal Amount);
