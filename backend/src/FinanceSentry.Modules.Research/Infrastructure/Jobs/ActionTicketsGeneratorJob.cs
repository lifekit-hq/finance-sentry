namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Risk.Application.Queries;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job (432) that reads IPS allocation drift for each user and emits a
/// RebalanceProposal alert when bands are breached, and a CashSweepProposal alert when idle
/// cash exceeds the configured buffer. Figures are always sourced from IBookFiguresService
/// via GetAllocationDriftQueryHandler — no independently derived numbers.
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
        var userIds = await ipsRepo.GetUserIdsWithCurrentIpsAsync(ct);

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

        var orders = drift.HasIps && drift.NeedsRebalance ? BuildOrderLines(drift) : [];
        if (orders.Count > 0)
        {
            var summary = BuildOrderSummary(orders, drift.TotalValueUsd);
            logger.LogInformation(
                "ActionTicketsGenerator: rebalance proposal for user {UserId} — {OrderCount} order(s), book ${TotalValueUsd:N0}",
                userId, orders.Count, drift.TotalValueUsd);
            await alerts.GenerateRebalanceProposalAlertAsync(userId, orders.Count, summary, ct);
        }
        else
        {
            await alerts.ResolveRebalanceProposalAlertAsync(userId, ct);
        }

        await TryGenerateCashSweepAsync(userId, drift, ct);
    }

    private async Task TryGenerateCashSweepAsync(Guid userId, AllocationDriftDto drift, CancellationToken ct)
    {
        if (drift.TotalValueUsd <= 0)
            return;

        var rules = await riskQuery.Handle(new GetRiskRuleSetQuery(userId), ct);
        if (rules?.MinCashBufferPct is not { } minPct || minPct <= 0)
            return;

        var minBufferUsd = Math.Round(minPct / 100m * drift.TotalValueUsd, 2);
        var excessUsd = Math.Round(drift.CashUsd - minBufferUsd, 2);

        if (excessUsd <= 0)
        {
            await alerts.ResolveCashSweepProposalAlertAsync(userId, ct);
            return;
        }

        logger.LogInformation(
            "ActionTicketsGenerator: cash-sweep proposal for user {UserId} — idle ${CashUsd:N0} > buffer ${MinBufferUsd:N0}, excess ${ExcessUsd:N0}",
            userId, drift.CashUsd, minBufferUsd, excessUsd);

        await alerts.GenerateCashSweepProposalAlertAsync(userId, drift.CashUsd, minBufferUsd, excessUsd, ct);
    }

    private static List<OrderLine> BuildOrderLines(AllocationDriftDto drift)
    {
        var orders = new List<OrderLine>();

        foreach (var sleeve in drift.Sleeves)
        {
            if (sleeve.Status == "OverBand")
            {
                var excess = sleeve.ActualValueUsd - (sleeve.TargetPct / 100m * drift.TotalValueUsd);
                if (excess > 0)
                    orders.Add(new OrderLine(sleeve.AssetClass, "Sell", Math.Round(excess, 2)));
            }
            else if (sleeve.Status == "UnderBand")
            {
                var deficit = (sleeve.TargetPct / 100m * drift.TotalValueUsd) - sleeve.ActualValueUsd;
                if (deficit > 0)
                    orders.Add(new OrderLine(sleeve.AssetClass, "Buy", Math.Round(deficit, 2)));
            }
            else if (sleeve.Status == "Unplanned" && sleeve.ActualPct >= 1m)
            {
                // Unplanned holding ≥ 1%: flag for review (no sizing target defined in IPS)
                orders.Add(new OrderLine(sleeve.AssetClass, "Review", Math.Round(sleeve.ActualValueUsd, 2)));
            }
        }

        return orders;
    }

    private static string BuildOrderSummary(List<OrderLine> orders, decimal totalValueUsd)
    {
        var parts = orders.Select(o => $"{o.Direction} {o.AssetClass} ≈ ${o.NotionalUsd:N0}");
        return $"Portfolio rebalance required (book ${totalValueUsd:N0}): {string.Join("; ", parts)}";
    }

    private record OrderLine(string AssetClass, string Direction, decimal NotionalUsd);
}
