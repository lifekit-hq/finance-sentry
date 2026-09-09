namespace FinanceSentry.Modules.Subscriptions.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

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
            var existing = await _repository.FindByUserAndMerchantAsync(
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

    public async Task MarkStaleAsPotentiallyCancelledAsync(
        string userId,
        CancellationToken ct = default)
    {
        var active = await _repository.GetStaleActiveAsync(userId, ct);
        var now = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var subscription in active)
        {
            if (subscription.IsManual) continue;

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
