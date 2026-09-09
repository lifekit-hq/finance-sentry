namespace FinanceSentry.Core.Interfaces;

public interface ISubscriptionHygieneSummaryReader
{
    /// <summary>
    /// Returns all active subscriptions and installments across all users with the data needed
    /// for hygiene detection (price hike). All users are returned in a single query; the caller
    /// groups by UserId.
    /// </summary>
    Task<IReadOnlyList<SubscriptionHygieneSummary>> GetAllActiveAsync(CancellationToken ct = default);
}

/// <summary>Flat summary of one active subscription or installment, scoped to a single user.</summary>
public record SubscriptionHygieneSummary(
    Guid Id,
    Guid UserId,
    string MerchantNameDisplay,
    decimal AverageAmount,
    decimal LastKnownAmount,
    string Currency,
    int OccurrenceCount,
    string Kind,
    decimal? PreviousAmount = null)
{
    /// <summary>
    /// What a price hike is measured against: the price billed before the most recent step
    /// when detection saw one, otherwise the running average. Averaging alone cannot expose a
    /// hike — the charge that raised the price is itself in the average that dilutes it.
    /// </summary>
    public decimal HikeBaseline => PreviousAmount ?? AverageAmount;
}
