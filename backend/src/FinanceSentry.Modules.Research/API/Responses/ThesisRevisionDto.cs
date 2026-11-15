namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Revisions;

/// <summary>
/// One thesis-revision proposal as the owner's confirm surface and Ledger's tools see it. Enums cross as their names.
/// <see cref="Proposed"/> holds only what the proposal would change; a null field is left as it is (or, on a
/// promotion, defaulted from the scorecard).
/// </summary>
public sealed record ThesisRevisionDto(
    Guid Id,
    string Kind,
    string Status,
    string ProposedBy,
    string SubjectType,
    Guid SubjectId,
    string Ticker,
    ThesisRevisionContent Proposed,
    string Rationale,
    ThesisEvidenceRefs? Evidence,
    decimal ProposedUsd,
    bool Paper,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    Guid? ResultThesisId)
{
    public static ThesisRevisionDto From(ThesisRevision r) => new(
        r.Id,
        r.Kind.ToString(),
        r.Status.ToString(),
        r.ProposedBy.ToString(),
        r.SubjectType.ToString(),
        r.SubjectId,
        r.Ticker,
        r.Proposed,
        r.Rationale,
        r.Evidence,
        r.ProposedUsd,
        r.Paper,
        r.CreatedAt,
        r.ExpiresAt,
        r.DecidedAt,
        r.DecisionNote,
        r.ResultThesisId);
}

/// <summary>The owner's queue of thesis revisions: pending oldest first, and those closed in the last 30 days.</summary>
public sealed record ThesisRevisionsResult(
    IReadOnlyList<ThesisRevisionDto> Pending,
    IReadOnlyList<ThesisRevisionDto> Decided);
