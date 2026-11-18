namespace FinanceSentry.Tests.Unit.MoneySemantics;

using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Wealth.API.Controllers;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FinanceSentry.Tests.Unit.Fx;
using FinanceSentry.Tests.Unit.Wealth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// Issue #1470 cases 25 and 28-30: the book against SPY, the dated EUR display of the history, the holdings-based
/// return and the pooled savings rate. Expected values are written from the blessed table.
/// </summary>
public sealed class ReturnAndHistoryCasesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // ---- Case 25 ----

    [Fact]
    public async Task Case_25_BookTwr_Is3_71Percent_AndThePlainChangeIs9800_98Percent_ALabelledValueChange_NotAReturn()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-7);
        var bars = new Mock<IDailyBarRepository>();
        bars.Setup(r => r.GetSinceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), default)).ReturnsAsync([]);
        bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default))
            .ReturnsAsync([Bar("SPY", since, 400m), Bar("SPY", today, 404m)]);
        var portfolio = new Mock<IPortfolioValueSource>();
        // Start 10,000 -> 11,000 (+10%), then a 10,000 deposit lands and the end reads 19,800 (21,000 with it).
        portfolio.Setup(p => p.GetAsync(UserId, It.IsAny<DateOnly>(), today, default)).ReturnsAsync(
            [Total(since, 10_000m), Total(today.AddDays(-3), 11_000m), Total(today, 19_800m)]);
        var holdings = new Mock<IHoldingsHistorySource>();
        holdings.Setup(h => h.GetBrokerageCapturesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), default)).ReturnsAsync([]);
        var flows = new Mock<IBrokerageFlowReader>();
        flows.Setup(f => f.ReadAsync(UserId, It.IsAny<DateOnly>(), default)).ReturnsAsync(new SleeveFlowReading(
            [new FlowSourceCoverage("ibkr", since.AddDays(-30), true)],
            [new SourceFlow("ibkr", new ClassifiedFlow(today, FlowClass.ExternalInflow, 10_000m), FxStatus.Exact)]));

        var period = (await new BookPerformanceService(bars.Object, portfolio.Object, holdings.Object, flows.Object)
            .GetAsync(UserId, [BookPerformancePeriod.OneWeek])).Periods.Single();

        period.BookTwr.Should().BeApproximately(0.0371m, 0.00005m);
        period.Book!.Amount.Should().Be(9_800m);
        period.Book.Percent.Should().Be(98.00m);
        period.Book.Method.Should().Be(HeadlineMethod.ValueChange, "the plain change includes deposits and is never named a return");
    }

    private static DailyPortfolioValue Total(DateOnly date, decimal total) => new(date, total, null, total);

    private static DailyBar Bar(string ticker, DateOnly date, decimal adjClose) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = ticker,
        Date = date,
        Open = adjClose,
        High = adjClose,
        Low = adjClose,
        Close = adjClose,
        AdjClose = adjClose,
        Volume = 1_000_000,
    };

    // ---- Case 28 ----

    private static readonly DateOnly Day1 = new(2026, 3, 6);
    private static readonly DateOnly Day2 = new(2026, 3, 9);

    [Fact]
    public async Task Case_28_HistoryInEur_FlatUsd10000_Rates1_25Then1_00_RowsEur8000Then10000_ChangePlus2000_Plus25Percent_NoFxReason()
    {
        using var _ = FxTableScope.Seed();
        var rates = new StubPublishedRates(new() { ["EUR"] = new() { [Day1] = 1.25m, [Day2] = 1.00m } });
        var first = Row(Day1, 10_000m);
        var last = Row(Day2, 10_000m);
        var usdChange = HeadlineFactory.Change(
            first.TotalNetWorth, last.TotalNetWorth, HeadlineScope.Umbrella, Day1, Day2, false, HeadlineSignals.None);

        var result = await HistoryAsync("EUR", rates, usdChange, [first, last]);

        result.Snapshots[0].TotalNetWorth.Should().Be(8_000m);
        result.Snapshots[1].TotalNetWorth.Should().Be(10_000m);
        var headline = result.ChangeHeadline!;
        headline.Amount.Should().Be(2_000m);
        headline.Percent.Should().Be(25.00m);
        headline.Method.Should().Be(HeadlineMethod.ValueChange);
        headline.Reasons.Should().NotContain(r => r.Code == HeadlineReasonCode.FxTodayRate);
    }

    private static NetWorthSnapshotDto Row(DateOnly date, decimal usd) =>
        new(date, usd, 0m, 0m, usd, "USD", null, false, CashTotal: usd);

    private static async Task<NetWorthHistoryResponse> HistoryAsync(
        string baseCurrency, StubPublishedRates rates, Headline changeHeadline, NetWorthSnapshotDto[] snapshots)
    {
        var handler = new Mock<IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse>>();
        handler.Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse(snapshots, snapshots.Length > 0, null, changeHeadline));
        var currencies = new Mock<IUserBaseCurrencyReader>();
        currencies.Setup(c => c.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(baseCurrency);

        var controller = new NetWorthHistoryController(handler.Object, currencies.Object, rates)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test")),
                },
            },
        };

        var result = await controller.GetHistory();
        return result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<NetWorthHistoryResponse>().Subject;
    }

    // ---- Case 29 ----

    [Fact]
    public void Case_29_HoldingsBasedReturn_1000InPositions_Plus500DepositBuying5SharesAt100_ValueChangePlus50_ReturnIs0()
    {
        var start = new HoldingsCapture(
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), [new CapturedHolding("AAA", 10m, 1_000m)]);
        var end = new HoldingsCapture(
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero), [new CapturedHolding("AAA", 15m, 1_500m)]);

        var result = HoldingsBasedReturnCalculator.Measure([start, end]);

        (1_500m / 1_000m - 1m).Should().Be(0.5m, "the plain value change is +50%");
        result.Value.Should().Be(0m);
    }

    // ---- Case 30 ----

    [Fact(Skip = "Blessed case 30, tracked in https://github.com/lifekit-hq/finance-sentry/issues/1488")]
    public async Task Case_30_CashflowProfile_TwoCompleteMonths_Income1000Net100_Income3000Net900_PooledSavingsRateIs25Percent()
    {
        var now = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
        var snapshots = new Mock<INetWorthSnapshotRepository>();
        snapshots.Setup(r => r.GetLatestByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new NetWorthSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            SnapshotDate = new DateOnly(2026, 9, 20),
            TotalNetWorth = 90_000m,
            TakenAt = now,
            CashTotal = 18_000m,
        });
        var monthly = new Mock<IHonestMonthlyFlowReader>();
        // Income is each month's net plus its outflow: 100 + 900 = 1,000 and 900 + 2,100 = 3,000.
        monthly.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            [new HonestMonthlyFlow("2026-07", 900m, 100m), new HonestMonthlyFlow("2026-08", 2_100m, 900m)]);
        var handler = new GetCashflowProfileQueryHandler(new ProjectionInputsReader(
            snapshots.Object, monthly.Object, Mock.Of<IUserFireAssumptionsReader>(), NoCoverageHoles.Reader, new FakeTimeProvider(now)));

        var result = await handler.Handle(new GetCashflowProfileQuery(UserId), CancellationToken.None);

        result.SavingsRate.Should().Be(0.25m, "pooled 1,000 / 4,000, not the mean of 10% and 30%");
    }
}
