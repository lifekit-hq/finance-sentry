namespace FinanceSentry.Modules.Subscriptions.API.Responses;

/// <summary>
/// What exchange-rate movement has done to one plan billed in a currency other than the user's base
/// currency. The native payment is fixed; only what it costs in the base currency moves. Savings are
/// positive when the plan got cheaper in the base currency than it was at signing, negative when dearer.
/// </summary>
/// <param name="SigningDate">
/// The date the comparison starts from: the plan's user-set start date when known, otherwise the first
/// charge detection actually observed.
/// </param>
/// <param name="SigningDateIsObserved">
/// True when the signing date is only the first *observed* charge rather than the real start — detection
/// sees ~13 months, so an older plan understates how far the rate has moved until its start date is set.
/// </param>
/// <param name="SigningUnitsPerBase">Units of the plan's currency per 1 base unit on the signing date.</param>
/// <param name="CurrentUnitsPerBase">The same quote at today's (or the latest published) rate.</param>
/// <param name="PaymentCostAtSigning">One payment in the base currency at the signing rate.</param>
/// <param name="PaymentCostNow">One payment in the base currency at today's rate.</param>
/// <param name="CostChangePercent">Change of <paramref name="PaymentCostNow"/> against <paramref name="PaymentCostAtSigning"/>.</param>
/// <param name="PaymentsMade">Payments already made, counted from the signing date.</param>
/// <param name="PaymentsRemaining">Payments left, or null when neither the term nor the end date is known.</param>
/// <param name="RemainingCostAtSigningRate">What the remaining payments would cost at the signing rate; null with no known remainder.</param>
/// <param name="RemainingCostAtTodayRate">What the remaining payments cost at today's rate; null with no known remainder.</param>
/// <param name="RemainingSaving">Signing-rate cost minus today's-rate cost of the remaining payments; null with no known remainder.</param>
/// <param name="PaidSaving">What the payments already made cost at the signing rate, minus what they actually cost at each payment date's rate.</param>
/// <param name="Rates">The quote over the plan's life, monthly from the signing date to today, for a sparkline.</param>
public record FxImpactPlan(
    Guid Id,
    string Merchant,
    string Kind,
    string Currency,
    decimal PaymentNative,
    DateOnly SigningDate,
    bool SigningDateIsObserved,
    decimal SigningUnitsPerBase,
    decimal CurrentUnitsPerBase,
    decimal PaymentCostAtSigning,
    decimal PaymentCostNow,
    decimal CostChangePercent,
    int PaymentsMade,
    int? PaymentsRemaining,
    decimal? RemainingCostAtSigningRate,
    decimal? RemainingCostAtTodayRate,
    decimal? RemainingSaving,
    decimal PaidSaving,
    IReadOnlyList<FxRatePoint> Rates);

public record FxRatePoint(DateOnly Date, decimal UnitsPerBase);

/// <param name="HasUnknownRemainder">True when a plan has no known remainder, so <paramref name="RemainingSaving"/> leaves it out.</param>
public record FxImpactTotals(
    decimal PaidSaving,
    decimal RemainingSaving,
    decimal TotalSaving,
    bool HasUnknownRemainder);

/// <summary>
/// The FX insight for the Subscriptions page. <paramref name="Plans"/> holds only plans the insight
/// applies to (active, billed in a currency other than USD, EUR or the base currency, with a published
/// rate history), so an empty list means the widget has nothing to show.
/// </summary>
public record FxImpactResponse(
    string BaseCurrency,
    IReadOnlyList<FxImpactPlan> Plans,
    FxImpactTotals Totals);
