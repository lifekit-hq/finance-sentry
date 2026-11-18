namespace FinanceSentry.Tests.Unit.MoneySemantics;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Unit.Fx;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

/// <summary>
/// Issue #1470 cases 24, 26 and 27: a sleeve's growth is its change net of what was paid in and what a newly linked
/// provider brought (docs/money-semantics.md section 8, finance-framework 3.2 / 3.5). Expected values are written
/// from the blessed table.
/// </summary>
public sealed class SleeveGrowthCasesTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly OneJuly = new(2026, 7, 1);

    private static DateOnly Day(int n) => OneJuly.AddDays(n);

    private static DailyBrokerageValue Value(int day, decimal total) => new(Day(day), total, total);

    private static SourceFlow Deposit(int day, decimal usd) =>
        new("ibkr", new ClassifiedFlow(Day(day), FlowClass.ExternalInflow, usd), FxStatus.Exact);

    private static Task<SleeveGrowthResponse> GrowthAsync(
        DailyBrokerageValue[] values, FlowSourceCoverage[] sources, params SourceFlow[] flows)
    {
        var history = new Mock<IBrokerageValueHistoryReader>();
        history.Setup(h => h.GetDailyAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(values);
        var flowReader = new Mock<IBrokerageFlowReader>();
        flowReader.Setup(f => f.ReadAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SleeveFlowReading(sources, flows));

        return new GetSleeveGrowthQueryHandler(
                history.Object, flowReader.Object, Mock.Of<INetWorthSnapshotRepository>(), Mock.Of<ICryptoFlowReader>())
            .Handle(new GetSleeveGrowthQuery(UserId, WealthSourceIds.Brokerage), CancellationToken.None);
    }

    [Fact]
    public async Task Case_24_BrokerageGrowth_Start10000_Deposit10000On1Jul_End19800_GrowthMinus200_ModifiedDietzMinus1_33Percent()
    {
        // An inflow counts from the start of its day (the previous close, the framework's M3/M4 placement), so a
        // 20 Jun - 10 Jul window puts the 1 Jul deposit exactly at its midpoint (weight 0.5).
        var result = await GrowthAsync(
            [Value(-11, 10_000m), Value(0, 20_000m), Value(9, 19_800m)],
            [new FlowSourceCoverage("ibkr", Day(-30), true)],
            Deposit(0, 10_000m));

        result.Contributed.Should().Be(10_000m);
        result.Growth.Should().Be(-200m);
        result.MoneyWeightedReturn.Should().BeApproximately(-0.0133m, 0.00005m);
    }

    [Fact]
    public async Task Case_26_Growth_Start10000_End15000_ProviderLinkedBringing4000_NoDeposits_Growth1000()
    {
        var result = await GrowthAsync(
            [Value(0, 10_000m), Value(1, 10_000m), Value(2, 14_000m), Value(3, 14_000m), Value(10, 15_000m)],
            [new FlowSourceCoverage("ibkr", Day(-30), true), new FlowSourceCoverage("inzhur", Day(2), true)]);

        result.NewlyLinked.Should().Be(4_000m);
        result.Contributed.Should().Be(0m);
        result.Growth.Should().Be(1_000m);
    }

    [Fact]
    public async Task Case_27_EurDeposit1000_AtItsOwnDatesRate1_05_Contributed1050_End11080_Growth30_NotReConvertedAt1_08()
    {
        using var _ = FxTableScope.Seed();
        var depositDay = Day(1);
        var userId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<BrokerageSyncDbContext>()
            .UseInMemoryDatabase($"case-27-{Guid.NewGuid()}").Options;
        await using (var seed = new BrokerageSyncDbContext(options, NoCurrentUser.Instance))
        {
            var link = new IBKRFlexCredential(userId, "999999", [1], [2], [3], 1);
            seed.IBKRFlexCredentials.Add(link);
            seed.Entry(link).Property(c => c.FirstLinkedAt).CurrentValue = Day(-30).ToDateTime(TimeOnly.MinValue);
            seed.BrokerageCashTransactions.Add(new BrokerageCashTransaction(
                userId, "ibkr", "case-27", null, null, null, depositDay.ToDateTime(TimeOnly.MinValue), 1_000m,
                "Deposits/Withdrawals", null, null, "EUR", null));
            await seed.SaveChangesAsync();
        }

        // The feed published EUR at 1.05 on the deposit's own date; today's (seed) rate is 1.08.
        var feed = new Mock<IHistoricalExchangeRateService>();
        feed.Setup(f => f.GetPublishedSeriesAsync("EUR", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DateOnly, decimal> { [depositDay] = 1.05m });

        await using var db = new BrokerageSyncDbContext(options, NoCurrentUser.Instance);
        var reading = await new BrokerageFlowReader(db, feed.Object).ReadAsync(userId, Day(0));
        var result = await GrowthAsync(
            [Value(0, 10_000m), Value(1, 11_050m), Value(10, 11_080m)], [.. reading.Sources], [.. reading.Flows]);

        reading.Flows.Should().ContainSingle().Which.Flow.AmountUsd.Should().Be(1_050m);
        result.Contributed.Should().Be(1_050m);
        result.EndValue.Should().Be(11_080m);
        result.Growth.Should().Be(30m);
    }
}
