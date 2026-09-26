using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetCashflowReportToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetMoneyFlowStatisticsQuery, IReadOnlyList<MonthlyFlow>>> _handler = new();

    private GetCashflowReportTool CreateSut() =>
        new(_handler.Object, new FakeIdentityResolver(), NullLogger<GetCashflowReportTool>.Instance);

    private static MonthlyFlow Flow(
        string month,
        string currency,
        decimal inflowUsd,
        decimal outflowUsd,
        decimal familySupportOutflowUsd = 0m,
        decimal investedOutflowUsd = 0m) =>
        new(
            month,
            currency,
            Inflow: inflowUsd,
            Outflow: outflowUsd,
            Net: inflowUsd - outflowUsd,
            InflowUsd: inflowUsd,
            OutflowUsd: outflowUsd,
            NetUsd: inflowUsd - outflowUsd,
            CommittedOutflowUsd: 0m,
            DiscretionaryOutflowUsd: outflowUsd,
            FamilySupportOutflowUsd: familySupportOutflowUsd,
            InvestedOutflowUsd: investedOutflowUsd);

    private void SetupHandler(params MonthlyFlow[] flows) =>
        _handler
            .Setup(h => h.Handle(It.IsAny<GetMoneyFlowStatisticsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MonthlyFlow>)flows);

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetMoneyFlowStatisticsQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenNoFlows()
    {
        SetupHandler();

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenFromIsAfterTo()
    {
        var result = await CreateSut().ExecuteAsync(
            UserId,
            fromDate: new DateOnly(2024, 6, 1),
            toDate: new DateOnly(2024, 1, 1));

        result.Should().BeEmpty();
        _handler.Verify(
            h => h.Handle(It.IsAny<GetMoneyFlowStatisticsQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SumsUsdFiguresAcrossCurrencyRows_PerMonth()
    {
        // Two per-currency rows plus the synthetic USD counterparty row for the same month —
        // the tool must sum InflowUsd/OutflowUsd across all three without double-counting.
        SetupHandler(
            Flow("2024-01", "USD", inflowUsd: 2000m, outflowUsd: 500m),
            Flow("2024-01", "UAH", inflowUsd: 0m, outflowUsd: 200m),
            Flow("2024-01", "USD", inflowUsd: 100m, outflowUsd: 50m, familySupportOutflowUsd: 50m),
            Flow("2024-02", "USD", inflowUsd: 3000m, outflowUsd: 1000m));

        var result = await CreateSut().ExecuteAsync(
            UserId,
            fromDate: new DateOnly(2024, 1, 1),
            toDate: new DateOnly(2024, 2, 29));

        result.Should().HaveCount(2);

        var jan = result.Single(e => e.Period == "2024-01");
        jan.Inflow.Should().Be(2100m);
        jan.Outflow.Should().Be(750m);
        jan.Net.Should().Be(1350m);
        jan.TransactionCount.Should().Be(0);

        var feb = result.Single(e => e.Period == "2024-02");
        feb.Inflow.Should().Be(3000m);
        feb.Outflow.Should().Be(1000m);
        feb.Net.Should().Be(2000m);
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesTransferClassifiedFlows_ReflectingClassifiedFigures()
    {
        // Regression for #675: the money-flow statistics service already strips internal
        // transfers before producing MonthlyFlow rows, so a transfer pair between the user's
        // own accounts must never surface as both inflow and outflow here. The mocked handler
        // stands in for that classification — its rows already reflect the transfer exclusion.
        SetupHandler(Flow("2024-03", "USD", inflowUsd: 5000m, outflowUsd: 1200m));

        var result = await CreateSut().ExecuteAsync(
            UserId,
            fromDate: new DateOnly(2024, 3, 1),
            toDate: new DateOnly(2024, 3, 31));

        var mar = result.Single();
        mar.Inflow.Should().Be(5000m);
        mar.Outflow.Should().Be(1200m);
        mar.Net.Should().Be(3800m);
    }

    [Fact]
    public async Task ExecuteAsync_FiltersMonthsOutsideRequestedRange()
    {
        SetupHandler(
            Flow("2024-01", "USD", inflowUsd: 100m, outflowUsd: 10m),
            Flow("2024-02", "USD", inflowUsd: 200m, outflowUsd: 20m),
            Flow("2024-03", "USD", inflowUsd: 300m, outflowUsd: 30m));

        var result = await CreateSut().ExecuteAsync(
            UserId,
            fromDate: new DateOnly(2024, 2, 1),
            toDate: new DateOnly(2024, 2, 29));

        result.Select(e => e.Period).Should().ContainSingle().Which.Should().Be("2024-02");
    }

    [Fact]
    public async Task ExecuteAsync_OrdersResults_Chronologically()
    {
        SetupHandler(
            Flow("2024-03", "USD", inflowUsd: 100m, outflowUsd: 0m),
            Flow("2024-01", "USD", inflowUsd: 100m, outflowUsd: 0m),
            Flow("2024-02", "USD", inflowUsd: 100m, outflowUsd: 0m));

        var result = await CreateSut().ExecuteAsync(
            UserId,
            fromDate: new DateOnly(2024, 1, 1),
            toDate: new DateOnly(2024, 3, 31));

        result.Select(e => e.Period).Should().ContainInOrder("2024-01", "2024-02", "2024-03");
    }

    [Fact]
    public async Task ExecuteAsync_MatchesMoneyFlowStatistics_ForSameFixtureAndWindow()
    {
        // Parity check for #675: the same MonthlyFlow rows the dashboard's money-flow
        // statistics query would produce for this user/window must roll up to the same
        // monthly inflow/outflow/net the tool reports — computed here independently of
        // the tool's own grouping code, by aggregating the fixture rows directly.
        var flows = new[]
        {
            Flow("2024-04", "USD", inflowUsd: 4000m, outflowUsd: 1500m),
            Flow("2024-04", "UAH", inflowUsd: 200m, outflowUsd: 300m),
            Flow("2024-05", "USD", inflowUsd: 5000m, outflowUsd: 2000m, familySupportOutflowUsd: 100m),
        };
        SetupHandler(flows);

        var expected = flows
            .GroupBy(f => f.Month)
            .ToDictionary(
                g => g.Key,
                g => (Inflow: g.Sum(f => f.InflowUsd), Outflow: g.Sum(f => f.OutflowUsd)));

        var result = await CreateSut().ExecuteAsync(
            UserId,
            fromDate: new DateOnly(2024, 4, 1),
            toDate: new DateOnly(2024, 5, 31));

        result.Should().HaveCount(expected.Count);
        foreach (var entry in result)
        {
            var (expectedInflow, expectedOutflow) = expected[entry.Period];
            entry.Inflow.Should().Be(expectedInflow);
            entry.Outflow.Should().Be(expectedOutflow);
            entry.Net.Should().Be(expectedInflow - expectedOutflow);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsToLastSixMonths_WhenNoDatesProvided()
    {
        GetMoneyFlowStatisticsQuery? captured = null;
        _handler
            .Setup(h => h.Handle(It.IsAny<GetMoneyFlowStatisticsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetMoneyFlowStatisticsQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync((IReadOnlyList<MonthlyFlow>)[]);

        await CreateSut().ExecuteAsync(UserId);

        captured.Should().NotBeNull();
        captured!.Months.Should().Be(6);
    }
}
