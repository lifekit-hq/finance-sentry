namespace FinanceSentry.Tests.Unit.MoneySemantics;

using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Services;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FinanceSentry.Tests.Unit.Fx;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Issue #1470 cases 17 and 19-23: the stored snapshot of the book (real snapshot job + real snapshot service over a
/// capturing repository) and the manual entries. Expected values are written from the blessed table
/// (docs/money-semantics.md section 8). Seed rates.
/// </summary>
public sealed class SnapshotAndManualCasesTests
{
    private static readonly Guid UserId = ScenarioS.UserId;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Stored(NetWorthSnapshot Snapshot, IReadOnlyList<NetWorthSnapshotSource> Rows);

    private static NetWorthSnapshot Previous(
        decimal banking, decimal brokerage, decimal crypto, string? stale = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            SnapshotDate = Today.AddDays(-1),
            BankingTotal = banking,
            BrokerageTotal = brokerage,
            CryptoTotal = crypto,
            TotalNetWorth = banking + brokerage + crypto,
            StaleSleeves = stale,
        };

    /// <summary>Runs the real job and service for the user; returns what was stored (null when the day was left unwritten).</summary>
    private static async Task<Stored?> CaptureAsync(
        BookFigures book,
        NetWorthSnapshot previous,
        IReadOnlyList<BrokerageHoldingSummary> brokerageHoldings,
        IReadOnlyList<CryptoHoldingSummary> cryptoHoldings,
        IReadOnlyList<NetWorthSnapshotSource>? previousRows = null,
        bool sleeveHeldBefore = false)
    {
        Stored? stored = null;
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetLatestBeforeUnscopedAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(previous);
        repo.Setup(r => r.GetSourcesUnscopedAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(previousRows ?? []);
        repo.Setup(r => r.SleeveHeldValueBeforeUnscopedAsync(UserId, It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sleeveHeldBefore);
        repo.Setup(r => r.UpsertWithSourcesAsync(
                It.IsAny<NetWorthSnapshot>(), It.IsAny<IReadOnlyList<NetWorthSnapshotSource>>(), It.IsAny<CancellationToken>()))
            .Callback<NetWorthSnapshot, IReadOnlyList<NetWorthSnapshotSource>, CancellationToken>((s, rows, _) => stored = new Stored(s, rows))
            .Returns(Task.CompletedTask);

        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetLatestSuccessfulSyncAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)DateTime.UtcNow);
        var bookService = new Mock<IBookFiguresService>();
        bookService.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(book);

        var job = new NetWorthSnapshotJob(
            banking.Object,
            ScenarioS.CryptoReader(cryptoHoldings).Object,
            ScenarioS.Readers(brokerageHoldings).Object,
            new NetWorthSnapshotService(repo.Object, NullLogger<NetWorthSnapshotService>.Instance),
            bookService.Object);

        await job.CaptureForUserAsync(UserId, Today);
        return stored;
    }

    [Fact]
    public async Task Case_17_StoredSnapshotOfS_Cash1164_Plus_BrokerageInvested4700_Plus_CryptoInvested2000_Is7864()
    {
        using var _ = FxTableScope.Seed();
        var book = await ScenarioS.BookOfSAsync();

        var stored = await CaptureAsync(book, Previous(864m, 5_000m, 2_000m), ScenarioS.BrokerageOfS(), ScenarioS.CryptoOfS());

        stored.Should().NotBeNull();
        var snapshot = stored!.Snapshot;
        snapshot.CashTotal.Should().Be(1_164.00m);
        snapshot.BrokerageInvested.Should().Be(4_700.00m);
        snapshot.CryptoInvested.Should().Be(2_000.00m);
        SnapshotSources.Of(snapshot, stored.Rows).OtherInvested.Should().Be(0m);
        snapshot.TotalNetWorth.Should().Be(7_864.00m);
    }

    [Fact]
    public async Task Case_19_BankingSleevePrevious500_TodayMinus432_FeedSynced_IsRecordedAsMinus432_NotCarriedForward()
    {
        using var _ = FxTableScope.Seed();
        var reader = ScenarioS.BankingReader(ScenarioS.Bank("checking", "EUR", 100m), ScenarioS.Bank("credit", "EUR", 500m));
        var book = await ScenarioS.BookAsync(reader, ScenarioS.BrokerageOfS(), ScenarioS.CryptoOfS());

        var stored = await CaptureAsync(book, Previous(500m, 5_000m, 2_000m), ScenarioS.BrokerageOfS(), ScenarioS.CryptoOfS());

        stored.Should().NotBeNull();
        stored!.Snapshot.BankingTotal.Should().Be(-432.00m);
        stored.Snapshot.StaleSleeves.Should().BeNull();
    }

