namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;

/// <summary>
/// Maps a transaction to the key of the detected commitment it would belong to, so callers can
/// ask "is this outflow one of the user's active commitments?" by set membership.
/// <para>
/// <b>This must mirror <c>SubscriptionDetectionJob</c>'s routing exactly.</b> The job splits a
/// user's debits into installment repayments (keyed by
/// <see cref="InstallmentPlanRecognizer.PlanKey"/>) and everything else (keyed by
/// <see cref="MerchantNameNormalizer.NormalizeDetectionKey"/>), and stores the resulting key on
/// <c>DetectedSubscription.MerchantNameNormalized</c>. A resolver that keyed every transaction
/// as a merchant would never match an installment plan, because no merchant key ever takes the
/// <c>installment:{merchant}:{amount}</c> form.
/// </para>
/// </summary>
public static class CommitmentKeyResolver
{
    /// <summary>
    /// The detected-commitment key for a transaction. Always returns a key: a transaction that
    /// is not an installment repayment falls back to its normalized merchant key, which is what
    /// the recurring-service detector would have grouped it under.
    /// </summary>
    public static string Resolve(DetectionPhraseSet phrases, string? merchantName, string? description, decimal amount, int? mcc) =>
        InstallmentPlanRecognizer.PlanKeyForTransaction(phrases, description, amount, mcc)
        ?? MerchantNameNormalizer.NormalizeDetectionKey(merchantName, description);
}
