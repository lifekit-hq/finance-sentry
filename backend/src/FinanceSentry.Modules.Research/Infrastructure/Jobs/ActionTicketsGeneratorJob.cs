namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Risk.Application.Queries;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job (432) that reads IPS allocation drift for each user and emits a
/// RebalanceProposal alert when bands are breached, and a CashSweepProposal alert when idle
/// cash exceeds the configured buffer, both from <see cref="PortfolioAdviceEngine"/> so the two cannot disagree. Figures are always sourced from IBookFiguresService
/// via GetAllocationDriftQueryHandler — no independently derived numbers. A proposal notifies once per drift
/// episode: the alert generator holds the same drift quiet (see <see cref="IAlertGeneratorService"/>) and this job
/// supplies what "the same drift" means, a signature of sleeves, directions and band multiples (#1266).
/// </summary>
public sealed class ActionTicketsGeneratorJob(
    IIpsRepository ipsRepo,
    IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto> driftQuery,
    IQueryHandler<GetRiskRuleSetQuery, RiskRuleSetDto?> riskQuery,
    IAlertGeneratorService alerts,
    ILogger<ActionTicketsGeneratorJob> logger)
{
    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var userIds = await ipsRepo.GetUserIdsWithCurrentIpsUnscopedAsync(ct);

        if (userIds.Count == 0)
        {
            logger.LogDebug("ActionTicketsGenerator: no users with IPS, skipping.");
            return;
        }

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ActionTicketsGenerator: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, CancellationToken ct)
    {
        var drift = await driftQuery.Handle(new GetAllocationDriftQuery(userId), ct);

        // A book read with a failed source understates the total and flips sleeves across their bands, so a stale
        // read is neither a drift to propose nor proof that one cleared: opening or resolving on it would make the
        // operator hear the same proposal again when the source comes back (#1266).
        if (drift.IsStale)
        {
            logger.LogWarning("ActionTicketsGenerator: book figures are stale for user {UserId}, skipping.", userId);
            return;
        }

        var rules = await riskQuery.Handle(new GetRiskRuleSetQuery(userId), ct);

        // One engine pass answers both questions from the same policy and buffer, so the rebalance proposal and
        // the cash-sweep proposal cannot point opposite ways.
        var advice = PortfolioAdviceEngine.Advise(drift, rules);

        if (advice.Orders.Count > 0)
        {
            var summary = BuildOrderSummary(advice.Orders, drift.TotalValueUsd, advice.Basis);
            logger.LogInformation(
                "ActionTicketsGenerator: rebalance proposal for user {UserId} — {OrderCount} order(s), book ${TotalValueUsd:N0}, basis {Basis}",
                userId, advice.Orders.Count, drift.TotalValueUsd, advice.Basis.Describe());
            await alerts.GenerateRebalanceProposalAlertAsync(userId, advice.Orders.Count, summary, BuildDriftSignature(advice.Orders), ct);
        }
        else
        {
            await alerts.ResolveRebalanceProposalAlertAsync(userId, ct);
        }

        await ApplyCashAdviceAsync(userId, drift, advice, ct);
    }

    private async Task ApplyCashAdviceAsync(Guid userId, AllocationDriftDto drift, PortfolioAdvice advice, CancellationToken ct)
    {
        if (drift.TotalValueUsd <= 0)
            return;

        if (advice.Cash.Status is CashAdviceStatus.BufferNotSet)
        {
            // A buffer that is unset or degenerate is never a reason to deploy: withdraw any standing proposal.
            logger.LogWarning(
                "ActionTicketsGenerator: cash buffer not set for user {UserId} ({Basis}); no deployment advice.",
                userId, advice.Basis.Describe());
            await alerts.ResolveCashSweepProposalAlertAsync(userId, ct);
            return;
        }

        if (advice.Cash.Status is not CashAdviceStatus.Deploy)
        {
            await alerts.ResolveCashSweepProposalAlertAsync(userId, ct);
            return;
        }

        var bufferUsd = advice.Basis.BufferUsd ?? 0m;
        logger.LogInformation(
            "ActionTicketsGenerator: cash-sweep proposal for user {UserId} — idle ${CashUsd:N0} > buffer ${MinBufferUsd:N0}, excess ${ExcessUsd:N0}",
            userId, advice.Cash.IdleCashUsd, bufferUsd, advice.Cash.DeployableUsd);

        await alerts.GenerateCashSweepProposalAlertAsync(
            userId, advice.Cash.IdleCashUsd, bufferUsd, advice.Cash.DeployableUsd, advice.Basis.Describe(), ct);
    }

    /// <summary>
    /// Names the drift a proposal answers — which sleeves, which way, how many bands out — and nothing that moves
    /// with the market day to day (notionals, book total), so the same drift reads the same on every run. Ordered
    /// so the sleeve ordering of the drift query doesn't matter.
    /// </summary>
    private static string BuildDriftSignature(IReadOnlyList<RebalanceOrder> orders)
        => string.Join(
            ';',
            orders.OrderBy(o => o.AssetClass, StringComparer.Ordinal)
                .Select(o => $"{o.AssetClass}:{o.Direction}:{o.BandsOut}"));

    private static string BuildOrderSummary(IReadOnlyList<RebalanceOrder> orders, decimal totalValueUsd, AdviceBasis basis)
    {
        var parts = orders.Select(o => $"{o.Direction} {o.AssetClass} ≈ ${o.NotionalUsd:N0}");
        return $"Portfolio rebalance required (book ${totalValueUsd:N0}): {string.Join("; ", parts)}. Basis: {basis.Describe()}.";
    }
}
