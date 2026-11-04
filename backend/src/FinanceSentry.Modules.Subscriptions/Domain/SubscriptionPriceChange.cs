namespace FinanceSentry.Modules.Subscriptions.Domain;

using FinanceSentry.Core.Utils;

/// <summary>
/// One observed change in what a detected subscription charges: the previous charge's amount, the new
/// charge's amount, the change in percent and the date of the new charge. Stored for an increase and a
/// decrease alike; only an increase raises an alert.
/// </summary>
public class SubscriptionPriceChange
{
    /// <summary>
    /// The relative difference between two charges that is still the same price: the variance band detection
    /// itself admits for a subscription (a metered bill, a charge converted at a moving FX rate). Anything
    /// above it is a change.
    /// </summary>
    public const decimal Tolerance = SubscriptionPriceBand.MaxRelativeVariation;

    private const int PercentScale = 2;
    private const decimal PercentFactor = 100m;

    public Guid Id { get; private set; } = Guid.NewGuid();
    /// <summary><see cref="DetectedSubscription.UserId"/>'s form: <c>Guid.ToString()</c>.</summary>
    public string UserId { get; private set; } = string.Empty;
    public Guid SubscriptionId { get; private set; }
    public decimal OldAmount { get; private set; }
    public decimal NewAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    /// <summary>Signed: positive for an increase, negative for a decrease.</summary>
    public decimal ChangePercent { get; private set; }
    public DateOnly ChargeDate { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsIncrease => NewAmount > OldAmount;

    private SubscriptionPriceChange() { }

    /// <summary>
    /// The change between two consecutive charges, or null when there is none to record: the new charge is
    /// not a later one, the two are in different currencies (a card moved between accounts restates the same
    /// price, so the numbers are not comparable), or they differ by no more than <see cref="Tolerance"/>.
    /// </summary>
    public static SubscriptionPriceChange? TryCreate(
        string userId,
        Guid subscriptionId,
        decimal oldAmount,
        string oldCurrency,
        DateOnly oldChargeDate,
        decimal newAmount,
        string newCurrency,
        DateOnly newChargeDate)
    {
        if (newChargeDate <= oldChargeDate || oldAmount <= 0m || newAmount <= 0m)
            return null;

        if (!string.Equals(oldCurrency, newCurrency, StringComparison.OrdinalIgnoreCase))
            return null;

        var change = (newAmount - oldAmount) / oldAmount;
        if (Math.Abs(change) <= Tolerance)
            return null;

        return new SubscriptionPriceChange
        {
            UserId = userId,
            SubscriptionId = subscriptionId,
            OldAmount = oldAmount,
            NewAmount = newAmount,
            Currency = newCurrency,
            ChangePercent = Math.Round(change * PercentFactor, PercentScale),
            ChargeDate = newChargeDate,
        };
    }
}
