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
        FxStatus brokerageStatus = FxStatus.Exact)
    {
        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([UserId]);
        banking.Setup(b => b.GetTotalUsdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(1_000m);
        var crypto = new Mock<ICryptoHoldingsReader>();
        crypto.Setup(c => c.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new CryptoHoldingSummary("BTC", 1m, 0m, 500m, DateTime.UtcNow, "binance", FxStatus: cryptoStatus)]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new BrokerageHoldingSummary("AAPL", "equity", 1m, 700m, DateTime.UtcNow, "ibkr", FxStatus: brokerageStatus)]);
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

    // ---- Net-worth history ----

    private static NetWorthSnapshot Row(bool approximate) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        SnapshotDate = new DateOnly(2026, 10, 1),
        BankingTotal = 100m,
        TotalNetWorth = 100m,
        Currency = "USD",
        IsApproximate = approximate,
    };

    [Fact]
    public async Task History_ReportsFxTodayRate_ForABackfilledRow_AndNothingForAMeasuredOne()
    {
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row(approximate: true), Row(approximate: false)]);

        var result = await new GetNetWorthHistoryQueryHandler(repo.Object)
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        result.Snapshots[0].FxReasons.Should().Equal("FX_TODAY_RATE");
        result.Snapshots[1].FxReasons.Should().BeEmpty();
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

    [Fact]
    public async Task HistoryController_AddsFxTodayRate_WhenTheBaseCurrencyIsShownOnTheSeedTable()
    {
        using var _ = FxTableScope.Live(LiveEur); // UAH is not in the live feed
        var dto = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 1), 100m, 0m, 0m, 100m, "USD", null, false, FxReasons: []);

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
            new DateOnly(2026, 10, 1), 100m, 0m, 0m, 100m, "USD", null, true, FxReasons: ["FX_TODAY_RATE"]);

        var uah = await ControllerHistoryAsync("UAH", dto);

        uah.Snapshots[0].FxReasons.Should().Equal("FX_TODAY_RATE");
    }

    // ---- Backfill ----

    private static async Task<BackfillNetWorthHistoryResult> BackfillAsync(string currency)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var anchorDate = today.AddDays(-2);
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetEarliestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthSnapshot { SnapshotDate = anchorDate });
        repo.Setup(r => r.InsertMissingAsync(It.IsAny<IReadOnlyCollection<NetWorthSnapshot>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<NetWorthSnapshot> rows, CancellationToken _) => rows.Count);

        var accountId = Guid.NewGuid();
        var accounts = new Mock<IBankingAccountsReader>();
        accounts.Setup(a => a.GetActiveAccountSnapshotsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AccountBalanceSnapshot(accountId, "Bank", "checking", "1234", currency, 1_000m)]);
        var transactions = new Mock<IBankingTransactionReader>();
        transactions.Setup(t => t.GetTransactionsAsync(
                UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BankingTransactionSummary(
                accountId, "monobank", "debit", 10m, currency, 10m,
                anchorDate.AddDays(-3).ToDateTime(TimeOnly.MinValue), IsPending: false)]);

        return await new BackfillNetWorthHistoryCommandHandler(repo.Object, accounts.Object, transactions.Object)
            .Handle(new BackfillNetWorthHistoryCommand(UserId), CancellationToken.None);
    }

    [Theory]
    [InlineData("USD", FxStatus.TodayRate)] // reconstructed days are valued at today's rate, whatever the currency
    [InlineData("EUR", FxStatus.TodayRate)]
    [InlineData("UAH", FxStatus.SeedTable)]
    [InlineData("XYZ", FxStatus.Unknown1To1)]
    public async Task Backfill_NeverReportsBetterThanTodayRate_AndTheWorstOfTheCurrenciesItConverted(
        string currency, FxStatus expected)
    {
        using var _ = FxTableScope.Live(LiveEur);

        var result = await BackfillAsync(currency);

        result.InsertedCount.Should().BeGreaterThan(0);
        result.FxStatus.Should().Be(expected);
    }
}
