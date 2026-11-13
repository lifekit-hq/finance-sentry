namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// The single hook point every thesis/candidate lifecycle write path calls to append a price-stamped
/// <see cref="ThesisEvent"/> (R1). Never blocks or throws on a quote failure — appends a pending event
/// instead (FR-003).
/// </summary>
public interface IThesisEventRecorder
{
    Task RecordAsync(
        Guid userId,
        ThesisSubjectType subjectType,
        Guid subjectId,
        string ticker,
        ThesisEventType eventType,
        string? decisionNote = null,
        CancellationToken ct = default);

    /// <summary>
    /// Appends an event that also carries its <see cref="ThesisEventOrigin"/> and, for a thesis write, its
    /// <see cref="ThesisAudit"/>. <see cref="ThesisEventType.Revised"/> and <see cref="ThesisEventType.Reverted"/>
    /// are not price-stamped: they are edits, not points on the track-record curve.
    /// </summary>
    Task RecordAuditedAsync(
        Guid userId,
        ThesisSubjectType subjectType,
        Guid subjectId,
        string ticker,
        ThesisEventType eventType,
        ThesisEventOrigin origin,
        ThesisAudit? audit,
        string? decisionNote = null,
        CancellationToken ct = default);
}
