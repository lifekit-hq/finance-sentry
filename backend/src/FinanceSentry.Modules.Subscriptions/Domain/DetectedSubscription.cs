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
    /// <summary>True when the user added this by hand — detection never overwrites or auto-stales it.</summary>
    public bool IsManual { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DismissedAt { get; private set; }

    /// <summary>Payments remaining, or null when neither the end date nor the term is known.</summary>
    public int? RemainingPayments =>
        EndDate is DateOnly end
            ? Math.Max(0, ((end.Year - LastChargeDate.Year) * 12) + end.Month - LastChargeDate.Month)
            : TermCount is int term ? Math.Max(0, term - OccurrenceCount) : null;

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
        $"manual:{kind}:{display.Trim().ToLowerInvariant()}";

    public void UpdateFromDetection(
        string merchantNameDisplay,
        decimal averageAmount,
        decimal lastKnownAmount,
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
