namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Restores the prior version a <see cref="ThesisEventType.Revised"/> (or <see cref="ThesisEventType.Reverted"/>) journal
/// event recorded, and journals the reversal itself (event type <see cref="ThesisEventType.Reverted"/>, the replaced
/// version as its prior, <see cref="ThesisAudit.RevertsEventId"/> pointing back). Open to the owner's session and to
/// Ledger, each stamped with its own origin.
/// </summary>
public record RevertThesisRevisionCommand(
    Guid UserId,
    Guid EventId,
    string? Rationale = null,
    ThesisEventOrigin Origin = ThesisEventOrigin.User) : ICommand<RevertThesisRevisionResult>;

public enum RevertOutcome
{
    Reverted,
    EventNotFound,

    /// <summary>The event is not a revision that recorded a prior version (a Created, Promoted, price snapshot, ...).</summary>
    NotARevision,
    ThesisNotFound,

    /// <summary>The thesis moved on after that revision; revert the later one first, or save the wanted content.</summary>
    ThesisChangedSince,
}

public sealed record RevertThesisRevisionResult(RevertOutcome Outcome, ThesisDto? Thesis);

public sealed class RevertThesisRevisionCommandHandler(
    IThesisRepository thesisRepo,
    IThesisEventRepository eventRepo,
    IThesisEventRecorder eventRecorder)
    : ICommandHandler<RevertThesisRevisionCommand, RevertThesisRevisionResult>
{
    public async Task<RevertThesisRevisionResult> Handle(RevertThesisRevisionCommand cmd, CancellationToken ct)
    {
        var revision = await eventRepo.FindAsync(cmd.UserId, cmd.EventId, ct);
        if (revision is null)
        {
            return new RevertThesisRevisionResult(RevertOutcome.EventNotFound, null);
        }

        if (revision.SubjectType != ThesisSubjectType.Thesis
            || revision.EventType is not (ThesisEventType.Revised or ThesisEventType.Reverted)
            || revision.Audit is not { Prior: { } prior, New: { } revisedTo })
        {
            return new RevertThesisRevisionResult(RevertOutcome.NotARevision, null);
        }

        var thesis = await thesisRepo.FindAsync(cmd.UserId, revision.SubjectId, ct);
        if (thesis is null)
        {
            return new RevertThesisRevisionResult(RevertOutcome.ThesisNotFound, null);
        }

        var replaced = ThesisVersion.Of(thesis);
        if (!replaced.SameContentAs(revisedTo))
        {
            return new RevertThesisRevisionResult(RevertOutcome.ThesisChangedSince, null);
        }

        prior.ApplyTo(thesis);
        await thesisRepo.UpsertAsync(thesis, ct);

        var rationale = string.IsNullOrWhiteSpace(cmd.Rationale) ? null : cmd.Rationale.Trim();
        await eventRecorder.RecordAuditedAsync(
            cmd.UserId, ThesisSubjectType.Thesis, thesis.Id, thesis.Ticker,
            ThesisEventType.Reverted, cmd.Origin,
            new ThesisAudit(replaced, prior, rationale, Evidence: null, RevertsEventId: revision.Id),
            rationale, ct);

        return new RevertThesisRevisionResult(
            RevertOutcome.Reverted,
            new ThesisDto(
                thesis.Id, thesis.Ticker, thesis.ThesisText,
                thesis.KeyDataPoints, thesis.Catalysts, thesis.InvalidationTriggers,
                thesis.CreatedAt, thesis.UpdatedAt, thesis.BrokenAt, thesis.BrokenReason, thesis.EntryPrice,
                cmd.Origin.ToString()));
    }
}
