namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Options;

/// <summary>
/// Promotes an Active candidate into a monitored <see cref="InvestmentThesis"/> (US3/FR-011).
/// The 022 risk gate always runs first (FR-011b): a Refused verdict aborts with the named rule
/// unless the caller passes <see cref="OverrideRisk"/>, in which case the override is recorded as a
/// signal (never silent — FR-007). Invalidation triggers are the caller's when supplied, else the
/// deterministic <see cref="TriggerPrefill"/> derived from the candidate's latest scorecard.
/// <see cref="Paper"/> (finance-sentry#704) marks the promotion as tracking-only: it never draws
/// down real cash, so the gate skips the real-book cash-funding rule (MinCashBuffer) for it while
/// still enforcing concentration/sizing rules (MaxPositionWeight, MaxNewPosition, Turnover). Real
/// (non-paper) promotions keep today's full rule set unchanged.
/// </summary>
public record PromoteCandidateCommand(
    Guid UserId,
    Guid CandidateId,
    IReadOnlyList<ThesisInvalidationTrigger>? Triggers = null,
    bool OverrideRisk = false,
    decimal ProposedUsd = 0m,
    string? DecisionNote = null,
    bool Paper = false) : ICommand<PromoteCandidateResult>;

public sealed record PromoteCandidateResult(
    Guid? ThesisId,
    RiskGateVerdict Gate,
    bool CandidateFound);

public sealed class PromoteCandidateCommandHandler(
    ICandidateRepository candidateRepo,
    ICandidateScoreRepository scoreRepo,
    IRiskPolicyGate riskGate,
    IRadarSignalWriter signalWriter,
    IThesisEventRecorder eventRecorder,
    ICommandHandler<SaveThesisCommand, API.Responses.ThesisDto> saveThesis,
    IOptions<OpportunityOptions> options)
    : ICommandHandler<PromoteCandidateCommand, PromoteCandidateResult>
{
    private const string Scanner = "opportunity";
    private const string OverrideSignalType = "promotion_risk_override";
    private const string SubjectTypeTicker = "Ticker";
    private const string PromotionThesisText = "Promoted from opportunity candidate.";

    private readonly OpportunityOptions _options = options.Value;

    public async Task<PromoteCandidateResult> Handle(PromoteCandidateCommand command, CancellationToken ct)
    {
        var candidate = await candidateRepo.GetAsync(command.UserId, command.CandidateId, ct);
        if (candidate is null || candidate.Status != CandidateStatus.Active)
        {
            var notFound = new RiskGateVerdict(RiskGateDecision.Allowed, null, null, null, null, "candidate not found or not active");
            return new PromoteCandidateResult(null, notFound, CandidateFound: candidate is not null);
        }

        var gate = await riskGate.CheckProposalAsync(
            command.UserId, candidate.Ticker, command.ProposedUsd, command.OverrideRisk, command.Paper, ct);

        if (gate.Decision == RiskGateDecision.Refused && !command.OverrideRisk)
        {
            return new PromoteCandidateResult(null, gate, CandidateFound: true);
        }

        if (gate.Decision == RiskGateDecision.Refused && command.OverrideRisk)
        {
            await signalWriter.AppendSignalAsync(new RadarSignalRequest(
                Scanner,
                OverrideSignalType,
                SignalSeverity.Notable,
                SubjectTypeTicker,
                candidate.Ticker,
                command.UserId,
                $"{Scanner}:{OverrideSignalType}:{candidate.Id}",
                new { gate.RuleKey, gate.ObservedValue, gate.LimitValue }), ct);
        }

        var triggers = command.Triggers ?? await BuildPrefilledTriggersAsync(candidate.Id, ct);

        var thesis = await saveThesis.Handle(
            new SaveThesisCommand(
                command.UserId,
                Id: null,
                candidate.Ticker,
                PromotionThesisText,
                KeyDataPoints: [],
                Catalysts: [],
                InvalidationTriggers: triggers,
                EntryPrice: null,
                DecisionNote: command.DecisionNote),
            ct);

        candidate.PromotedThesisId = thesis.Id;
        candidate.Status = CandidateStatus.Promoted;
        await candidateRepo.UpdateAsync(candidate, ct);

        await eventRecorder.RecordAsync(
            command.UserId, ThesisSubjectType.Candidate, candidate.Id, candidate.Ticker,
            ThesisEventType.Promoted, command.DecisionNote, ct);

        return new PromoteCandidateResult(thesis.Id, gate, CandidateFound: true);
    }

    private async Task<IReadOnlyList<ThesisInvalidationTrigger>> BuildPrefilledTriggersAsync(Guid candidateId, CancellationToken ct)
    {
        var latest = await scoreRepo.LatestForCandidateAsync(candidateId, ct);
        var detail = new FundamentalsScoreDetail(
            latest?.Evidence.RevenueYoy, latest?.Evidence.GrossMarginLatest);

        return TriggerPrefill.Propose(detail, _options)
            .Select(t => new ThesisInvalidationTrigger(
                t.Metric, t.Direction, t.Threshold, t.ProxyTicker, t.ConsecutivePeriods))
            .ToList();
    }
}
