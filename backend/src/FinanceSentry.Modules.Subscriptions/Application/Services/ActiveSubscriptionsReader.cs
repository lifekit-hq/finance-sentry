namespace FinanceSentry.Modules.Subscriptions.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

// Its callers include bank syncs and sentinel jobs with no person in scope, so every read is unscoped and keyed by
// the user it is given.
public class ActiveSubscriptionsReader(IDetectedSubscriptionRepository repository) : IActiveSubscriptionsReader
{
    private readonly IDetectedSubscriptionRepository _repository = repository;

    public async Task<IReadOnlyList<ActiveSubscriptionSummary>> GetActiveSubscriptionsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subscriptions = await _repository.GetActiveByUserIdUnscopedAsync(userId.ToString(), ct);

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

    public async Task<IReadOnlySet<string>> GetActiveCommitmentMerchantKeysAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subscriptions = await _repository.GetActiveByUserIdUnscopedAsync(userId.ToString(), ct);

        return subscriptions
            .Select(s => s.MerchantNameNormalized)
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<string>> GetActiveManualCommitmentMerchantNamesAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subscriptions = await _repository.GetActiveByUserIdUnscopedAsync(userId.ToString(), ct);

        return subscriptions
            .Where(s => s.IsManual && !s.IsTracked)
            .Select(s => s.MerchantNameDisplay)
            .ToList();
    }

    public async Task<IReadOnlyList<ActiveInstallmentPlan>> GetActiveInstallmentPlansAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subscriptions = await _repository.GetActiveByUserIdUnscopedAsync(userId.ToString(), ct);

        return subscriptions
            .Where(s => s.Kind == SubscriptionKinds.Installment)
            .Select(s => new ActiveInstallmentPlan(s.MerchantNameNormalized, s.AverageAmount))
            .ToList();
    }
}
