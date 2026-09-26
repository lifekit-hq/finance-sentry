namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Expires every Active candidate past its <c>ExpiresAt</c> (US3/FR-001, R9). Each expiry appends a
/// final <see cref="CandidateScore"/> snapshot (a copy of the latest score, so the terminal moment is
/// auditable) and records an <see cref="ThesisEventType.Expired"/> event. Nothing is deleted —
/// expired candidates stay queryable for 020 counterfactuals (SC-005).
/// </summary>
public record ExpireCandidatesCommand(DateTimeOffset AsOf) : ICommand<ExpireCandidatesResult>;

public sealed record ExpireCandidatesResult(int ExpiredCount);

public sealed class ExpireCandidatesCommandHandler(
    ICandidateRepository candidateRepo,
    ICandidateScoreRepository scoreRepo,
    IThesisEventRecorder eventRecorder,
    IAlertGeneratorService alerts)
    : ICommandHandler<ExpireCandidatesCommand, ExpireCandidatesResult>
{
    private const string ExpiryReason = "auto-expired past TTL";

    public async Task<ExpireCandidatesResult> Handle(ExpireCandidatesCommand command, CancellationToken ct)
    {
        var due = await candidateRepo.ListExpiredAsync(command.AsOf, ct);

        var expired = 0;
        foreach (var candidate in due)
        {
            var latest = await scoreRepo.LatestForCandidateAsync(candidate.Id, ct);
            if (latest is not null)
            {
                await scoreRepo.AppendAsync(new CandidateScore
                {
                    CandidateId = candidate.Id,
                    StructureScore = latest.StructureScore,
                    FundamentalsScore = latest.FundamentalsScore,
                    CrowdingClass = latest.CrowdingClass,
                    IpsFit = latest.IpsFit,
                    Evidence = latest.Evidence,
                    FormulaVersion = latest.FormulaVersion,
                }, ct);
            }

            candidate.Status = CandidateStatus.Expired;
            await candidateRepo.UpdateAsync(candidate, ct);

            await eventRecorder.RecordAsync(
                candidate.UserId, ThesisSubjectType.Candidate, candidate.Id, candidate.Ticker,
                ThesisEventType.Expired, ExpiryReason, ct);

            await alerts.ResolveOpportunityAlertAsync(candidate.UserId, candidate.Id, ct);

            expired++;
        }

        return new ExpireCandidatesResult(expired);
    }
}
