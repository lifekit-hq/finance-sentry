namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Revisions;

public enum ThesisRevisionVerdict
{
    Confirm,
    Reject,
}

public enum ThesisRevisionOutcome
{
    Confirmed,
    Rejected,

    /// <summary>A promotion the 022 risk gate refused: nothing was written and the proposal is still pending.</summary>
    RiskGateRefused,
}

/// <summary>
/// The owner's verdict on a pending proposal. Confirm is the single step that writes: a promotion goes through
/// <see cref="PromoteCandidateCommand"/> (so the risk gate runs first and an override is the owner's alone), a narrative or
/// trigger change through <see cref="SaveThesisCommand"/>, a close through <see cref="DeleteThesisCommand"/>, each stamped as
/// the owner's own write with the proposal's rationale and evidence in the audit. Reject needs the real reason, kept as
/// the counterfactual. Only the owner's own session decides: the handler refuses every other actor (a service token,
/// the in-app agent, a job), so the boundary does not rest on a transport attribute alone.
/// </summary>
public sealed record DecideThesisRevisionCommand(
    Guid UserId,
    Guid RevisionId,
    ThesisRevisionVerdict Verdict,
    string? Note,
    bool OverrideRisk = false) : ICommand<DecideThesisRevisionResult>;

public sealed record DecideThesisRevisionResult(
    ThesisRevisionOutcome Outcome,
    ThesisRevisionDto Revision,
    ThesisDto? Thesis = null,
    RiskGateVerdict? Gate = null,
    string? Note = null);