    [Fact]
    public async Task Case_22_StaleBookSource_StoresNullSplit_NotZero_AndTheCashAndInvestedHeadlinesAreUnavailableWithNoSplit()
    {
        using var _ = FxTableScope.Seed();
        // The brokerage read fails, so the book is stale; the previous snapshot has a full split.
        var book = await ScenarioS.BookAsync(ScenarioS.BankingReader(ScenarioS.BankOfS()), null, ScenarioS.CryptoOfS());
        book.IsStale.Should().BeTrue();
        var previous = Previous(864m, 5_000m, 2_000m);

        var stored = await CaptureAsync(book, previous, ScenarioS.BrokerageOfS(), ScenarioS.CryptoOfS());

        stored.Should().NotBeNull();
        stored!.Snapshot.CashTotal.Should().BeNull();
        stored.Snapshot.BrokerageInvested.Should().BeNull();
        stored.Snapshot.CryptoInvested.Should().BeNull();

        var dashboard = await DashboardOver(book).GetDashboardDataAsync(UserId);
        dashboard.CashHeadline!.Amount.Should().BeNull();
        dashboard.CashHeadline.Quality.Should().Be(HeadlineQuality.Unavailable);
        dashboard.CashHeadline.Reasons.Select(r => r.Code).Should().Contain(HeadlineReasonCode.NoSplit);
        dashboard.InvestedHeadline!.Amount.Should().BeNull();
        dashboard.InvestedHeadline.Quality.Should().Be(HeadlineQuality.Unavailable);
        dashboard.InvestedHeadline.Reasons.Select(r => r.Code).Should().Contain(HeadlineReasonCode.NoSplit);
    }

    [Fact]
    public async Task Case_23_BrokeragePrevious5000_TodayReadsExactlyZero_IsCarriedAt5000_AndAStaleZeroIsNotCarriedASecondTime()
    {
        using var _ = FxTableScope.Seed();
        var reader = ScenarioS.BankingReader(ScenarioS.BankOfS());
        var book = await ScenarioS.BookAsync(reader, [], ScenarioS.CryptoOfS());

        var carried = await CaptureAsync(book, Previous(864m, 5_000m, 2_000m), [], ScenarioS.CryptoOfS());

        carried.Should().NotBeNull();
        carried!.Snapshot.BrokerageTotal.Should().Be(5_000m);
        carried.Snapshot.StaleSleeves.Should().Contain("brokerage");

        // The next day it is still $0: the previous row is itself a carried zero, so no second carry is written.
        var carriedZero = Previous(864m, 0m, 2_000m, stale: "brokerage");
        var second = await CaptureAsync(book, carriedZero, [], ScenarioS.CryptoOfS(), sleeveHeldBefore: true);

        second.Should().BeNull();
    }

    [Fact]
    public async Task Case_20_ManualSteam100Usd_InEconomy_Adds100ToTotalAndInvested_0ToCash_AndIsEstimated()
    {
        using var _ = FxTableScope.Seed();
        var book = await ManualBookAsync(ManualHolding.Create(UserId, "Steam", 100m, "USD", Today, Convertibility.InEconomy));

        book.TotalValueUsd.Should().Be(100m);
        book.InvestedValueUsd.Should().Be(100m);
        book.CashUsd.Should().Be(0m);
        HeadlineSignals.EstimatedIn(book, r => r.TotalValueUsd).Should().Equal("manual");
        HeadlineSignals.EstimatedIn(book, r => r.InvestedValueUsd).Should().Equal("manual");
        HeadlineSignals.EstimatedIn(book, r => r.CashUsd).Should().BeEmpty();
        HeadlineQualityMapper.Assess(HeadlineSignals.FromBook(book), true).Reasons
            .Should().Equal(new HeadlineReason(HeadlineReasonCode.EstimatedValue, "manual"));
    }

    [Fact]
    public async Task Case_21_ManualEntry200Eur_Cash_Adds216ToCashAndTotal_InvestedUnchanged()
    {
        using var _ = FxTableScope.Seed();
        var book = await ManualBookAsync(ManualHolding.Create(UserId, "Wallet", 200m, "EUR", Today, Convertibility.Cash));

        book.CashUsd.Should().Be(216.00m);
        book.TotalValueUsd.Should().Be(216.00m);
        book.InvestedValueUsd.Should().Be(0m);
    }

    private static Task<BookFigures> ManualBookAsync(ManualHolding holding)
    {
        var repo = new Mock<IManualHoldingRepository>();
        repo.Setup(r => r.ListUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([holding]);
        return new BookFiguresService([new ManualWealthSource(repo.Object)], NullLogger<BookFiguresService>.Instance).ReadAsync(UserId);
    }

    private static DashboardQueryService DashboardOver(BookFigures figures)
    {
        var aggregation = new Mock<IAggregationService>();
        aggregation.Setup(s => s.GetAggregatedBalanceAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        aggregation.Setup(s => s.GetAccountCountByTypeAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        aggregation.Setup(s => s.GetTotalNetWorthUsdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(1_000m);
        var counterparties = new Mock<ICounterpartyClassificationService>();
        counterparties.Setup(s => s.ClassifyForWindowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CounterpartyClassificationResult([], []));
        var moneyFlow = new Mock<IMoneyFlowStatisticsService>();
        moneyFlow.Setup(s => s.GetMonthlyFlowAsync(UserId, It.IsAny<CounterpartyClassificationResult>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var categories = new Mock<IMerchantCategoryStatisticsService>();
        categories.Setup(s => s.GetTopCategoriesAsync(UserId, It.IsAny<CounterpartyClassificationResult>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var book = new Mock<IBookFiguresService>();
        book.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(figures);

        return new DashboardQueryService(
            aggregation.Object, moneyFlow.Object, categories.Object, counterparties.Object,
            new Mock<ISyncJobRepository>().Object, bookFigures: book.Object);
    }
}
