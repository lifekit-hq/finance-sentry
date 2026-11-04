namespace FinanceSentry.Core.Utils;

/// <summary>
/// The relative spread a detected subscription's charge may show and still be one price. Detection admits a
/// series whose current charges vary by up to this much (a metered bill, an FX-converted charge), so a change
/// between two consecutive charges inside it is that same variance, not a repricing.
/// </summary>
public static class SubscriptionPriceBand
{
    public const decimal MaxRelativeVariation = 0.10m;
}
