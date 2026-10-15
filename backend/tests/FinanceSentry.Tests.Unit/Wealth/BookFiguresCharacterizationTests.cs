namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Integration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

/// <summary>
/// Pins the book figures built from a fixed, fictional input through the production registration
/// (<c>AddCrossModulePorts</c>), so the figures are the same before and after the three readers became
/// wealth sources (R8, PR 1): totals, per-sleeve values, positions and their order, FX status, and the
/// stale-source names.
/// </summary>
public sealed class BookFiguresCharacterizationTests
{
    private static readonly Guid UserId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly BankingAccountSummary[] BankAccounts =
    [
        Account("checking", 1_000m),
        Account("savings", 540m),
        Account("credit", 200m),
        Account("checking", balanceUsd: null, FxStatus.Unknown1To1),
    ];

    private static readonly BrokerageHoldingSummary[] BrokerageHoldings =
    [
        Brokerage("AAPL", "Stock", 10m, 1_800m, costBasisUsd: 1_500m),
        Brokerage("VWRA", "ETF", 5m, 600m),
        Brokerage("USD", "CASH", 250m, 250m),
        Brokerage("OLD", "Stock", 0m, 0m, fxStatus: FxStatus.Unknown1To1),
    ];

    private static readonly CryptoHoldingSummary[] CryptoHoldings =
    [
        new("BTC", 0.1m, 0.05m, 9_000m, DateTime.UnixEpoch, "binance", CostBasisUsd: 6_000m),
        new("EUR", 400m, 0m, 432m, DateTime.UnixEpoch, "revolut-x", IsVenueFiat: true, FxStatus: FxStatus.TodayRate),
        new("SOL", 0m, 0m, 0m, DateTime.UnixEpoch, "binance", FxStatus: FxStatus.Unknown1To1),
    ];

    [Fact]
    public async Task ReadAsync_PinsTotalsSleevesPositionsAndFxStatus()
    {
        var book = await ReadAsync(new Mock<IBankingAccountsReader>(), new Mock<IBrokerageHoldingsReader>(), new Mock<ICryptoHoldingsReader>());

        book.BankingCashUsd.Should().Be(1_340m);
        book.BrokerageCashUsd.Should().Be(250m);
        book.VenueCashUsd.Should().Be(432m);
        book.CashUsd.Should().Be(2_022m);
        book.InvestedValueUsd.Should().Be(11_400m);
        book.TotalValueUsd.Should().Be(13_422m);
        book.FxStatus.Should().Be(FxStatus.TodayRate);
        book.FxReasonCodes.Should().Equal(FxReasons.TodayRate);
        book.IsStale.Should().BeFalse();
        book.StaleSources.Should().BeEmpty();

        // Crypto first, then brokerage, as the figures have always been built.
        book.Positions.Should().Equal(
            new BookFigurePosition("BTC", AssetClassNormalizer.Crypto, 0.15m, 6_000m, 9_000m, "binance"),
            new BookFigurePosition("AAPL", AssetClassNormalizer.Normalize("Stock"), 10m, 1_500m, 1_800m, "ibkr"),
            new BookFigurePosition("VWRA", AssetClassNormalizer.Normalize("ETF"), 5m, null, 600m, "ibkr"));
    }

    [Fact]
    public async Task ReadAsync_ASourceThatFails_ContributesNothingAndIsNamed()
    {
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));

        var book = await ReadAsync(new Mock<IBankingAccountsReader>(), brokerage, new Mock<ICryptoHoldingsReader>());

        book.BrokerageCashUsd.Should().Be(0m);
        book.BankingCashUsd.Should().Be(1_340m);
        book.VenueCashUsd.Should().Be(432m);
        book.CashUsd.Should().Be(1_772m);
        book.InvestedValueUsd.Should().Be(9_000m);
        book.TotalValueUsd.Should().Be(10_772m);
        book.Positions.Select(p => p.Symbol).Should().Equal("BTC");
        book.IsStale.Should().BeTrue();
        book.StaleSources.Should().Equal("brokerage");
    }

    [Fact]
    public async Task ReadAsync_EverySourceFailing_NamesThemCryptoBrokerageBanking_AndIsEmpty()
    {
        var banking = new Mock<IBankingAccountsReader>();
        banking.Setup(b => b.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));
        var crypto = new Mock<ICryptoHoldingsReader>();
        crypto.Setup(c => c.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));

        var book = await ReadAsync(banking, brokerage, crypto);

        book.TotalValueUsd.Should().Be(0m);
        book.Positions.Should().BeEmpty();
        book.FxStatus.Should().Be(FxStatus.Exact);
        book.IsStale.Should().BeTrue();
        book.StaleSources.Should().Equal("crypto", "brokerage", "banking");
    }

    private static async Task<BookFigures> ReadAsync(
        Mock<IBankingAccountsReader> banking, Mock<IBrokerageHoldingsReader> brokerage, Mock<ICryptoHoldingsReader> crypto)
    {
        // A setup made by the test (a failure) wins; a reader left alone answers with the fixed input.
        banking.SetReturnsDefault(Task.FromResult<IReadOnlyList<BankingAccountSummary>>(BankAccounts));
        brokerage.SetReturnsDefault(Task.FromResult<IReadOnlyList<BrokerageHoldingSummary>>(BrokerageHoldings));
        crypto.SetReturnsDefault(Task.FromResult<IReadOnlyList<CryptoHoldingSummary>>(CryptoHoldings));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => banking.Object);
        services.AddScoped(_ => brokerage.Object);
        services.AddScoped(_ => crypto.Object);
        services.AddCrossModulePorts();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBookFiguresService>().ReadAsync(UserId);
    }

    private static BankingAccountSummary Account(string type, decimal balanceUsd) =>
        Account(type, (decimal?)balanceUsd, FxStatus.Exact);

    private static BankingAccountSummary Account(string type, decimal? balanceUsd, FxStatus fxStatus) =>
        new(Guid.NewGuid(), "Bank", type, "1234", "monobank", "USD", balanceUsd, balanceUsd, "ok", null, FxStatus: fxStatus);

    private static BrokerageHoldingSummary Brokerage(
        string symbol, string type, decimal qty, decimal usd, decimal? costBasisUsd = null, FxStatus fxStatus = FxStatus.Exact) =>
        new(symbol, type, qty, usd, DateTime.UnixEpoch, "ibkr", costBasisUsd, FxStatus: fxStatus);
}
