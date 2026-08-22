namespace FinanceSentry.Modules.Subscriptions.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

public class ActiveSubscriptionsReader(IDetectedSubscriptionRepository repository) : IActiveSubscriptionsReader
{
    private readonly IDetectedSubscriptionRepository _repository = repository;

    public async Task<IReadOnlyList<ActiveSubscriptionSummary>> GetActiveSubscriptionsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subscriptions = await _repository.GetActiveByUserIdAsync(userId.ToString(), ct);

        return subscriptions
            .Where(s => s.Kind == SubscriptionKinds.Subscription)
            .Select(s => new ActiveSubscriptionSummary(
                s.MerchantNameDisplay,
                s.Cadence,
                s.AverageAmount,
                s.Currency,
                s.NextExpectedDate))
            .ToList();
    }
}
