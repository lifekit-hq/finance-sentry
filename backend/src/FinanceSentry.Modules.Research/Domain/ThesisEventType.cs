namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Lifecycle event type recorded for a thesis, candidate or recommendation (FR-001). <c>Promoted</c>/<c>Rejected</c>/
/// <c>Expired</c> are 019 (Candidate) events; a recommendation (#1069) records <c>Created</c>, then one of
/// <c>Confirmed</c>/<c>Rejected</c>/<c>Expired</c>/<c>Superseded</c>. A confirmed Exit also writes <c>Closed</c> and a
/// confirmed Trim <c>Trimmed</c> on the ticker's thesis (or on the recommendation when there is none).
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
    Trimmed,
    Superseded,
}
