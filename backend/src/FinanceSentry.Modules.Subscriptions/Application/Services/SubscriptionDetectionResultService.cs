namespace FinanceSentry.Modules.Subscriptions.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

// Its only caller is the subscription detection job, which has no person in scope, so its reads are unscoped and
// keyed by the user it is given.
public class SubscriptionDetectionResultService(IDetectedSubscriptionRepository repository)
    : ISubscriptionDetectionResultService
{
    private readonly IDetectedSubscriptionRepository _repository = repository;

    public async Task UpsertDetectedSubscriptionsAsync(
        string userId,
        IReadOnlyList<DetectedSubscriptionData> results,
        CancellationToken ct = default)
    {
        foreach (var result in results)
        {
            var existing = await _repository.FindByUserAndMerchantUnscopedAsync(
                userId, result.MerchantNameNormalized, ct);

            if (existing is null)
            {
                var subscription = DetectedSubscription.Create(
                    userId,
                    result.MerchantNameNormalized,
                    result.MerchantNameDisplay,
                    result.Cadence,
                    result.AverageAmount,
                    result.LastKnownAmount,
                    result.Currency,
                    result.LastChargeDate,
                    result.NextExpectedDate,
                    result.OccurrenceCount,
                    result.ConfidenceScore,
                    result.Category,
                    result.Kind,
                    result.IsCompleted,
                    result.PreviousAmount);

                await _repository.UpsertAsync(subscription, ct);
            }
            else if (ShouldUpdate(existing, result))
            {
                existing.UpdateFromDetection(
                    result.MerchantNameDisplay,
                    result.AverageAmount,
                    result.LastKnownAmount,
                    result.Currency,
                    result.LastChargeDate,
                    result.NextExpectedDate,
                    result.OccurrenceCount,
                    result.ConfidenceScore,
                    result.Category,
                    result.Kind,
                    result.IsCompleted,
                    result.PreviousAmount);

                await _repository.UpsertAsync(existing, ct);
            }
        }
    }

    private static bool ShouldUpdate(DetectedSubscription existing, DetectedSubscriptionData result)
    {
        // Never let detection touch a user-owned (manual) or dismissed record.
        if (existing.IsManual || existing.Status == SubscriptionStatus.Dismissed)
            return false;

        // A completed installment stays completed unless a genuinely new payment arrived.
        if (existing.Status == SubscriptionStatus.Completed &&
            result.OccurrenceCount <= existing.OccurrenceCount)
        {
            return false;
        }

        return true;
    }

    public async Task TrackManualCommitmentsAsync(
        string userId,
        IReadOnlyList<CommitmentCharge> charges,
        CancellationToken ct = default)
    {
        var manual = await _repository.GetLiveManualUnscopedAsync(userId, ct);
        if (manual.Count == 0) return;

        var chargesByKey = charges.ToLookup(c => c.Key, StringComparer.Ordinal);

        foreach (var subscription in manual.Where(s => s.IsTracked))
        {
            var advanced = false;
            foreach (var charge in chargesByKey[subscription.MerchantNameNormalized].OrderBy(c => c.Date))
                advanced |= subscription.RecordCharge(charge.Date, charge.Amount, charge.Currency);

            if (advanced)
                await _repository.UpsertAsync(subscription, ct);
        }
    }

    public async Task MarkStaleAsPotentiallyCancelledAsync(
        string userId,
        CancellationToken ct = default)
    {
        var active = await _repository.GetStaleActiveUnscopedAsync(userId, ct);
        var now = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var subscription in active)
        {
            // A legacy hand-typed row matches no charge, so silence is no evidence it stopped.
            if (!subscription.IsTracked) continue;

            var averageIntervalDays = subscription.Cadence == "annual" ? 365 : 30;
            var staleThreshold = subscription.LastChargeDate.AddDays((int)(averageIntervalDays * 1.5));

            if (now > staleThreshold)
            {
                // A silent installment (payments simply stop, e.g. Telemart) has finished;
                // a lapsed subscription is only "potentially cancelled".
                if (subscription.Kind == SubscriptionKinds.Installment)
                    subscription.MarkCompleted();
                else
                    subscription.MarkPotentiallyCancelled();

                await _repository.UpsertAsync(subscription, ct);
            }
        }
    }
}
