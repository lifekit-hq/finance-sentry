namespace FinanceSentry.Tests.Integration.CrossModulePorts;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Integration;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>
/// 043/414: the portfolio-scan port is the single seam where the risk rule set (stored as a
/// fraction in (0,1]) meets <c>PortfolioScanData</c> (contractually percentage points, 0–100).
/// Passing the fraction through made every cash-buffer check pass and every concentration check
/// fail, so the weekly brief printed an action line with no breach behind it.
/// #673: composed over Research's <c>IAllocationDriftReader</c> and Risk's <c>IRiskLimitsReader</c> ports.
/// </summary>
public sealed class PortfolioScanDataReaderTests
{
    private static readonly Guid UserId = Guid.Parse("22222222-0000-0000-0000-000000000001");

    private sealed class StubBook(BookFigures figures) : IBookFiguresService
    {
        public Task<BookFigures> ReadAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(figures);
    }

    private sealed class StubDrift(AllocationDriftDto dto)
        : IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto>
    {
        public Task<AllocationDriftDto> Handle(GetAllocationDriftQuery query, CancellationToken cancellationToken)
            => Task.FromResult(dto);
    }

    private sealed class StubRiskRules(RiskRuleSet? current) : IRiskRuleSetRepository
    {
        public Task<RiskRuleSet?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(current);

        public Task<RiskRuleSet> SaveNewVersionAsync(RiskRuleSet ruleSet, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetUserIdsWithRuleSetsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([UserId]);
    }

    private sealed class StubIps : IIpsRepository
    {
        public Task<InvestmentPolicyStatement?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<InvestmentPolicyStatement?>(null);

        public Task<IReadOnlyList<InvestmentPolicyStatement>> ListVersionsAsync(
            Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InvestmentPolicyStatement>>([]);

        public Task<int> GetMaxVersionAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task AddVersionAsync(InvestmentPolicyStatement ips, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InvestmentPolicyStatement?> GetCurrentUnscopedAsync(Guid userId, CancellationToken ct = default)
            => GetCurrentAsync(userId, ct);

        public Task<IReadOnlyList<InvestmentPolicyStatement>> ListVersionsUnscopedAsync(Guid userId, CancellationToken ct = default)
            => ListVersionsAsync(userId, ct);

        public Task<IReadOnlyList<Guid>> GetUserIdsWithCurrentIpsUnscopedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private static PortfolioScanDataReader Reader(RiskRuleSet? rules) =>
        new(
            new StubBook(new BookFigures(
                CashUsd: 2_000m,
                BankingCashUsd: 2_000m,
                BrokerageCashUsd: 0m,
                InvestedValueUsd: 98_000m,
                TotalValueUsd: 100_000m,
                Positions: [new BookFigurePosition("NVDA", "Equity", 100m, null, 30_000m, "IBKR")],
                IsStale: false,
                StaleSources: [])),
            new AllocationDriftReader(
                new StubDrift(new AllocationDriftDto(
                    HasIps: false,
                    TotalValueUsd: 100_000m,
                    CashUsd: 2_000m,
                    InvestedValueUsd: 98_000m,
                    NeedsRebalance: false,
                    Sleeves: [],
                    RebalancingCadence: "Quarterly")),
                new StubIps()),
            new RiskLimitsReader(new StubRiskRules(rules)));

    [Fact]
    public async Task ConvertsRiskRuleFractionsToPercentagePoints()
    {
        // Stored as fractions: 25% cap, 5% cash floor.
        var reader = Reader(new RiskRuleSet { MaxPositionWeightPct = 0.25m, MinCashBufferPct = 0.05m });

        var data = await reader.ReadAsync(UserId);

        data.Should().NotBeNull();
        data!.MaxPositionWeightPct.Should().Be(25m);
        data.MinCashBufferPct.Should().Be(5m);
    }

    [Fact]
    public async Task ConvertedLimitsAreComparableToTheDataPercentages()
    {
        var reader = Reader(new RiskRuleSet { MaxPositionWeightPct = 0.25m, MinCashBufferPct = 0.05m });

        var data = await reader.ReadAsync(UserId);

        // 2k cash of a 100k book is a real breach of the 5% floor; a 30% position breaches the 25% cap.
        data!.CashPct.Should().Be(2m);
        data.CashPct.Should().BeLessThan(data.MinCashBufferPct!.Value);
        data.TopPositions[0].WeightPct.Should().Be(30m);
        data.TopPositions[0].WeightPct.Should().BeGreaterThan(data.MaxPositionWeightPct!.Value);
    }

    [Fact]
    public async Task LeavesLimitsNullWhenNoRuleSetExists()
    {
        var reader = Reader(rules: null);

        var data = await reader.ReadAsync(UserId);

        data!.MaxPositionWeightPct.Should().BeNull();
        data.MinCashBufferPct.Should().BeNull();
    }

    [Fact]
    public async Task LeavesAnIndividualLimitNullWhenThatRuleIsUnset()
    {
        var reader = Reader(new RiskRuleSet { MaxPositionWeightPct = 0.25m, MinCashBufferPct = null });

        var data = await reader.ReadAsync(UserId);

        data!.MaxPositionWeightPct.Should().Be(25m);
        data.MinCashBufferPct.Should().BeNull();
    }
}
