namespace FinanceSentry.Core.Interfaces;

public interface ISubscriptionDetectionResultService
{
    Task UpsertDetectedSubscriptionsAsync(
        string userId,
        IReadOnlyList<DetectedSubscriptionData> results,
        CancellationToken ct = default);

    Task MarkStaleAsPotentiallyCancelledAsync(
        string userId,
        CancellationToken ct = default);
}

// PreviousAmount is the price the merchant billed before its most recent price step, while
// that step is still recent; null when only one price was ever billed. A hike is measured
// against it — AverageAmount averages the current price alone and so can never show a step.
public record DetectedSubscriptionData(
    string MerchantNameNormalized,
    string MerchantNameDisplay,
    string Cadence,
    decimal AverageAmount,
    decimal LastKnownAmount,
    string Currency,
    DateOnly LastChargeDate,
    DateOnly NextExpectedDate,
    int OccurrenceCount,
    int ConfidenceScore,
    string? Category,
    string Kind = SubscriptionKinds.Subscription,
    bool IsCompleted = false,
    decimal? PreviousAmount = null);

/// <summary>
/// Distinguishes an open-ended recurring service from a fixed-term installment
/// (розстрочка) repayment. Both are detected by the same recurrence heuristic
/// but surfaced in separate UI sections.
/// </summary>
public static class SubscriptionKinds
{
    public const string Subscription = "subscription";
    public const string Installment = "installment";
}
