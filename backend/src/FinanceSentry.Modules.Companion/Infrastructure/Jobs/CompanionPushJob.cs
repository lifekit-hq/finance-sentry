namespace FinanceSentry.Modules.Companion.Infrastructure.Jobs;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Web Push sender (spec 859). Reads the Companion outbox and queues one <see cref="PushDelivery"/> per (event,
/// subscription) for users who opted in, then sends what is due. It never reads or writes
/// <see cref="CompanionEvent.Disposition"/> and ignores <see cref="NotificationMode"/>, quiet hours and the agent
/// rate limit, so it runs alongside the agent/Telegram path without affecting it. Everyone gets their own events;
/// <see cref="CompanionEventKind.OperationalFailure"/> goes to <c>ops.admin</c> holders only. Overlap-protected, and a
/// keyless deployment (no VAPID keys) does nothing.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 120)]
public sealed class CompanionPushJob(
    IPushDeliveryRepository repository,
    IPushSender sender,
    IUserAuthorizationChecker authorization,
    IOptions<WebPushOptions> options,
    ILogger<CompanionPushJob> logger)
{
    private const int EnqueueBatchPerSubscription = 50;
    private const int SendBatch = 200;

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (!options.Value.IsConfigured)
            return;

        var now = DateTimeOffset.UtcNow;
        await EnqueueAsync(now, ct);
        await SendDueAsync(now, ct);
        await repository.PruneDisabledSubscriptionsUnscopedAsync(now - PushDeliveryPolicy.PruneDisabledAfter, ct);
    }

    private async Task EnqueueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var opsByUser = new Dictionary<Guid, bool>();
        var queued = new List<PushDelivery>();

        foreach (var subscription in await repository.ListActiveSubscriptionsUnscopedAsync(ct))
        {
            var isOps = await IsOpsAdminAsync(subscription.UserId, opsByUser, ct);
            // A device that subscribed later does not receive the backlog from before it existed.
            var since = Max(now - PushDeliveryPolicy.EventWindow, subscription.CreatedAt);
            var events = await repository.ListUndeliveredEventsUnscopedAsync(
                subscription.UserId, subscription.Id, since, isOps, EnqueueBatchPerSubscription, ct);
            queued.AddRange(events.Select(e => new PushDelivery
            {
                EventId = e.Id,
                SubscriptionId = subscription.Id,
                UserId = subscription.UserId,
            }));
        }

        if (queued.Count > 0)
            await repository.AddDeliveriesAsync(queued, ct);
    }

    private async Task SendDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await repository.ListDueUnscopedAsync(now, SendBatch, ct);
        if (due.Count == 0)
            return;

        var events = await repository.GetEventsUnscopedAsync([.. due.Select(d => d.EventId).Distinct()], ct);
        var subscriptions = await repository.GetSubscriptionsUnscopedAsync([.. due.Select(d => d.SubscriptionId).Distinct()], ct);
        var opsByUser = new Dictionary<Guid, bool>();
        var goneSubscriptions = new HashSet<Guid>();

        foreach (var delivery in due)
        {
            if (!subscriptions.TryGetValue(delivery.SubscriptionId, out var subscription)
                || subscription.DisabledAt is not null
                || goneSubscriptions.Contains(subscription.Id)
                || !events.TryGetValue(delivery.EventId, out var evt)
                || (evt.Kind == CompanionEventKind.OperationalFailure
                    && !await IsOpsAdminAsync(subscription.UserId, opsByUser, ct)))
            {
                delivery.Status = PushDeliveryStatus.Expired;
                await repository.SaveAsync(ct);
                continue;
            }

            var result = await sender.SendAsync(subscription, PushPayload.Build(evt), ct);
            Apply(delivery, subscription, result, now);
            if (result.Outcome == PushSendOutcome.Gone)
                goneSubscriptions.Add(subscription.Id);

            await repository.SaveAsync(ct);
        }

        foreach (var id in goneSubscriptions)
            await repository.RemoveSubscriptionUnscopedAsync(id, ct);

        if (goneSubscriptions.Count > 0)
            logger.LogInformation("Removed {Count} Web Push subscriptions the push service reported gone", goneSubscriptions.Count);
    }

    /// <summary>The status-code matrix: what each answer does to the delivery and to the subscription.</summary>
    private static void Apply(PushDelivery delivery, PushSubscription subscription, PushSendResult result, DateTimeOffset now)
    {
        delivery.Attempts++;
        delivery.LastStatusCode = result.StatusCode;

        switch (result.Outcome)
        {
            case PushSendOutcome.Sent:
                delivery.Status = PushDeliveryStatus.Sent;
                delivery.SentAt = now;
                delivery.NextAttemptAt = null;
                subscription.LastSuccessAt = now;
                subscription.FailureCount = 0;
                break;

            case PushSendOutcome.Gone:
                delivery.Status = PushDeliveryStatus.Expired;
                delivery.NextAttemptAt = null;
                break;

            case PushSendOutcome.Transient:
                subscription.LastFailureAt = now;
                if (delivery.Attempts >= PushDeliveryPolicy.MaxAttempts)
                {
                    delivery.Status = PushDeliveryStatus.Failed;
                    delivery.NextAttemptAt = null;
                }
                else
                {
                    delivery.NextAttemptAt = now + PushDeliveryPolicy.BackoffAfter(delivery.Attempts);
                }

                break;

            case PushSendOutcome.Unauthorized:
                delivery.Status = PushDeliveryStatus.Failed;
                delivery.NextAttemptAt = null;
                subscription.LastFailureAt = now;
                subscription.FailureCount++;
                if (subscription.FailureCount >= PushDeliveryPolicy.DisableAfterRejections)
                    subscription.DisabledAt = now;
                break;

            default:
                delivery.Status = PushDeliveryStatus.Failed;
                delivery.NextAttemptAt = null;
                subscription.LastFailureAt = now;
                break;
        }
    }

    private async Task<bool> IsOpsAdminAsync(Guid userId, Dictionary<Guid, bool> cache, CancellationToken ct)
    {
        if (!cache.TryGetValue(userId, out var isOps))
        {
            isOps = await authorization.IsAuthorizedAsync(userId, AuthPolicies.RequireOwner, ct);
            cache[userId] = isOps;
        }

        return isOps;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
