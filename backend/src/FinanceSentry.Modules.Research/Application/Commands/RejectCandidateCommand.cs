namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Rejects an Active candidate (US3). The scorecard is kept for the track record / counterfactuals
/// (SC-005/020) — nothing is deleted. Records a <see cref="ThesisEventType.Rejected"/> event.
/// </summary>
public record RejectCandidateCommand(
    Guid UserId,
    Guid CandidateId,
    string Reason,
    ThesisEventOrigin Origin = ThesisEventOrigin.User) : ICommand<RejectCandidateResult>;

public sealed record RejectCandidateResult(bool CandidateFound, CandidateStatus Status);

public sealed class RejectCandidateCommandHandler(
    ICandidateRepository candidateRepo,
    IThesisEventRecorder eventRecorder)
    : ICommandHandler<RejectCandidateCommand, RejectCandidateResult>
{
    public async Task<RejectCandidateResult> Handle(RejectCandidateCommand command, CancellationToken ct)
    {
        var candidate = await candidateRepo.GetAsync(command.UserId, command.CandidateId, ct);
        if (candidate is null)
        {
            return new RejectCandidateResult(CandidateFound: false, CandidateStatus.Active);
        }

        if (candidate.Status != CandidateStatus.Active)
        {
            return new RejectCandidateResult(CandidateFound: true, candidate.Status);
        }

        candidate.Status = CandidateStatus.Rejected;
        candidate.RejectedReason = command.Reason;
        await candidateRepo.UpdateAsync(candidate, ct);

        await eventRecorder.RecordAuditedAsync(
            command.UserId, ThesisSubjectType.Candidate, candidate.Id, candidate.Ticker,
            ThesisEventType.Rejected, command.Origin,
            new ThesisAudit(Prior: null, New: null, command.Reason, Evidence: null),
            command.Reason, ct);

        return new RejectCandidateResult(CandidateFound: true, CandidateStatus.Rejected);
    }
}
