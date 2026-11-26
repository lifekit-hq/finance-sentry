namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Revisions;

/// <summary>
/// Journals a review of a thesis that changes nothing in it (#1518): a <see cref="ThesisEventType.Reviewed"/> event
/// on the thesis, price-stamped like any decision point, whose <see cref="ThesisEvent.DecisionNote"/> is the verdict
/// ("no change", "hold") and whose <see cref="ThesisAudit"/> carries the reasoning and the evidence it rested on. The
/// thesis itself is not touched, so a review needs no companion event to hang on and no proposal to the owner; a
/// review that wants a change goes through <see cref="ProposeThesisRevisionCommand"/> instead. Open to the owner's
/// session and to Ledger, each stamped with its own origin.
/// </summary>
public sealed record RecordThesisReviewCommand(
    Guid UserId,
    Guid ThesisId,
    string Verdict,
    string Rationale,
    ThesisEvidenceRefs? Evidence = null,
    ThesisEventOrigin Origin = ThesisEventOrigin.User) : ICommand<RecordThesisReviewResult>;

public sealed record RecordThesisReviewResult(
    Guid ThesisId,
    string Ticker,
    ThesisEventType EventType,
    string Verdict,
    string Rationale,
    string Origin);

public sealed class RecordThesisReviewCommandHandler(
    IThesisRepository theses,
    IThesisEventRecorder eventRecorder,
    IResearchWriteScope writeScope)
    : ICommandHandler<RecordThesisReviewCommand, RecordThesisReviewResult>
{
    public const int MaxVerdictLength = 200;

    public async Task<RecordThesisReviewResult> Handle(RecordThesisReviewCommand cmd, CancellationToken ct)
    {
        var verdict = cmd.Verdict?.Trim();
        if (string.IsNullOrEmpty(verdict))
        {
            throw new InvalidThesisReviewException("a verdict is required (for example 'no change').");
        }

        if (verdict.Length > MaxVerdictLength)
        {
            throw new InvalidThesisReviewException($"the verdict is at most {MaxVerdictLength} characters.");
        }

        var rationale = cmd.Rationale?.Trim();
        if (string.IsNullOrEmpty(rationale))
        {
            throw new InvalidThesisReviewException("a rationale is required - it is what the review is graded on.");
        }

        if (rationale.Length > ThesisRevision.MaxRationaleLength)
        {
            throw new InvalidThesisReviewException($"the rationale is at most {ThesisRevision.MaxRationaleLength} characters.");
        }

        var thesis = await theses.FindAsync(cmd.UserId, cmd.ThesisId, ct) ?? throw new ThesisNotFoundException();

        await writeScope.RunAsync(cmd.Origin, async () =>
        {
            await eventRecorder.RecordAuditedAsync(
                cmd.UserId, ThesisSubjectType.Thesis, thesis.Id, thesis.Ticker,
                ThesisEventType.Reviewed, cmd.Origin,
                new ThesisAudit(Prior: null, New: null, rationale, cmd.Evidence),
                verdict, ct);
            return thesis;
        }, ct);

        return new RecordThesisReviewResult(
            thesis.Id, thesis.Ticker, ThesisEventType.Reviewed, verdict, rationale, cmd.Origin.ToString());
    }
}
