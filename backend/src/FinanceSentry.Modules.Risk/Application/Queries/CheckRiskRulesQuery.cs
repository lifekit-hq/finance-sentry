namespace FinanceSentry.Modules.Risk.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using Microsoft.Extensions.Options;

public sealed record RiskProposal(string Ticker, decimal ProposedUsd, bool Override = false, bool IsPaper = false);

/// <summary>FR-001d portfolio-context facts attached to the no-arg compliance report.</summary>
public sealed record PortfolioContext(
    IReadOnlyList<PairwiseCorrelation> TopHoldingsCorrelations,
    StressLine? StressLine);

/// <summary>"−<see cref="ShockPct"/> on <see cref="Ticker"/> ⇒ −<see cref="BookImpactPct"/> of book."</summary>
public sealed record StressLine(string Ticker, decimal WeightPct, decimal ShockPct, decimal BookImpactPct);

public sealed record CheckRiskRulesResult(
    ComplianceReport? Report, RiskVerdict? Verdict, bool HasRuleSet, PortfolioContext? Context = null);

public record CheckRiskRulesQuery(Guid UserId, RiskProposal? Proposal = null) : IQuery<CheckRiskRulesResult>;

public sealed class CheckRiskRulesQueryHandler(
    IBookSnapshotReader bookReader,
    IRiskRuleSetRepository ruleSetRepo,
    IPolicyViolationAckRepository ackRepo,
    IHoldingSnapshotRepository snapshotRepo,
    IRiskEvaluationService evaluationService,
    IAllocationPolicySource allocationPolicySource,
    ITurnoverTracker turnoverTracker,
    IMarketStructureReader structureReader,
    IOptions<RiskOptions> options)
    : IQueryHandler<CheckRiskRulesQuery, CheckRiskRulesResult>
{
    private const int TopHoldingsForCorrelation = 5;
    private const decimal StressShockPct = 0.30m;

    private readonly int _rollingQuarterDays = options.Value.RollingQuarterDays;

    public async Task<CheckRiskRulesResult> Handle(CheckRiskRulesQuery query, CancellationToken ct)
    {
        var book = await bookReader.ReadAsync(query.UserId, ct);
        var ruleSet = await ruleSetRepo.GetCurrentAsync(query.UserId, ct);

        if (query.Proposal is null)
        {
            var acks = await ackRepo.ListActiveAsync(query.UserId, ct);
            var allocationTargets = await allocationPolicySource.GetAllocationTargetsAsync(query.UserId, ct);
            var report = evaluationService.Evaluate(book, ruleSet, allocationTargets, acks);
            var context = await BuildPortfolioContextAsync(book, ct);
            return new CheckRiskRulesResult(report, null, ruleSet is not null, context);
        }

        var now = DateTimeOffset.UtcNow;
        var history = await snapshotRepo.ListSinceAsync(query.UserId, now - TimeSpan.FromDays(_rollingQuarterDays), ct);
        var turnoverCount = turnoverTracker.CountDiscretionaryTradesInRollingQuarter(history, now);

        var verdict = evaluationService.EvaluateProposal(
            book, ruleSet, query.Proposal.Ticker, query.Proposal.ProposedUsd, turnoverCount, query.Proposal.IsPaper);
        return new CheckRiskRulesResult(null, verdict, ruleSet is not null);
    }

    /// <summary>
    /// FR-001d facts, computed only when data allows: 63-day pairwise correlations among the top
    /// holdings (from 018 bars — pairs without history are simply absent) and the stress line for
    /// the largest position. No VaR machinery.
    /// </summary>
    private async Task<PortfolioContext?> BuildPortfolioContextAsync(BookSnapshot book, CancellationToken ct)
    {
        if (book.Positions.Count == 0 || book.TotalUsd <= 0)
        {
            return null;
        }

        var top = book.Positions
            .OrderByDescending(p => p.UsdValue)
            .Take(TopHoldingsForCorrelation)
            .ToList();

        var correlations = await structureReader.GetPairwiseCorrelationsAsync(
            top.Select(p => p.Symbol).ToList(), ct);

        var largest = top[0];
        var stress = new StressLine(
            largest.Symbol,
            largest.WeightPct,
            StressShockPct,
            Math.Round(largest.WeightPct * StressShockPct, 4));

        return new PortfolioContext(correlations, stress);
    }
}
