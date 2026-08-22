namespace FinanceSentry.Core.Interfaces;

public interface IActiveSubscriptionsReader
{
    Task<IReadOnlyList<ActiveSubscriptionSummary>> GetActiveSubscriptionsAsync(
        Guid userId, CancellationToken ct = default);
}

public record ActiveSubscriptionSummary(
    string MerchantNameDisplay,
    string Cadence,
    decimal AverageAmount,
    string Currency,
    DateOnly NextExpectedDate);
