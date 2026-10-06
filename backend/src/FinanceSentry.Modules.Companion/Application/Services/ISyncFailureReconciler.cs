namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// Delivery-time check for <see cref="CompanionEventKind.SyncFailure"/> events: a failure whose alert is no longer
/// open (resolved, dismissed or deleted with its account) must never be relayed, and a still-held failure whose alert
/// stayed open past <see cref="IMaterialityPolicy.SyncFailureEscalationAge"/> is a real outage that escalates.
/// </summary>
public interface ISyncFailureReconciler
{
    /// <summary>
    /// Expires events whose alert is no longer open and escalates held ones to <see cref="EventDisposition.Pending"/>
    /// when the policy says so, persisting both; a held event the policy would not escalate stays held. Returns the
    /// input minus the expired events; other kinds pass through untouched. An escalated event keeps its place in the
    /// list with its new disposition.
    /// </summary>
    Task<IReadOnlyList<CompanionEvent>> ReconcileAsync(IReadOnlyList<CompanionEvent> events, CancellationToken ct = default);
}
