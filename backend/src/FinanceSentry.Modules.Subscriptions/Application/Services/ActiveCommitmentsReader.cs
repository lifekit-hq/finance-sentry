namespace FinanceSentry.Modules.Subscriptions.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

/// <summary>
/// <see cref="IActiveCommitmentsReader"/> over the user's detected commitments, active or ended. Its callers include the budget
/// breach job with no person in scope, so the read is unscoped and keyed by the user it is given.
/// </summary>
public class ActiveCommitmentsReader(IDetectedSubscriptionRepository repository) : IActiveCommitmentsReader
{
    public async Task<IReadOnlyList<ActiveCommitment>> GetActiveCommitmentsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subscriptions = await repository.GetActiveByUserIdUnscopedAsync(userId.ToString(), ct);
        return subscriptions.Select(ToCommitment).ToList();
    }

    public async Task<IReadOnlyList<ActiveCommitment>> GetEndedCommitmentsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subscriptions = await repository.GetEndedByUserIdUnscopedAsync(userId.ToString(), ct);
        return subscriptions.Select(ToCommitment).ToList();
    }

    private static ActiveCommitment ToCommitment(DetectedSubscription s) => new(
        s.MerchantNameNormalized,
        s.MerchantNameDisplay,
        s.IsTracked,
        s.Kind,
        s.Cadence,
        s.LastKnownAmount,
        s.Currency,
        s.EndDate);
}
