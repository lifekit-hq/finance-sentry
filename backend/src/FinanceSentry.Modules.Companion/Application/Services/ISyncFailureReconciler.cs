namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// Delivery-time check for <see cref="CompanionEventKind.SyncFailure"/> events: a failure whose alert has since
/// resolved must never be relayed, and a still-held failure whose alert stayed open past
/// <see cref="IMaterialityPolicy.SyncFailureEscalationAge"/> is a real outage that escalates.
/// </summary>
public interface ISyncFailureReconciler
{
    /// <summary>
    /// Expires events whose alert is resolved and re-evaluates held ones for escalation, persisting both. Returns the
    /// input minus the expired events; other kinds pass through untouched. An escalated event keeps its place in the
    /// list with its new disposition.
    /// </summary>
    Task<IReadOnlyList<CompanionEvent>> ReconcileAsync(IReadOnlyList<CompanionEvent> events, CancellationToken ct = default);
}
