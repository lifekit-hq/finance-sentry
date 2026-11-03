namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.BankSync.Domain;

/// <summary>
/// Recognises Monobank installment (розстрочка) repayments and derives the key of the plan a
/// repayment belongs to.
/// <para>
/// Lives in the Application layer rather than in <c>SubscriptionDetectionJob</c> because two
/// callers need it: the detection job (to route installments to their own detector and to key
/// the plans it stores) and <see cref="CommitmentKeyResolver"/> (to decide whether an outflow is
/// committed). One definition means the key a plan is stored under and the key a transaction is
/// matched by cannot drift apart.
/// </para>
/// </summary>
public static class InstallmentPlanRecognizer
{
    // Installment (розстрочка) repayments look exactly like a monthly subscription
    // — fixed amount, monthly, repeated — but they are a fixed-term repayment, not a
    // recurring service. Monobank labels them distinctively (verified against live
    // data): "Погашення наступного платежу RozetkaPay", "Щомісячний платіж telemart
    // - monomarket", "Платіж Pandora", "Повне погашення RozetkaPay", etc.
    // The wording itself is data: the detection_phrases rows of the installment_* kinds.

    public static bool IsInstallmentDescription(DetectionPhraseSet phrases, string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;

        var lowered = description.ToLowerInvariant();
        foreach (var marker in phrases.Phrases(DetectionPhraseKinds.InstallmentMarker))
        {
            if (lowered.Contains(marker, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// True for a Monobank installment repayment: a strong description marker, or a
    /// "Платіж &lt;merchant&gt;" on the internal-transfer MCC (catches merchant payoffs
    /// like "Платіж Pandora" that carry no розстрочка keyword).
    /// </summary>
    public static bool IsInstallmentTransaction(DetectionPhraseSet phrases, string? description, int? mcc)
    {
        if (IsInstallmentDescription(phrases, description)) return true;
        if (description is null) return false;

        var trimmed = description.Trim();
        foreach (var prefix in phrases.Phrases(DetectionPhraseKinds.InstallmentTransferPrefix, mcc))
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static bool IsInstallmentPayoff(DetectionPhraseSet phrases, string? description)
    {
        if (description is null) return false;

        foreach (var marker in phrases.Phrases(DetectionPhraseKinds.InstallmentPayoffMarker))
        {
            if (description.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Recovers the merchant from an installment description by stripping Monobank's
    /// leading phrase ("Погашення наступного платежу", "Щомісячний платіж", "Платіж", …)
    /// and the trailing "- monomarket" marketplace tag.
    /// </summary>
    public static string ExtractMerchant(DetectionPhraseSet phrases, string description)
    {
        var text = description.Trim();

        foreach (var prefix in phrases.Phrases(DetectionPhraseKinds.InstallmentMerchantPrefix))
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        var tagIdx = -1;
        foreach (var tag in phrases.Phrases(DetectionPhraseKinds.InstallmentMerchantTag))
        {
            var at = text.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && (tagIdx < 0 || at < tagIdx))
                tagIdx = at;
        }
        if (tagIdx >= 0)
            text = text[..tagIdx];

        return text.Trim(' ', '-', '\t');
    }

    /// <summary>
    /// Plan-identity amount: rounded to the whole unit, half away from zero, so cent-level
    /// jitter (telemart bills ₴6,499.84 and ₴6,499.85) stays one plan. Must round the same
    /// way as the M004 data migration's SQL <c>round()</c>.
    /// </summary>
    public static int RoundPlanAmount(decimal amount) =>
        (int)Math.Round(amount, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The key an installment plan is stored under on
    /// <c>DetectedSubscription.MerchantNameNormalized</c>. One plan per (merchant, rounded
    /// monthly amount) — the same shop can carry several concurrent розстрочки.
    /// </summary>
    public static string PlanKey(string merchant, decimal amount) =>
        $"installment:{merchant.ToLowerInvariant()}:{RoundPlanAmount(amount)}";

    /// <summary>
    /// The plan key a single repayment belongs to, derived from the transaction alone, or
    /// <c>null</c> when the transaction is not a plan repayment the detector would have keyed.
    /// A full payoff returns <c>null</c>: the detector never stores a plan under a payoff's
    /// own amount — it uses payoffs only to mark a plan completed.
    /// </summary>
    public static string? PlanKeyForTransaction(DetectionPhraseSet phrases, string? description, decimal amount, int? mcc)
    {
        if (!IsInstallmentTransaction(phrases, description, mcc)) return null;
        if (IsInstallmentPayoff(phrases, description)) return null;

        var merchant = ExtractMerchant(phrases, description ?? string.Empty);
        return string.IsNullOrWhiteSpace(merchant) ? null : PlanKey(merchant, amount);
    }
}
