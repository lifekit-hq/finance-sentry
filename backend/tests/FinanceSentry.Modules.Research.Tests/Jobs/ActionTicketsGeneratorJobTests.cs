namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Risk.Application.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class ActionTicketsGeneratorJobTests
{
    private readonly Mock<IIpsRepository> _ipsRepo = new();
    private readonly Mock<IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto>> _driftQuery = new();
    private readonly Mock<IQueryHandler<GetRiskRuleSetQuery, RiskRuleSetDto?>> _riskQuery = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly ActionTicketsGeneratorJob _job;

    private readonly Guid _userId = Guid.NewGuid();

    public ActionTicketsGeneratorJobTests()
    {
        _job = new ActionTicketsGeneratorJob(
            _ipsRepo.Object,
            _driftQuery.Object,
            _riskQuery.Object,
            _alerts.Object,
            NullLogger<ActionTicketsGeneratorJob>.Instance);
    }

    // ── US1: Rebalance proposal ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NoUsersWithIps_SkipsGracefully()
    {
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([]);

        await _job.ExecuteAsync();

        _driftQuery.Verify(q => q.Handle(It.IsAny<GetAllocationDriftQuery>(), default), Times.Never);
        _alerts.Verify(a => a.GenerateRebalanceProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NeedsRebalanceFalse_NoAlertGenerated()
    {
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: false, []));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync((RiskRuleSetDto?)null);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateRebalanceProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NeedsRebalanceFalse_ResolvesExistingProposal()
    {
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: false, []));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync((RiskRuleSetDto?)null);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.ResolveRebalanceProposalAlertAsync(_userId, default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OverBandSleeve_DoesNotResolveRebalanceProposal()
    {
        var sleeves = new List<AllocationSleeveDrift>
        {
            new("Equities", 60m, 55m, 65m, 75m, 75_000m, 15m, "OverBand"),
        };
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: true, sleeves));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync((RiskRuleSetDto?)null);
        _alerts.Setup(a => a.GenerateRebalanceProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), default)).Returns(Task.CompletedTask);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.ResolveRebalanceProposalAlertAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_HasIpsFalse_NoAlertGenerated()
    {
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: true, [], hasIps: false));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync((RiskRuleSetDto?)null);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateRebalanceProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_OverBandSleeve_GeneratesSellOrder()
    {
        var sleeves = new List<AllocationSleeveDrift>
        {
            new("Equities", 60m, 55m, 65m, 75m, 75_000m, 15m, "OverBand"),
        };
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: true, sleeves));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync((RiskRuleSetDto?)null);

        string? capturedSummary = null;
        int? capturedOrderCount = null;
        _alerts.Setup(a => a.GenerateRebalanceProposalAlertAsync(
                _userId, It.IsAny<int>(), It.IsAny<string>(), default))
            .Callback<Guid, int, string, CancellationToken>((_, count, summary, _) =>
            {
                capturedOrderCount = count;
                capturedSummary = summary;
            })
            .Returns(Task.CompletedTask);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateRebalanceProposalAlertAsync(_userId, 1, It.IsAny<string>(), default), Times.Once);
        Assert.Equal(1, capturedOrderCount);
        Assert.Contains("Sell", capturedSummary);
        Assert.Contains("Equities", capturedSummary);
        // Sell notional: 75000 - (60/100 * 100000) = 75000 - 60000 = 15000
        Assert.Contains("15,000", capturedSummary);
    }

    [Fact]
    public async Task ExecuteAsync_UnderBandSleeve_GeneratesBuyOrder()
    {
        var sleeves = new List<AllocationSleeveDrift>
        {
            new("Bonds", 30m, 25m, 35m, 15m, 15_000m, -15m, "UnderBand"),
        };
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: true, sleeves));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync((RiskRuleSetDto?)null);

        string? capturedSummary = null;
        _alerts.Setup(a => a.GenerateRebalanceProposalAlertAsync(
                _userId, It.IsAny<int>(), It.IsAny<string>(), default))
            .Callback<Guid, int, string, CancellationToken>((_, _, summary, _) => capturedSummary = summary)
            .Returns(Task.CompletedTask);

        await _job.ExecuteAsync();

        Assert.Contains("Buy", capturedSummary);
        Assert.Contains("Bonds", capturedSummary);
        // Buy notional: (30/100 * 100000) - 15000 = 30000 - 15000 = 15000
        Assert.Contains("15,000", capturedSummary);
    }

    [Fact]
    public async Task ExecuteAsync_UnplannedSleeveAbove1Pct_GeneratesReviewOrder()
    {
        var sleeves = new List<AllocationSleeveDrift>
        {
            new("Crypto", 0m, 0m, 0m, 5m, 5_000m, 5m, "Unplanned"),
        };
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: true, sleeves));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync((RiskRuleSetDto?)null);

        string? capturedSummary = null;
        _alerts.Setup(a => a.GenerateRebalanceProposalAlertAsync(
                _userId, It.IsAny<int>(), It.IsAny<string>(), default))
            .Callback<Guid, int, string, CancellationToken>((_, _, summary, _) => capturedSummary = summary)
            .Returns(Task.CompletedTask);

        await _job.ExecuteAsync();

        Assert.Contains("Review", capturedSummary);
        Assert.Contains("Crypto", capturedSummary);
    }

    [Fact]
    public async Task ExecuteAsync_UnplannedSleeveBelowThreshold_NoOrder()
    {
        // Unplanned < 1% → still NeedsRebalance? Actually the handler sets NeedsRebalance=true only for >=1%.
        // Here we simulate NeedsRebalance=true from another sleeve but unplanned is below 1%.
        var sleeves = new List<AllocationSleeveDrift>
        {
            new("Equities", 60m, 55m, 65m, 75m, 75_000m, 15m, "OverBand"),
            new("Micro", 0m, 0m, 0m, 0.5m, 500m, 0.5m, "Unplanned"),
        };
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: true, sleeves));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync((RiskRuleSetDto?)null);

        int? capturedOrderCount = null;
        _alerts.Setup(a => a.GenerateRebalanceProposalAlertAsync(
                _userId, It.IsAny<int>(), It.IsAny<string>(), default))
            .Callback<Guid, int, string, CancellationToken>((_, count, _, _) => capturedOrderCount = count)
            .Returns(Task.CompletedTask);

        await _job.ExecuteAsync();

        // Only the OverBand sleeve contributes an order; Micro (0.5%) is below threshold
        Assert.Equal(1, capturedOrderCount);
    }

    [Fact]
    public async Task ExecuteAsync_ErrorInOneUser_OtherUsersStillProcessed()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([user1, user2]);

        // user1 throws; user2 succeeds with a proposal
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(user1), default))
            .ThrowsAsync(new InvalidOperationException("DB error"));
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(user2), default))
            .ReturnsAsync(BuildDrift(needsRebalance: true, [
                new AllocationSleeveDrift("Equities", 60m, 55m, 65m, 75m, 75_000m, 15m, "OverBand"),
            ]));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(user2), default))
            .ReturnsAsync((RiskRuleSetDto?)null);

        _alerts.Setup(a => a.GenerateRebalanceProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), default)).Returns(Task.CompletedTask);

        await _job.ExecuteAsync();

        // user2 still gets a proposal despite user1 failing
        _alerts.Verify(a => a.GenerateRebalanceProposalAlertAsync(
            user2, It.IsAny<int>(), It.IsAny<string>(), default), Times.Once);
        _alerts.Verify(a => a.GenerateRebalanceProposalAlertAsync(
            user1, It.IsAny<int>(), It.IsAny<string>(), default), Times.Never);
    }

    // ── US2: Cash-sweep proposal ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CashExceedsBuffer_GeneratesCashSweepProposal()
    {
        // idle cash = $20k; min buffer = 10% of $100k = $10k; excess = $10k
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: false, [], cashUsd: 20_000m));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync(BuildRules(minCashBufferPct: 10m));

        decimal? capturedExcess = null;
        _alerts.Setup(a => a.GenerateCashSweepProposalAlertAsync(
                _userId, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), default))
            .Callback<Guid, decimal, decimal, decimal, CancellationToken>((_, _, _, excess, _) => capturedExcess = excess)
            .Returns(Task.CompletedTask);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateCashSweepProposalAlertAsync(
            _userId, 20_000m, 10_000m, 10_000m, default), Times.Once);
        Assert.Equal(10_000m, capturedExcess);
    }

    [Fact]
    public async Task ExecuteAsync_CashBelowBuffer_NoCashSweepAlert()
    {
        // idle cash = $5k; min buffer = 10% of $100k = $10k; no excess
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: false, [], cashUsd: 5_000m));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync(BuildRules(minCashBufferPct: 10m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateCashSweepProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CashBelowBuffer_ResolvesExistingCashSweepProposal()
    {
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: false, [], cashUsd: 5_000m));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync(BuildRules(minCashBufferPct: 10m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.ResolveCashSweepProposalAlertAsync(_userId, default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CashExceedsBuffer_DoesNotResolveCashSweepProposal()
    {
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: false, [], cashUsd: 20_000m));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync(BuildRules(minCashBufferPct: 10m));
        _alerts.Setup(a => a.GenerateCashSweepProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), default))
            .Returns(Task.CompletedTask);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.ResolveCashSweepProposalAlertAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoRiskRules_NoCashSweepAlert()
    {
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: false, [], cashUsd: 50_000m));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync((RiskRuleSetDto?)null);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateCashSweepProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MinCashBufferPctIsZero_NoCashSweepAlert()
    {
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: false, [], cashUsd: 50_000m));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync(BuildRules(minCashBufferPct: 0m));

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateCashSweepProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_BothRebalanceAndCashSweep_BothAlertsGenerated()
    {
        var sleeves = new List<AllocationSleeveDrift>
        {
            new("Equities", 60m, 55m, 65m, 75m, 75_000m, 15m, "OverBand"),
        };
        // cash = $20k; min buffer = 10% of $100k = $10k; excess = $10k; AND rebalance needed
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsAsync(default)).ReturnsAsync([_userId]);
        _driftQuery.Setup(q => q.Handle(new GetAllocationDriftQuery(_userId), default))
            .ReturnsAsync(BuildDrift(needsRebalance: true, sleeves, cashUsd: 20_000m));
        _riskQuery.Setup(q => q.Handle(new GetRiskRuleSetQuery(_userId), default))
            .ReturnsAsync(BuildRules(minCashBufferPct: 10m));

        _alerts.Setup(a => a.GenerateRebalanceProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), default)).Returns(Task.CompletedTask);
        _alerts.Setup(a => a.GenerateCashSweepProposalAlertAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), default)).Returns(Task.CompletedTask);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateRebalanceProposalAlertAsync(
            _userId, It.IsAny<int>(), It.IsAny<string>(), default), Times.Once);
        _alerts.Verify(a => a.GenerateCashSweepProposalAlertAsync(
            _userId, 20_000m, 10_000m, 10_000m, default), Times.Once);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AllocationDriftDto BuildDrift(
        bool needsRebalance,
        IReadOnlyList<AllocationSleeveDrift> sleeves,
        bool hasIps = true,
        decimal totalValueUsd = 100_000m,
        decimal cashUsd = 10_000m)
        => new(hasIps, totalValueUsd, cashUsd, totalValueUsd - cashUsd, needsRebalance, sleeves, "quarterly");

    private static RiskRuleSetDto BuildRules(decimal? minCashBufferPct) =>
        new(Guid.NewGuid(), 1, null, null, minCashBufferPct, null, null, null, DateTimeOffset.UtcNow);
}
