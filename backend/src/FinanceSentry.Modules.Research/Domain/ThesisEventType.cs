namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Lifecycle event type recorded for a thesis, candidate or recommendation (FR-001). <c>Promoted</c>/<c>Rejected</c>/
/// <c>Expired</c> are 019 (Candidate) events; a recommendation (#1069) records <c>Created</c>, then one of
/// <c>Confirmed</c>/<c>Rejected</c>/<c>Expired</c>/<c>Superseded</c>, all on the recommendation itself.
/// </summary>
public enum ThesisEventType
{
    Created,
    Broken,
    Unbroken,
    Closed,
    Promoted,
    Rejected,
    Expired,
    Snapshot,
    Confirmed,
    Superseded,
}
