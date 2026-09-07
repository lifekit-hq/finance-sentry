namespace FinanceSentry.Core.Interfaces;

public interface IActiveSubscriptionsReader
{
    Task<IReadOnlyList<ActiveSubscriptionSummary>> GetActiveSubscriptionsAsync(
        Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Normalized merchant keys of every ACTIVE detected commitment — subscriptions and
    /// installments alike, unlike <see cref="GetActiveSubscriptionsAsync"/> which is scoped to
    /// recurring services for cash-flow projection. The keys are the ones the detector stored,
    /// so callers match against them by deriving the same key from a transaction (see
    /// <c>MerchantNameNormalizer.NormalizeDetectionKey</c>). Comparison is ordinal — both sides
    /// are already lower-cased by normalization.
    /// </summary>
    Task<IReadOnlySet<string>> GetActiveCommitmentMerchantKeysAsync(
        Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The <c>installment</c>-kind half of <see cref="GetActiveCommitmentMerchantKeysAsync"/>:
    /// the user's ACTIVE repayment obligations only — розстрочка plans and loans the detector
    /// recognised behind a masked card number (the mortgage). Categorization reads these to
    /// tell a repayment apart from a genuine transfer on the same MCC, so the kind filter is
    /// load-bearing: a <c>subscription</c>-kind commitment (Netflix, a recurring transfer to a
    /// person) is a recurring charge, not a debt being repaid, and must not be pulled in.
    /// </summary>
    Task<IReadOnlyList<ActiveInstallmentPlan>> GetActiveInstallmentPlansAsync(
        Guid userId, CancellationToken ct = default);
}

/// <summary>
/// An active repayment obligation: the key the detector stored it under, and the amount its
/// charges cluster around. The amount is part of the plan's identity, not decoration — a
/// masked-card key is only the card's leading digits (<c>MerchantNameNormalizer</c> strips the
/// trailing group), so several cards share one key and only the amount tells the mortgage
/// apart from a small transfer to a friend's card at the same bank.
/// </summary>
public record ActiveInstallmentPlan(string Key, decimal ExpectedAmount);

public record ActiveSubscriptionSummary(
    string MerchantNameDisplay,
    string Cadence,
    decimal AverageAmount,
    string Currency,
    DateOnly NextExpectedDate);
