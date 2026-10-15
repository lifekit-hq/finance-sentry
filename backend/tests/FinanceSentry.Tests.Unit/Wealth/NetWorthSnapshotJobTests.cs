namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FluentAssertions;
using Moq;
using Xunit;

public class NetWorthSnapshotJobTests
{
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Mock<IBankingTotalsReader> BankingMock(decimal total)
    {
        var mock = new Mock<IBankingTotalsReader>();
        mock.Setup(r => r.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([UserId]);
        mock.Setup(r => r.GetTotalUsdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(total);
        return mock;
    }

    private static Mock<ICryptoHoldingsReader> CryptoMock(decimal usdValue)
    {
        var mock = new Mock<ICryptoHoldingsReader>();
        mock.Setup(r => r.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CryptoHoldingSummary("BTC", 1m, 0m, usdValue, DateTime.UtcNow, "binance")]);
        return mock;
    }

    private static Mock<IBrokerageHoldingsReader> BrokerageMock(decimal usdValue)
    {
        var mock = new Mock<IBrokerageHoldingsReader>();
        mock.Setup(r => r.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "equity", 10m, usdValue, DateTime.UtcNow, "ibkr")]);
        return mock;
    }

    private static BookSourceReading Source(string id, EconomyKind kind, decimal cash)
        => new(id, kind, new WealthSourceReading(cash, []));

    private static IBookFiguresService BookStub(
        IReadOnlyList<BookFigurePosition>? positions = null, bool isStale = false,
        decimal banking = 2000m, decimal crypto = 500m, decimal brokerage = 1000m)
    {
        var book = new BookFigures(0m, 0m, 0m, 0m, 0m, positions ?? [], isStale, isStale ? ["crypto"] : [])
        {
            Sources =
            [
                Source("banking", EconomyKind.Bank, banking),
                Source("brokerage", EconomyKind.Brokerage, brokerage),
                Source("crypto", EconomyKind.CryptoVenue, crypto),
            ],
        };
        var mock = new Mock<IBookFiguresService>();
        mock.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        return mock.Object;
    }

    [Fact]
    public async Task ExecuteAsync_SumsAllAssetClassesAndPersistsSnapshot()
    {
        NetWorthSnapshotData? captured = null;
        var snapshotServiceMock = new Mock<INetWorthSnapshotService>();
        snapshotServiceMock
            .Setup(s => s.PersistSnapshotAsync(UserId, It.IsAny<NetWorthSnapshotData>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NetWorthSnapshotData, CancellationToken>((_, data, _) => captured = data)
            .Returns(Task.CompletedTask);

        var sut = new NetWorthSnapshotJob(
            BankingMock(total: 2000m).Object,
            CryptoMock(usdValue: 500m).Object,
            BrokerageMock(usdValue: 1000m).Object,
            snapshotServiceMock.Object,
            BookStub());

        await sut.ExecuteAsync();

        captured.Should().NotBeNull();
        captured!.BankingTotal.Should().Be(2000m);
        captured.CryptoTotal.Should().Be(500m);
        captured.BrokerageTotal.Should().Be(1000m);
    }

    [Fact]
    public async Task ExecuteForUserAsync_PersistsSnapshotForSpecificUser()
    {
        Guid? capturedUserId = null;
        DateOnly? capturedSnapshotDate = null;
        var expectedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var snapshotServiceMock = new Mock<INetWorthSnapshotService>();
        snapshotServiceMock
            .Setup(s => s.PersistSnapshotAsync(It.IsAny<Guid>(), It.IsAny<NetWorthSnapshotData>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NetWorthSnapshotData, CancellationToken>((uid, data, _) =>
            {
                capturedUserId = uid;
                capturedSnapshotDate = data.SnapshotDate;
            })
            .Returns(Task.CompletedTask);

        var bankingMock = new Mock<IBankingTotalsReader>();
        bankingMock.Setup(r => r.GetTotalUsdAsync(UserId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(1500m);

        var sut = new NetWorthSnapshotJob(
            bankingMock.Object,
            CryptoMock(usdValue: 0m).Object,
            BrokerageMock(usdValue: 0m).Object,
            snapshotServiceMock.Object,
            BookStub(banking: 1500m, crypto: 0m, brokerage: 0m));

        await sut.ExecuteForUserAsync(UserId);

        capturedUserId.Should().Be(UserId);
        capturedSnapshotDate.Should().Be(expectedDate);
    }

    [Fact]
    public async Task CaptureForUserAsync_UsesProvidedSnapshotDate()
    {
        NetWorthSnapshotData? captured = null;
        var snapshotServiceMock = new Mock<INetWorthSnapshotService>();
        snapshotServiceMock
            .Setup(s => s.PersistSnapshotAsync(UserId, It.IsAny<NetWorthSnapshotData>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NetWorthSnapshotData, CancellationToken>((_, data, _) => captured = data)
            .Returns(Task.CompletedTask);

        var sut = new NetWorthSnapshotJob(
            BankingMock(total: 2000m).Object,
            CryptoMock(usdValue: 500m).Object,
            BrokerageMock(usdValue: 1000m).Object,
            snapshotServiceMock.Object,
            BookStub());

        var snapshotDate = new DateOnly(2026, 6, 29);

        await sut.CaptureForUserAsync(UserId, snapshotDate);

        captured.Should().NotBeNull();
        captured!.SnapshotDate.Should().Be(snapshotDate);
    }

    [Fact]
    public async Task CaptureForUserAsync_PassesInvestedPartOfEachSleeveFromTheBookFigures()
    {
        NetWorthSnapshotData? captured = null;
        var snapshotServiceMock = new Mock<INetWorthSnapshotService>();
        snapshotServiceMock
            .Setup(s => s.PersistSnapshotAsync(UserId, It.IsAny<NetWorthSnapshotData>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NetWorthSnapshotData, CancellationToken>((_, data, _) => captured = data)
            .Returns(Task.CompletedTask);
        var positions = new BookFigurePosition[]
        {
            new("AAPL", AssetClassNormalizer.Equities, 10m, null, 700m, "ibkr"),
            new("UA-BOND", AssetClassNormalizer.Bonds, 1m, null, 100m, "inzhur"),
            new("BTC", AssetClassNormalizer.Crypto, 1m, null, 400m, "binance"),
        };

        var sut = new NetWorthSnapshotJob(
            BankingMock(total: 2000m).Object,
            CryptoMock(usdValue: 500m).Object,
            BrokerageMock(usdValue: 1000m).Object,
            snapshotServiceMock.Object,
            BookStub(positions));

        await sut.CaptureForUserAsync(UserId, new DateOnly(2026, 10, 1));

        captured!.BrokerageInvested.Should().Be(800m, "every non-crypto position, idle broker cash excluded");
        captured.CryptoInvested.Should().Be(400m, "venue fiat is not a position");
        captured.BrokerageTotal.Should().Be(1000m, "the sleeve totals stay byte-identical");
        captured.CryptoTotal.Should().Be(500m);
    }

    [Fact]
    public async Task CaptureForUserAsync_WithAStaleBookSource_WithholdsTheSplit()
    {
        NetWorthSnapshotData? captured = null;
        var snapshotServiceMock = new Mock<INetWorthSnapshotService>();
        snapshotServiceMock
            .Setup(s => s.PersistSnapshotAsync(UserId, It.IsAny<NetWorthSnapshotData>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NetWorthSnapshotData, CancellationToken>((_, data, _) => captured = data)
            .Returns(Task.CompletedTask);

        var sut = new NetWorthSnapshotJob(
            BankingMock(total: 2000m).Object,
            CryptoMock(usdValue: 500m).Object,
            BrokerageMock(usdValue: 1000m).Object,
            snapshotServiceMock.Object,
            BookStub(isStale: true));

        await sut.CaptureForUserAsync(UserId, new DateOnly(2026, 10, 1));

        captured!.BrokerageInvested.Should().BeNull();
        captured.CryptoInvested.Should().BeNull();
        captured.BankingTotal.Should().Be(2000m);
    }

    private static Mock<INetWorthSnapshotService> CapturingService(Action<NetWorthSnapshotData> capture)
    {
        var service = new Mock<INetWorthSnapshotService>();
        service
            .Setup(s => s.PersistSnapshotAsync(UserId, It.IsAny<NetWorthSnapshotData>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NetWorthSnapshotData, CancellationToken>((_, data, _) => capture(data))
            .Returns(Task.CompletedTask);
        return service;
    }

    [Fact]
    public async Task CaptureForUserAsync_ReadsEachSourceOnce_AndTakesColumnsAndRowsFromThatRead()
    {
        NetWorthSnapshotData? captured = null;
        var banking = BankingMock(total: 9_999m);
        var crypto = CryptoMock(usdValue: 9_999m);
        var brokerage = BrokerageMock(usdValue: 9_999m);
        var fund = new BookSourceReading("fund", EconomyKind.Fund, new WealthSourceReading(0m, []));
        var book = new BookFigures(0m, 0m, 0m, 0m, 0m, [], false, [])
        {
            Sources =
            [
                Source("banking", EconomyKind.Bank, 2000m),
                Source("brokerage", EconomyKind.Brokerage, 1000m),
                Source("crypto", EconomyKind.CryptoVenue, 500m),
                fund,
            ],
        };
        var bookService = new Mock<IBookFiguresService>();
        bookService.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(book);

        var sut = new NetWorthSnapshotJob(
            banking.Object, crypto.Object, brokerage.Object, CapturingService(d => captured = d).Object, bookService.Object);

        await sut.CaptureForUserAsync(UserId, new DateOnly(2026, 10, 1));

        bookService.Verify(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        captured!.BankingTotal.Should().Be(2000m, "the column is the source's reading, not a second read of the provider");
        captured.BrokerageTotal.Should().Be(1000m);
        captured.CryptoTotal.Should().Be(500m);
        captured.Sources.Should().BeSameAs(book.Sources, "the rows are the readings the columns were taken from");
    }

    [Fact]
    public async Task CaptureForUserAsync_WithAFailedSource_CarriesItsSleeveForward_NotAsZero()
    {
        NetWorthSnapshotData? captured = null;
        var book = new BookFigures(0m, 0m, 0m, 0m, 0m, [], true, ["crypto"])
        {
            Sources =
            [
                Source("banking", EconomyKind.Bank, 2000m),
                Source("brokerage", EconomyKind.Brokerage, 1000m),
                new BookSourceReading("crypto", EconomyKind.CryptoVenue, null),
            ],
        };
        var bookService = new Mock<IBookFiguresService>();
        bookService.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(book);

        var sut = new NetWorthSnapshotJob(
            BankingMock(total: 2000m).Object,
            CryptoMock(usdValue: 500m).Object,
            BrokerageMock(usdValue: 1000m).Object,
            CapturingService(d => captured = d).Object,
            bookService.Object);

        await sut.CaptureForUserAsync(UserId, new DateOnly(2026, 10, 1));

        captured!.CryptoFresh.Should().BeFalse("a source that did not read has no live figure to record");
        captured.BrokerageFresh.Should().BeTrue();
        captured.Sources!.Single(r => r.SourceId == "crypto").Reading.Should().BeNull();
    }
}
