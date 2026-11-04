namespace FinanceSentry.Modules.Subscriptions.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

/// <summary>
/// <see cref="IActiveCommitmentsReader"/> over the user's ACTIVE detected commitments. Its callers include the budget
/// breach job with no person in scope, so the read is unscoped and keyed by the user it is given.
/// </summary>
public class ActiveCommitmentsReader(IDetectedSubscriptionRepository repository) : IActiveCommitmentsReader
{
    public async Task<IReadOnlyList<ActiveCommitment>> GetActiveCommitmentsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subscriptions = await repository.GetActiveByUserIdUnscopedAsync(userId.ToString(), ct);

        return subscriptions
            .Select(s => new ActiveCommitment(
                s.MerchantNameNormalized,
                s.MerchantNameDisplay,
                s.IsTracked,
                s.Kind,
                s.Cadence,
                s.LastKnownAmount,
                s.Currency,
                s.EndDate))
            .ToList();
    }
}
