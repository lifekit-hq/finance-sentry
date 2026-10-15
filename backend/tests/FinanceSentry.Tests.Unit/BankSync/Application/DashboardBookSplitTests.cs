namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The dashboard's "invested X · cash Y" sub-line reads the canonical book split from
/// <see cref="IBookFiguresService"/> and does no money math of its own (P8).
/// </summary>
public class DashboardBookSplitTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static DashboardQueryService Build(IBookFiguresService? bookFigures, decimal bankTotalUsd = 1_000m)
    {
        var aggregation = new Mock<IAggregationService>();
        aggregation.Setup(s => s.GetAggregatedBalanceAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        aggregation.Setup(s => s.GetAccountCountByTypeAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        aggregation.Setup(s => s.GetTotalNetWorthUsdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(bankTotalUsd);

        var counterparties = new Mock<ICounterpartyClassificationService>();
        counterparties.Setup(s => s.ClassifyForWindowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new CounterpartyClassificationResult([], []));
        var moneyFlow = new Mock<IMoneyFlowStatisticsService>();
        moneyFlow.Setup(s => s.GetMonthlyFlowAsync(UserId, It.IsAny<CounterpartyClassificationResult>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
        var categories = new Mock<IMerchantCategoryStatisticsService>();
        categories.Setup(s => s.GetTopCategoriesAsync(UserId, It.IsAny<CounterpartyClassificationResult>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([]);

        return new DashboardQueryService(
            aggregation.Object,
            moneyFlow.Object,
            categories.Object,
            counterparties.Object,
            new Mock<ISyncJobRepository>().Object,
            bookFigures: bookFigures);
    }

    private static IBookFiguresService BookOf(BookFigures figures)
    {
        var book = new Mock<IBookFiguresService>();
        book.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(figures);
        return book.Object;
    }

    [Fact]
    public async Task CashAndInvested_AreTheBookFigures_AndLeaveTheTotalAlone()
    {
        // Cash is net of card debt (BookFigures' own definition), so it can be below gross.
        var figures = new BookFigures(
            CashUsd: 700m, BankingCashUsd: 500m, BrokerageCashUsd: 150m, InvestedValueUsd: 9_300m,
            TotalValueUsd: 10_000m, Positions: [], IsStale: false, StaleSources: [], VenueCashUsd: 50m);

        var data = await Build(BookOf(figures), bankTotalUsd: 1_000m).GetDashboardDataAsync(UserId);

        data.CashUsd.Should().Be(700m);
        data.InvestedUsd.Should().Be(9_300m);
        data.TotalNetWorthUsd.Should().Be(1_000m, "the headline stays the live bank + crypto + brokerage sum");
    }

    [Fact]
    public async Task StaleBook_WithholdsTheSplit()
    {
        var figures = new BookFigures(
            CashUsd: 700m, BankingCashUsd: 700m, BrokerageCashUsd: 0m, InvestedValueUsd: 0m,
            TotalValueUsd: 700m, Positions: [], IsStale: true, StaleSources: ["crypto"]);

        var data = await Build(BookOf(figures)).GetDashboardDataAsync(UserId);

        data.CashUsd.Should().BeNull();
        data.InvestedUsd.Should().BeNull();
    }

    [Fact]
    public async Task NoBookFiguresService_LeavesTheSplitNull()
    {
        var data = await Build(null).GetDashboardDataAsync(UserId);

        data.CashUsd.Should().BeNull();
        data.InvestedUsd.Should().BeNull();
    }

    [Fact]
    public async Task Headlines_TypeTheThreeFigures_AsCompleteValues()
    {
        var figures = new BookFigures(
            CashUsd: 700m, BankingCashUsd: 500m, BrokerageCashUsd: 150m, InvestedValueUsd: 9_300m,
            TotalValueUsd: 10_000m, Positions: [], IsStale: false, StaleSources: [], VenueCashUsd: 50m);

        var data = await Build(BookOf(figures), bankTotalUsd: 1_000m).GetDashboardDataAsync(UserId);

        data.NetWorthHeadline!.Amount.Should().Be(1_000m, "the record describes the live total, not the book total");
        data.NetWorthHeadline.Scope.Should().Be(HeadlineScope.Umbrella);
        data.NetWorthHeadline.Quality.Should().Be(HeadlineQuality.Complete);
        data.CashHeadline!.Amount.Should().Be(700m);
        data.CashHeadline.Scope.Should().Be(HeadlineScope.CashSleeve);
        data.InvestedHeadline!.Amount.Should().Be(9_300m);
        data.InvestedHeadline.Scope.Should().Be(HeadlineScope.InvestedSleeve);
    }

    [Fact]
    public async Task Headlines_StaleBook_DegradesTheTotal_AndLeavesTheSplitUnavailable()
    {
        var figures = new BookFigures(
            CashUsd: 700m, BankingCashUsd: 700m, BrokerageCashUsd: 0m, InvestedValueUsd: 0m,
            TotalValueUsd: 700m, Positions: [], IsStale: true, StaleSources: ["crypto"]);

        var data = await Build(BookOf(figures)).GetDashboardDataAsync(UserId);

        data.NetWorthHeadline!.Quality.Should().Be(HeadlineQuality.Degraded);
        data.NetWorthHeadline.Reasons.Should().Contain(new HeadlineReason(HeadlineReasonCode.StaleSource, "crypto"));
        data.CashHeadline!.Amount.Should().BeNull("a withheld split is unavailable, not zero");
        data.CashHeadline.Quality.Should().Be(HeadlineQuality.Unavailable);
        data.CashHeadline.Reasons.Select(r => r.Code).Should().Contain(HeadlineReasonCode.NoSplit);
        data.InvestedHeadline!.Quality.Should().Be(HeadlineQuality.Unavailable);
    }
}
