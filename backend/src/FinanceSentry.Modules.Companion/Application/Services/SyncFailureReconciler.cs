namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;

public sealed class SyncFailureReconciler(
    IMaterialAlertReader alerts,
    INotificationSettingRepository settings,
    ICompanionEventRepository events,
    IMaterialityPolicy policy) : ISyncFailureReconciler
{
    public async Task<IReadOnlyList<CompanionEvent>> ReconcileAsync(
        IReadOnlyList<CompanionEvent> candidates, CancellationToken ct = default)
    {
        var syncFailures = candidates
            .Where(e => e.Kind == CompanionEventKind.SyncFailure && policy.AlertIdFromDedupKey(e.DedupKey) is not null)
            .ToList();
        if (syncFailures.Count == 0)
        {
            return candidates;
        }

        var resolved = await alerts.GetResolvedIdsAsync(
            [.. syncFailures.Select(e => policy.AlertIdFromDedupKey(e.DedupKey)!.Value).Distinct()], ct);

        var now = DateTimeOffset.UtcNow;
        var expired = new HashSet<Guid>();
        foreach (var evt in syncFailures)
        {
            if (resolved.Contains(policy.AlertIdFromDedupKey(evt.DedupKey)!.Value))
            {
                evt.Disposition = EventDisposition.Expired;
                evt.LastError = "alert resolved before delivery";
                await events.UpdateAsync(evt, ct);
                expired.Add(evt.Id);
            }
            else if (evt.Disposition == EventDisposition.HeldForDigest)
            {
                // The alert is a single row that stays open for the whole outage, so its age is how long the source
                // has been failing; capture-time staleness only saw bank accounts and never looked again.
                var mode = (await settings.GetOrDefaultUnscopedAsync(evt.UserId, ct)).Mode;
                var escalated = policy.DispositionFor(mode, evt.Kind, now - evt.OccurredAt);
                if (escalated != EventDisposition.HeldForDigest)
                {
                    evt.Disposition = escalated;
                    await events.UpdateAsync(evt, ct);
                }
            }
        }

        return [.. candidates.Where(e => !expired.Contains(e.Id))];
    }
}