public sealed class DecideThesisRevisionCommandHandler(
    IThesisRevisionRepository revisions,
    IThesisRepository theses,
    ICommandHandler<PromoteCandidateCommand, PromoteCandidateResult> promote,
    ICommandHandler<SaveThesisCommand, ThesisDto> saveThesis,
    ICommandHandler<DeleteThesisCommand, bool> deleteThesis,
    IThesisEventRecorder recorder,
    IAuditActor actor)
    : ICommandHandler<DecideThesisRevisionCommand, DecideThesisRevisionResult>
{
    private static readonly AuditActorKind[] OwnerActors = [AuditActorKind.OwnerSession, AuditActorKind.McpPersonal];

    public async Task<DecideThesisRevisionResult> Handle(DecideThesisRevisionCommand command, CancellationToken ct)
    {
        if (!OwnerActors.Contains(actor.Kind))
        {
            throw new ThesisRevisionOwnerOnlyException();
        }

        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        if (note is { Length: > ThesisRevision.MaxDecisionNoteLength })
        {
            throw new InvalidThesisRevisionException($"the note is at most {ThesisRevision.MaxDecisionNoteLength} characters.");
        }

        if (command.Verdict == ThesisRevisionVerdict.Reject && note is null)
        {
            throw new InvalidThesisRevisionException("say why you are rejecting it - the reason is kept as the counterfactual.");
        }

        var revision = await revisions.GetAsync(command.UserId, command.RevisionId, ct)
            ?? throw new ThesisRevisionNotFoundException();
        if (revision.Status != ThesisRevisionStatus.Pending)
        {
            throw new ThesisRevisionNotPendingException(revision.Status.ToString());
        }

        if (revision.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            revision.Status = ThesisRevisionStatus.Expired;
            await revisions.UpdateAsync(revision, ct);
            throw new ThesisRevisionNotPendingException(revision.Status.ToString());
        }

        if (command.Verdict == ThesisRevisionVerdict.Reject)
        {
            await Close(revision, ThesisRevisionStatus.Rejected, note, resultThesisId: null, ct);
            return new DecideThesisRevisionResult(ThesisRevisionOutcome.Rejected, ThesisRevisionDto.From(revision));
        }

        return revision.Kind switch
        {
            ThesisRevisionKind.Promote => await ConfirmPromoteAsync(command, revision, note, ct),
            ThesisRevisionKind.Close => await ConfirmCloseAsync(command, revision, note, ct),
            _ => await ConfirmReviseAsync(command, revision, note, ct),
        };
    }

    private async Task<DecideThesisRevisionResult> ConfirmPromoteAsync(
        DecideThesisRevisionCommand command, ThesisRevision revision, string? note, CancellationToken ct)
    {
        var result = await promote.Handle(
            new PromoteCandidateCommand(
                command.UserId,
                revision.SubjectId,
                Triggers: null,
                command.OverrideRisk,
                revision.ProposedUsd,
                DecisionNote: revision.Rationale,
                revision.Paper,
                ThesisEventOrigin.User,
                revision.Evidence,
                revision.Proposed),
            ct);

        if (result.ThesisId is not { } thesisId)
        {
            if (result.Gate.Decision == RiskGateDecision.Refused)
            {
                return new DecideThesisRevisionResult(
                    ThesisRevisionOutcome.RiskGateRefused, ThesisRevisionDto.From(revision), Gate: result.Gate, Note: result.Note);
            }

            throw new InvalidThesisRevisionException("the candidate is no longer active; reject this proposal.");
        }

        await Close(revision, ThesisRevisionStatus.Confirmed, note, thesisId, ct);
        var thesis = await theses.FindAsync(command.UserId, thesisId, ct);
        return new DecideThesisRevisionResult(
            ThesisRevisionOutcome.Confirmed, ThesisRevisionDto.From(revision), thesis is null ? null : ToDto(thesis),
            result.Gate);
    }

    private async Task<DecideThesisRevisionResult> ConfirmReviseAsync(
        DecideThesisRevisionCommand command, ThesisRevision revision, string? note, CancellationToken ct)
    {
        var thesis = await theses.FindAsync(command.UserId, revision.SubjectId, ct)
            ?? throw new InvalidThesisRevisionException("the thesis no longer exists; reject this proposal.");
        var merged = ThesisRevisionMerge.Apply(ThesisVersion.Of(thesis), revision.Proposed);

        var saved = await saveThesis.Handle(
            new SaveThesisCommand(
                command.UserId,
                thesis.Id,
                merged.Ticker,
                merged.ThesisText,
                merged.KeyDataPoints,
                merged.Catalysts,
                merged.InvalidationTriggers,
                merged.EntryPrice,
                DecisionNote: null,
                ThesisEventOrigin.User,
                Rationale: ConfirmedRationale(revision),
                Evidence: revision.Evidence,
                NarrativeFalsifiers: merged.NarrativeFalsifiers),
            ct);

        await Close(revision, ThesisRevisionStatus.Confirmed, note, saved.Id, ct);
        return new DecideThesisRevisionResult(ThesisRevisionOutcome.Confirmed, ThesisRevisionDto.From(revision), saved);
    }

    private async Task<DecideThesisRevisionResult> ConfirmCloseAsync(
        DecideThesisRevisionCommand command, ThesisRevision revision, string? note, CancellationToken ct)
    {
        var thesis = await theses.FindAsync(command.UserId, revision.SubjectId, ct)
            ?? throw new InvalidThesisRevisionException("the thesis no longer exists; reject this proposal.");
        var prior = ThesisVersion.Of(thesis);

        if (!await deleteThesis.Handle(new DeleteThesisCommand(command.UserId, thesis.Id), ct))
        {
            throw new InvalidThesisRevisionException("the thesis no longer exists; reject this proposal.");
        }

        await recorder.RecordAuditedAsync(
            command.UserId, ThesisSubjectType.Thesis, thesis.Id, thesis.Ticker, ThesisEventType.Closed,
            ThesisEventOrigin.User, new ThesisAudit(prior, null, ConfirmedRationale(revision), revision.Evidence),
            ConfirmedRationale(revision), ct);

        await Close(revision, ThesisRevisionStatus.Confirmed, note, thesis.Id, ct);
        return new DecideThesisRevisionResult(ThesisRevisionOutcome.Confirmed, ThesisRevisionDto.From(revision));
    }

    private async Task Close(
        ThesisRevision revision, ThesisRevisionStatus status, string? note, Guid? resultThesisId, CancellationToken ct)
    {
        revision.Status = status;
        revision.DecidedAt = DateTimeOffset.UtcNow;
        revision.DecisionNote = note;
        revision.ResultThesisId = resultThesisId;
        await revisions.UpdateAsync(revision, ct);
    }

    private static string ConfirmedRationale(ThesisRevision revision)
        => $"Owner confirmed {revision.ProposedBy} proposal {revision.Id}: {revision.Rationale}";

    private static ThesisDto ToDto(InvestmentThesis thesis) => new(
        thesis.Id, thesis.Ticker, thesis.ThesisText,
        thesis.KeyDataPoints, thesis.Catalysts, thesis.InvalidationTriggers,
        thesis.CreatedAt, thesis.UpdatedAt, thesis.BrokenAt, thesis.BrokenReason, thesis.EntryPrice,
        ThesisEventOrigin.User.ToString(), thesis.NarrativeFalsifiers.ToList());
}
