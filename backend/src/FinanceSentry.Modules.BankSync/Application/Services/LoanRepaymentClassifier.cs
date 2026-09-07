namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;

/// <summary>
/// Classifies loan and installment (розстрочка) repayments that Monobank books under the
/// wire-transfer MCC 4829 — the same MCC it gives a genuine card-to-card transfer. Left to the
/// MCC map those repayments become <see cref="CategoryKeys.TransferOut"/> and
/// <see cref="CategoryKeys.IsTransfer"/> then drops them from every outflow view, hiding the
/// largest fixed commitment in the book (issue #553).
/// <para>
/// Two signals, in order:
/// <list type="number">
/// <item>the description itself — <see cref="InstallmentPlanRecognizer.IsInstallmentTransaction"/>
/// («Погашення наступного платежу …», "- monomarket", «Платіж &lt;merchant&gt;» on MCC 4829);</item>
/// <item>failing that, a match against one of the user's ACTIVE <c>installment</c>-kind plans —
/// same key AND an amount that fits the plan. This is the only way to reach a mortgage, whose
/// description is a masked card number («516936******4992») carrying no wording any keyword
/// could match. The detector already labels such a plan <c>installment</c> (see
/// <c>SubscriptionDetectionJob.DetectSubscriptions</c>), so the categorizer trusts that label
/// rather than re-deriving a masked-PAN heuristic that would also claim one-off card-to-card
/// transfers.</item>
/// </list>
/// </para>
/// <para>
/// A brand-new plan's FIRST repayment is not yet in the plan set — detection runs after ingest —
/// so it lands as a transfer until the next detection pass, after which the recategorization path
/// re-resolves it. That lag is inherent to a detector-driven rule, not a defect to route around.
/// </para>
/// </summary>
public static class LoanRepaymentClassifier
{
    private const string CreditTransactionType = "credit";

    // A plan only exists because its charges cluster tightly around one amount — the detector
    // rejects anything with a coefficient of variation above 0.10 — so a debit far off that
    // amount is not a repayment of it. The band is load-bearing, not a nicety: a masked-card
    // plan's key is only the card's leading digits (MerchantNameNormalizer strips the trailing
    // group, so «516936******4992» keys as "516936"), which every card issued on the same BIN
    // shares. Without the amount test a ₴200 transfer to a friend's card would be booked as a
    // mortgage payment.
    //
    // The band is deliberately no wider than the detector's own stability gate: a charge that
    // drifts outside it falls back to TRANSFER_OUT and is missed, which the next detection pass
    // plus a re-categorization repairs, whereas a band wide enough to swallow a large transfer
    // to a relative misstates spending with nothing to repair it.
    private const decimal PlanAmountTolerance = 0.15m;

    /// <summary>
    /// Returns <see cref="CategoryKeys.LoanPayments"/> for a repayment, or <c>null</c> when the
    /// transaction is something else and the normal ladder should decide. Callers splice this
    /// in FRONT of the MCC map so the rule takes precedence over MCC 4829 → TRANSFER_OUT.
    /// <para>
    /// Credits are never claimed: the rule is about outflow, and money arriving on MCC 4829 (an
    /// installment refund, an incoming card-to-card) is not a repayment. A null
    /// <paramref name="transactionType"/> counts as an outflow, matching the way
    /// <c>SubscriptionDetectionJob</c> reads the same legacy rows.
    /// </para>
    /// </summary>
    public static string? Resolve(
        string? transactionType,
        string? merchantName,
        string? description,
        decimal amount,
        int? mcc,
        IReadOnlyList<ActiveInstallmentPlan> activeInstallmentPlans)
    {
        ArgumentNullException.ThrowIfNull(activeInstallmentPlans);

        if (string.Equals(transactionType, CreditTransactionType, StringComparison.Ordinal))
            return null;

        if (InstallmentPlanRecognizer.IsInstallmentTransaction(description, mcc))
            return CategoryKeys.LoanPayments;

        // Only merchant-shaped keys are reachable here — anything the recognizer could plan-key
        // already returned above — but the resolver stays the single definition of "this
        // transaction's commitment key", so the two sides cannot drift if the markers change.
        var commitmentKey = CommitmentKeyResolver.Resolve(merchantName, description, amount, mcc);
        var matchesAPlan = activeInstallmentPlans.Any(plan =>
            string.Equals(plan.Key, commitmentKey, StringComparison.Ordinal)
            && AmountFitsPlan(amount, plan.ExpectedAmount));

        return matchesAPlan ? CategoryKeys.LoanPayments : null;
    }

    private static bool AmountFitsPlan(decimal amount, decimal expectedAmount) =>
        expectedAmount > 0m
        && Math.Abs(amount - expectedAmount) <= expectedAmount * PlanAmountTolerance;
}
