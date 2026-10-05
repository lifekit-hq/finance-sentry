namespace FinanceSentry.Modules.Subscriptions.Domain;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;

public class DetectedSubscription
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string UserId { get; private set; } = string.Empty;
    public string MerchantNameNormalized { get; private set; } = string.Empty;
    public string MerchantNameDisplay { get; private set; } = string.Empty;
    public string Cadence { get; private set; } = string.Empty;
    public decimal AverageAmount { get; private set; }
    public decimal LastKnownAmount { get; private set; }
    /// <summary>
    /// The price billed before the most recent price step, while that step is still recent;
    /// null when only one price was ever billed. <see cref="AverageAmount"/> averages the
    /// current price alone, so it is this — not the average — that a hike is measured against.
    /// </summary>
    public decimal? PreviousAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    /// <summary>"subscription" (open-ended service) or "installment" (fixed-term розстрочка).</summary>
    public string Kind { get; private set; } = SubscriptionKinds.Subscription;
    public DateOnly LastChargeDate { get; private set; }
    public DateOnly NextExpectedDate { get; private set; }
    public string Status { get; private set; } = SubscriptionStatus.Active;
    public int OccurrenceCount { get; private set; }
    public int ConfidenceScore { get; private set; }
    public string? Category { get; private set; }
    /// <summary>Installment plan length in payments, if known (user-set). Enables remaining/auto-complete.</summary>
    public int? TermCount { get; private set; }
    /// <summary>
    /// Final payment month, if known (user-set). For long obligations (a mortgage) the
    /// detector's lookback window can't count payments made, so remaining derives from
    /// this date instead of <see cref="TermCount"/>.
    /// </summary>
    public DateOnly? EndDate { get; private set; }
    /// <summary>
    /// When the plan actually began (user-set). Detection only sees the last 13 months, so
    /// an older plan's first observed charge is not its start — this anchors it, which
    /// matters for pricing the plan against the exchange rate at signing.
    /// </summary>
    public DateOnly? StartDate { get; private set; }
    /// <summary>
    /// True when the user added this rather than detection finding it; detection never
    /// overwrites it. A row added from a picked transaction is still tracked by its charges
    /// (<see cref="IsTracked"/>); a legacy hand-typed row is not.
    /// </summary>
    public bool IsManual { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DismissedAt { get; private set; }

    /// <summary>Payments remaining, or null when neither the end date nor the term is known.</summary>
    public int? RemainingPayments =>
        EndDate is DateOnly end
            ? Math.Max(0, ((end.Year - LastChargeDate.Year) * 12) + end.Month - LastChargeDate.Month)
            : TermCount is int term ? Math.Max(0, term - OccurrenceCount) : null;

    /// <summary>
    /// Whether the row follows the user's transactions: every detected row, and a manual row
    /// added from a picked transaction (keyed as the detection job keys that transaction's
    /// charges). Only a legacy hand-typed row (<c>manual:{kind}:{merchant}</c>) matches no charge,
    /// so its dates stay where they were typed.
    /// </summary>
    public bool IsTracked => !MerchantNameNormalized.StartsWith(LegacyManualKeyPrefix, StringComparison.Ordinal);

    private const string LegacyManualKeyPrefix = "manual:";

    private DetectedSubscription() { }

    public static DetectedSubscription Create(
        string userId,
        string merchantNameNormalized,
        string merchantNameDisplay,
        string cadence,
        decimal averageAmount,
        decimal lastKnownAmount,
        string currency,
        DateOnly lastChargeDate,
        DateOnly nextExpectedDate,
        int occurrenceCount,
        int confidenceScore,
        string? category,
        string kind = SubscriptionKinds.Subscription,
        bool isCompleted = false,
        decimal? previousAmount = null)
    {
        var entity = new DetectedSubscription
        {
            UserId = userId,
            MerchantNameNormalized = merchantNameNormalized,
            MerchantNameDisplay = merchantNameDisplay,
            Cadence = cadence,
            AverageAmount = averageAmount,
            LastKnownAmount = lastKnownAmount,
            PreviousAmount = previousAmount,
            Currency = currency,
            LastChargeDate = lastChargeDate,
            NextExpectedDate = nextExpectedDate,
            OccurrenceCount = occurrenceCount,
            ConfidenceScore = confidenceScore,
            Category = category,
            Kind = kind,
        };
        entity.EvaluateCompletion(isCompleted);
        return entity;
    }

    /// <summary>
    /// Creates a user-entered recurring item (manual, never overwritten by detection).
    /// Use for a subscription detection can't reliably find (e.g. an irregular Claude
    /// plan) or an installment it missed.
    /// </summary>
    public static DetectedSubscription CreateManual(
        string userId,
        string merchantNameDisplay,
        decimal monthlyAmount,
        string currency,
        DateOnly startDate,
        int? termCount,
        string kind = SubscriptionKinds.Installment)
    {
        var entity = new DetectedSubscription
        {
            UserId = userId,
            MerchantNameNormalized = MerchantNameKey(merchantNameDisplay, kind),
            MerchantNameDisplay = merchantNameDisplay,
            Cadence = "monthly",
            AverageAmount = monthlyAmount,
            LastKnownAmount = monthlyAmount,
            Currency = currency,
            LastChargeDate = startDate,
            NextExpectedDate = startDate.AddMonths(1),
            OccurrenceCount = 1,
            ConfidenceScore = 100,
            Category = null,
            Kind = kind,
            TermCount = kind == SubscriptionKinds.Installment ? termCount : null,
            IsManual = true,
        };
        entity.EvaluateCompletion(false);
        return entity;
    }

    private static string MerchantNameKey(string display, string kind) =>
        $"{LegacyManualKeyPrefix}{kind}:{display.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Creates a user-added commitment from one of their transactions. It is keyed as the
    /// detection job keys that transaction's charges, so each later matching charge advances
    /// it (<see cref="RecordCharge"/>) and it lapses like a detected row when the charges stop.
    /// </summary>
    public static DetectedSubscription CreateFromTransaction(
        string userId,
        string trackingKey,
        string merchantNameDisplay,
        decimal monthlyAmount,
        string currency,
        DateOnly chargeDate,
        int chargeCount,
        int? termCount,
        string kind,
        string cadence = SubscriptionCadences.Monthly)
    {
        var entity = new DetectedSubscription { UserId = userId };
        entity.TrackFromTransaction(
            trackingKey, merchantNameDisplay, monthlyAmount, currency, chargeDate, chargeCount,
            termCount, null, null, kind, cadence);
        return entity;
    }

    /// <summary>
    /// Points the row at one of the user's transactions and makes it theirs: keyed by that
    /// transaction's charges (so later ones advance it), active again, and no longer something
    /// detection may overwrite. <paramref name="chargeCount"/> is how many charges under the key
    /// the picked one is, so installment progress starts from the payments already made.
    /// </summary>
    public void TrackFromTransaction(
        string trackingKey,
        string merchantNameDisplay,
        decimal monthlyAmount,
        string currency,
        DateOnly chargeDate,
        int chargeCount,
        int? termCount,
        DateOnly? startDate,
        DateOnly? endDate,
        string kind,
        string cadence = SubscriptionCadences.Monthly)
    {
        MerchantNameNormalized = trackingKey;
        MerchantNameDisplay = merchantNameDisplay;
        Cadence = cadence;
        AverageAmount = monthlyAmount;
        LastKnownAmount = monthlyAmount;
        PreviousAmount = null;
        Currency = currency;
        LastChargeDate = chargeDate;
        NextExpectedDate = NextChargeAfter(chargeDate);
        OccurrenceCount = chargeCount;
        ConfidenceScore = 100;
        Kind = kind;
        TermCount = kind == SubscriptionKinds.Installment && termCount is > 0 ? termCount : null;
        StartDate = kind == SubscriptionKinds.Installment ? startDate : null;
        EndDate = kind == SubscriptionKinds.Installment ? endDate : null;
        IsManual = true;
        Status = SubscriptionStatus.Active;
        DismissedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        EvaluateCompletion(false);
    }

    /// <summary>
    /// Advances a tracked row by a charge that resolved to its key: the charge becomes the last
    /// one, the next is expected one cadence later, and a row that had lapsed is active again.
    /// Returns false (and changes nothing) for a charge at or before the last one already seen.
    /// </summary>
    public bool RecordCharge(DateOnly chargeDate, decimal amount, string? currency = null)
    {
        if (chargeDate <= LastChargeDate)
            return false;

        LastChargeDate = chargeDate;
        NextExpectedDate = NextChargeAfter(chargeDate);
        LastKnownAmount = amount;
        AverageAmount = amount;
        // The unit the amounts above are in: billing moved to another account restates them.
        if (!string.IsNullOrWhiteSpace(currency))
            Currency = currency;
        OccurrenceCount++;
        Status = SubscriptionStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
        EvaluateCompletion(false);
        return true;
    }

    private DateOnly NextChargeAfter(DateOnly chargeDate) =>
        Cadence == SubscriptionCadences.Annual ? chargeDate.AddYears(1) : chargeDate.AddMonths(1);

    public void UpdateFromDetection(
        string merchantNameDisplay,
        decimal averageAmount,
        decimal lastKnownAmount,
        string currency,
        DateOnly lastChargeDate,
        DateOnly nextExpectedDate,
        int occurrenceCount,
        int confidenceScore,
        string? category,
        string kind = SubscriptionKinds.Subscription,
        bool isCompleted = false,
        decimal? previousAmount = null)
    {
        // A masked card number must never clobber a human-readable name the row already
        // has (e.g. a mortgage renamed by the user whose charges show only the PAN).
        if (string.IsNullOrWhiteSpace(MerchantNameDisplay) || !MaskedPan.IsLikely(merchantNameDisplay))
            MerchantNameDisplay = merchantNameDisplay;
        AverageAmount = averageAmount;
        LastKnownAmount = lastKnownAmount;
        // The unit the amounts above are in. A merchant whose billing moves to another account
        // restates every one of them, and a row left labelled with the old currency is not
        // merely mislabelled: the spend summaries run ToUsd over Currency, so a EUR amount
        // converted at the GBP rate silently misstates the user's monthly total.
        Currency = currency;
        // Always assigned, never merged: once the new price has settled, detection stops
        // reporting a step and the stale baseline must clear or the sentinel re-fires forever.
        PreviousAmount = previousAmount;
        LastChargeDate = lastChargeDate;
        NextExpectedDate = nextExpectedDate;
        OccurrenceCount = occurrenceCount;
        ConfidenceScore = confidenceScore;
        Category = category;
        Kind = kind;
        Status = SubscriptionStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
        EvaluateCompletion(isCompleted);
    }

    /// <summary>
    /// Sets the installment schedule — total payment count, first payment month and/or
    /// final payment month — and completes the plan if the end has already been reached.
    /// </summary>
    public void SetTerm(int? termCount, DateOnly? endDate = null, DateOnly? startDate = null)
    {
        TermCount = termCount is > 0 ? termCount : null;
        EndDate = endDate;
        StartDate = startDate;
        UpdatedAt = DateTimeOffset.UtcNow;
        EvaluateCompletion(false);
    }

    public void MarkCompleted()
    {
        Status = SubscriptionStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Completes the installment when a payoff was seen, the term has been reached, or the final payment month has passed.</summary>
    private void EvaluateCompletion(bool payoffSeen)
    {
        if (payoffSeen
            || (TermCount is int term && OccurrenceCount >= term)
            || (EndDate is DateOnly end && LastChargeDate >= end))
        {
            Status = SubscriptionStatus.Completed;
        }
    }

    public void MarkDismissed()
    {
        Status = SubscriptionStatus.Dismissed;
        DismissedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Restore()
    {
        Status = SubscriptionStatus.Active;
        DismissedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPotentiallyCancelled()
    {
        Status = SubscriptionStatus.PotentiallyCancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
