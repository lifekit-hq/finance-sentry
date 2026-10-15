namespace FinanceSentry.Tests.Unit.Wealth;

using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Services;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.Wealth.API.Controllers;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FinanceSentry.Tests.Unit.Fx;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Framework R4: a headline figure built from a seed-table or 1:1 conversion carries the matching
/// reason, from the reader that converted it through the book figures, the snapshot job and the
/// net-worth history.
/// </summary>
public sealed class FxStatusHeadlineTests
{
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly Dictionary<string, decimal> LiveEur = new() { ["EUR"] = 1.09m };

    // ---- Readers -> BookFiguresService ----

    private static BankingAccountsReader BankingReader(params (string Currency, decimal Balance)[] accounts)
    {
        var repo = new Mock<IBankAccountRepository>();
        repo.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts.Select((a, i) => new BankAccount
            {
                UserId = UserId,
                BankName = $"Bank {i}",
                AccountType = "checking",
                AccountNumberLast4 = "1234",
                Provider = "monobank",
                Currency = a.Currency,
                CurrentBalance = a.Balance,
                SyncStatus = "active",
            }).ToList());
        var jobs = new Mock<ISyncJobRepository>();
        jobs.Setup(j => j.GetLastSuccessfulSyncTimesByUserUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DateTime>());
        return new BankingAccountsReader(repo.Object, jobs.Object, Mock.Of<IClosedAccountsReader>());
    }

    private static BookFiguresService Book(
        IBankingAccountsReader banking, params CryptoHoldingSummary[] crypto)
    {
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var cryptoReader = new Mock<ICryptoHoldingsReader>();
        cryptoReader.Setup(c => c.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(crypto);
        return new BookFiguresService(
            banking, brokerage.Object, cryptoReader.Object, NullLogger<BookFiguresService>.Instance);
    }

    [Fact]
    public async Task BookFigures_AreExactWithNoReasons_WhenEveryAccountUsesALiveRate()
    {
        using var _ = FxTableScope.Live(LiveEur);

        var book = await Book(BankingReader(("EUR", 100m), ("USD", 50m))).ReadAsync(UserId);

        book.FxStatus.Should().Be(FxStatus.Exact);
        book.FxReasonCodes.Should().BeEmpty();
    }

    [Fact]
    public async Task BookFigures_CarryFxTodayRate_WhenAnAccountWasConvertedOnTheSeedTable()
    {
        using var _ = FxTableScope.Live(LiveEur);

        var book = await Book(BankingReader(("EUR", 100m), ("UAH", 1_000m))).ReadAsync(UserId);

        book.FxStatus.Should().Be(FxStatus.SeedTable);
        book.FxReasonCodes.Should().Equal("FX_TODAY_RATE");
    }

    [Fact]
    public async Task BookFigures_CarryFxUnknown1To1_AndStillSumTheAmount_WhenACurrencyHasNoRate()
    {
        using var _ = FxTableScope.Live(LiveEur);

        var book = await Book(BankingReader(("EUR", 100m), ("XYZ", 40m))).ReadAsync(UserId);

        book.BankingCashUsd.Should().Be(109m + 40m); // 1:1 is kept, never dropped or thrown on
        book.FxStatus.Should().Be(FxStatus.Unknown1To1);
        book.FxReasonCodes.Should().Equal("FX_UNKNOWN_1TO1");
    }

    [Fact]
    public async Task BookFigures_TakeTheWorstStatusAcrossBankingAndCrypto()
    {
        using var _ = FxTableScope.Live(LiveEur);
        var btc = new CryptoHoldingSummary("BTC", 1m, 0m, 60_000m, DateTime.UtcNow, "binance");
        var xyzFiat = new CryptoHoldingSummary(
            "XYZ", 10m, 0m, 10m, DateTime.UtcNow, "revolut-x", IsVenueFiat: true, FxStatus: FxStatus.Unknown1To1);

        var book = await Book(BankingReader(("UAH", 1_000m)), btc, xyzFiat).ReadAsync(UserId);

        book.FxStatus.Should().Be(FxStatus.Unknown1To1);
    }

    [Fact]
    public async Task BookFigures_IgnoreTheStatusOfAnAccountThatReportsNoBalance()
    {
        using var _ = FxTableScope.Live(LiveEur);
        var noBalance = new BankingAccountSummary(
            Guid.NewGuid(), "Bank", "checking", "1234", "monobank", "XYZ", null, null, "synced", null,
            FxStatus: FxStatus.Unknown1To1);
        var banking = new Mock<IBankingAccountsReader>();
        banking.Setup(b => b.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([noBalance]);

        var book = await Book(banking.Object).ReadAsync(UserId);

        book.FxStatus.Should().Be(FxStatus.Exact);
    }

    [Fact]
    public void BookFiguresEmpty_IsExact()
    {
        BookFigures.Empty.FxStatus.Should().Be(FxStatus.Exact);
        BookFigures.Empty.FxReasonCodes.Should().BeEmpty();
    }

    // ---- Crypto venue fiat ----

    [Fact]
    public void CryptoVenueFiat_ReportsTheConversionStatusOfItsCurrency_AndCryptoStaysExact()
    {
        using var _ = FxTableScope.Live(LiveEur);

        var eur = CryptoHoldingValuation.Convert(CryptoHolding.Create(UserId, "revolut-x", "EUR", 100m, 0m, 0m, isFiat: true));
        var uah = CryptoHoldingValuation.Convert(CryptoHolding.Create(UserId, "revolut-x", "UAH", 1_000m, 0m, 0m, isFiat: true));
        var btc = CryptoHoldingValuation.Convert(CryptoHolding.Create(UserId, "binance", "BTC", 1m, 0m, 60_000m));

        eur.Should().Be(new ConvertResult(109m, FxStatus.Exact));
        uah.Status.Should().Be(FxStatus.SeedTable);
        btc.Should().Be(new ConvertResult(60_000m, FxStatus.Exact));
    }

    // ---- Snapshot job ----

    private static Mock<INetWorthSnapshotService> CapturingService(Action<NetWorthSnapshotData> capture)
    {
        var service = new Mock<INetWorthSnapshotService>();
        service.Setup(s => s.PersistSnapshotAsync(UserId, It.IsAny<NetWorthSnapshotData>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NetWorthSnapshotData, CancellationToken>((_, data, _) => capture(data))
            .Returns(Task.CompletedTask);
        return service;
    }

    private static NetWorthSnapshotJob Job(
        INetWorthSnapshotService service,
        FxStatus bookStatus,
        FxStatus cryptoStatus = FxStatus.Exact,
        FxStatus brokerageStatus = FxStatus.Exact,
        FxStatus closedPositionStatus = FxStatus.Exact)
    {
        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([UserId]);
        banking.Setup(b => b.GetTotalUsdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(1_000m);
        var crypto = new Mock<ICryptoHoldingsReader>();
        crypto.Setup(c => c.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new CryptoHoldingSummary("BTC", 1m, 0m, 500m, DateTime.UtcNow, "binance", FxStatus: cryptoStatus),
                new CryptoHoldingSummary("XYZ", 0m, 0m, 0m, DateTime.UtcNow, "binance", FxStatus: closedPositionStatus),
            ]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new BrokerageHoldingSummary("AAPL", "equity", 1m, 700m, DateTime.UtcNow, "ibkr", FxStatus: brokerageStatus),
                new BrokerageHoldingSummary("OLD", "equity", 0m, 0m, DateTime.UtcNow, "ibkr", FxStatus: closedPositionStatus),
            ]);
        var book = new Mock<IBookFiguresService>();
        book.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookFigures.Empty with { FxStatus = bookStatus });

        return new NetWorthSnapshotJob(banking.Object, crypto.Object, brokerage.Object, service, book.Object);
    }

    [Theory]
    [InlineData(FxStatus.Exact, FxStatus.Exact, FxStatus.Exact, FxStatus.Exact)]
    [InlineData(FxStatus.SeedTable, FxStatus.Exact, FxStatus.Exact, FxStatus.SeedTable)]
    [InlineData(FxStatus.Exact, FxStatus.Unknown1To1, FxStatus.Exact, FxStatus.Unknown1To1)]
    [InlineData(FxStatus.TodayRate, FxStatus.Exact, FxStatus.SeedTable, FxStatus.SeedTable)]
    public async Task SnapshotJob_CarriesTheWorstStatusOfTheFiguresItSummed(
        FxStatus book, FxStatus crypto, FxStatus brokerage, FxStatus expected)
    {
        NetWorthSnapshotData? captured = null;
        var service = CapturingService(d => captured = d);

        await Job(service.Object, book, crypto, brokerage).ExecuteForUserAsync(UserId);

        captured.Should().NotBeNull();
        captured!.FxStatus.Should().Be(expected);
    }

    [Fact]
    public async Task SnapshotJob_LeavesOutClosedPositions_WhenFoldingTheStatus()
    {
        NetWorthSnapshotData? captured = null;
        var service = CapturingService(d => captured = d);

        await Job(service.Object, FxStatus.Exact, closedPositionStatus: FxStatus.Unknown1To1).ExecuteForUserAsync(UserId);

        captured!.FxStatus.Should().Be(FxStatus.Exact);
    }

    [Fact]
    public async Task SnapshotService_StoresTheWorstStatusOnTheSnapshotRow()
    {
        NetWorthSnapshot? stored = null;
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.UpsertAsync(It.IsAny<NetWorthSnapshot>(), It.IsAny<CancellationToken>()))
            .Callback<NetWorthSnapshot, CancellationToken>((s, _) => stored = s)
            .Returns(Task.CompletedTask);

        await new NetWorthSnapshotService(repo.Object).PersistSnapshotAsync(
            UserId,
            new NetWorthSnapshotData(new DateOnly(2026, 10, 1), 100m, 0m, 0m, FxStatus: FxStatus.Unknown1To1),
            CancellationToken.None);

        stored!.FxStatus.Should().Be(FxStatus.Unknown1To1);
    }

    // ---- Net-worth history ----

    private static NetWorthSnapshot Row(bool approximate, FxStatus? fxStatus = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        SnapshotDate = new DateOnly(2026, 10, 1),
        BankingTotal = 100m,
        TotalNetWorth = 100m,
        Currency = "USD",
        IsApproximate = approximate,
        FxStatus = fxStatus,
    };

    [Fact]
    public async Task History_ReportsFxTodayRate_ForABackfilledRow_AndNothingForAMeasuredOne()
    {
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row(approximate: true), Row(approximate: false)]);

        var result = await new GetNetWorthHistoryQueryHandler(repo.Object, [])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        result.Snapshots[0].FxReasons.Should().Equal("FX_TODAY_RATE");
        result.Snapshots[1].FxReasons.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FxStatus.Exact, null)]
    [InlineData(FxStatus.SeedTable, "FX_TODAY_RATE")]
    [InlineData(FxStatus.Unknown1To1, "FX_UNKNOWN_1TO1")]
    public async Task History_MapsTheStatusStoredOnAMeasuredRow(FxStatus stored, string? expected)
    {
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row(approximate: false, fxStatus: stored)]);

        var result = await new GetNetWorthHistoryQueryHandler(repo.Object, [])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        result.Snapshots[0].FxReasons.Should().Equal(expected is null ? [] : [expected]);
    }

    private static async Task<NetWorthHistoryResponse> ControllerHistoryAsync(string baseCurrency, NetWorthSnapshotDto dto)
    {
        var handler = new Mock<IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse>>();
        handler.Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse([dto], true));
        var currencies = new Mock<IUserBaseCurrencyReader>();
        currencies.Setup(c => c.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(baseCurrency);
        var controller = new NetWorthHistoryController(handler.Object, currencies.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test")),
                },
            },
        };

        var result = await controller.GetHistory();
        return result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<NetWorthHistoryResponse>().Subject;
    }

    private static NetWorthSnapshotDto HistoryDto(DateOnly date, params string[] reasons) =>
        new(date, 100m, 0m, 0m, 100m, "USD", null, false, FxReasons: reasons);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task HistoryController_AddsFxTodayRate_ToAPastRowShownInANonUsdBase()
    {
        using var _ = FxTableScope.Live(LiveEur);

        var eur = await ControllerHistoryAsync("EUR", HistoryDto(Today.AddDays(-30)));

        eur.Snapshots[0].FxReasons.Should().Equal("FX_TODAY_RATE");
        eur.Snapshots[0].TotalNetWorth.Should().Be(CurrencyConverter.FromUsd(100m, "EUR"));
    }

    [Fact]
    public async Task HistoryController_AddsNothing_ToAPastRowInUsd_OrATodayRowOnALiveRate()
    {
        using var _ = FxTableScope.Live(LiveEur);

        var usd = await ControllerHistoryAsync("USD", HistoryDto(Today.AddDays(-30)));
        var eurToday = await ControllerHistoryAsync("EUR", HistoryDto(Today));

        usd.Snapshots[0].FxReasons.Should().BeEmpty();
        eurToday.Snapshots[0].FxReasons.Should().BeEmpty();
    }

    [Fact]
    public async Task HistoryController_KeepsTheWorseReason_WithoutDuplicates()
    {
        using var _ = FxTableScope.Live(LiveEur);

        var unknown = await ControllerHistoryAsync("EUR", HistoryDto(Today.AddDays(-30), "FX_UNKNOWN_1TO1"));
        var uahUnknown = await ControllerHistoryAsync("UAH", HistoryDto(Today, "FX_UNKNOWN_1TO1"));
        var uahTodayRate = await ControllerHistoryAsync("UAH", HistoryDto(Today.AddDays(-30), "FX_TODAY_RATE"));

        unknown.Snapshots[0].FxReasons.Should().Equal("FX_UNKNOWN_1TO1");
        uahUnknown.Snapshots[0].FxReasons.Should().Equal("FX_UNKNOWN_1TO1");
        uahTodayRate.Snapshots[0].FxReasons.Should().Equal("FX_TODAY_RATE");
    }

    [Fact]
    public async Task HistoryController_AddsFxTodayRate_WhenTheBaseCurrencyIsShownOnTheSeedTable()
    {
        using var _ = FxTableScope.Live(LiveEur); // UAH is not in the live feed
        var dto = new NetWorthSnapshotDto(
            Today, 100m, 0m, 0m, 100m, "USD", null, false, FxReasons: []);

        var uah = await ControllerHistoryAsync("UAH", dto);
        var eur = await ControllerHistoryAsync("EUR", dto);

        uah.Snapshots[0].FxReasons.Should().Equal("FX_TODAY_RATE");
        eur.Snapshots[0].FxReasons.Should().BeEmpty();
    }

    [Fact]
    public async Task HistoryController_DoesNotRepeatAReasonTheRowAlreadyCarries()
    {
        using var _ = FxTableScope.Live(LiveEur);
        var dto = new NetWorthSnapshotDto(
            Today, 100m, 0m, 0m, 100m, "USD", null, true, FxReasons: ["FX_TODAY_RATE"]);

        var uah = await ControllerHistoryAsync("UAH", dto);

        uah.Snapshots[0].FxReasons.Should().Equal("FX_TODAY_RATE");
    }

    // ---- Backfill ----

    [Fact]
    public async Task Backfill_RecordsNoStoredStatus_SoHistoryReadsItFromIsApproximate()
    {
        using var _ = FxTableScope.Live(LiveEur);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var anchorDate = today.AddDays(-2);
        IReadOnlyCollection<NetWorthSnapshot>? inserted = null;
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetEarliestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthSnapshot { SnapshotDate = anchorDate });
        repo.Setup(r => r.InsertMissingAsync(It.IsAny<IReadOnlyCollection<NetWorthSnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<NetWorthSnapshot>, CancellationToken>((rows, _) => inserted = rows)
            .ReturnsAsync((IReadOnlyCollection<NetWorthSnapshot> rows, CancellationToken _) => rows.Count);

        var accountId = Guid.NewGuid();
        var accounts = new Mock<IBankingAccountsReader>();
        accounts.Setup(a => a.GetActiveAccountSnapshotsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AccountBalanceSnapshot(accountId, "Bank", "checking", "1234", "EUR", 1_000m)]);
        var transactions = new Mock<IBankingTransactionReader>();
        transactions.Setup(t => t.GetTransactionsAsync(
                UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BankingTransactionSummary(
                accountId, "monobank", "debit", 10m, "EUR", 10m,
                anchorDate.AddDays(-3).ToDateTime(TimeOnly.MinValue), IsPending: false)]);

        var result = await new BackfillNetWorthHistoryCommandHandler(repo.Object, accounts.Object, transactions.Object)
            .Handle(new BackfillNetWorthHistoryCommand(UserId), CancellationToken.None);

        result.InsertedCount.Should().BeGreaterThan(0);
        inserted.Should().OnlyContain(r => r.IsApproximate && r.FxStatus == null);
    }
}
