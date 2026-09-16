using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Services;
using FinanceSentry.Core.Domain;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

/// <summary>
/// The sync handler over a real <see cref="CryptoSyncDbContext"/> and real repositories: the
/// guarantee under test is that one venue's sync never reads, overwrites or deletes another
/// venue's rows (#472).
/// </summary>
public sealed class SyncExchangeHoldingsCommandTests : IDisposable
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly CryptoSyncDbContext _db = new(
        new DbContextOptionsBuilder<CryptoSyncDbContext>()
            .UseInMemoryDatabase($"crypto-sync-{Guid.NewGuid()}")
            .Options);

    private readonly Mock<ICryptoExchangeAdapter> _binance = new(MockBehavior.Loose);
    private readonly Mock<ICryptoExchangeAdapter> _revolutX = new(MockBehavior.Loose);
    private readonly Mock<ICredentialEncryptionService> _encryption = new(MockBehavior.Loose);

    public SyncExchangeHoldingsCommandTests()
    {
        _binance.SetupGet(a => a.ExchangeName).Returns(CryptoExchangeProvider.Binance);
        _revolutX.SetupGet(a => a.ExchangeName).Returns(CryptoExchangeProvider.RevolutX);
        _encryption
            .Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Returns<byte[], byte[], byte[], int>((ciphertext, _, _, _) => ciphertext[0] == 1 ? "api-key" : "api-secret");
        foreach (var adapter in new[] { _binance, _revolutX })
        {
            adapter
                .Setup(a => a.GetTradesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, string, string?, CancellationToken>((_, _, _, cursor, _) =>
                    Task.FromResult(new CryptoTradePage([], cursor)));
        }
    }

    public void Dispose() => _db.Dispose();

    private SyncExchangeHoldingsCommandHandler CreateHandler() =>
        new(
            new ExchangeCredentialRepository(_db),
            new CryptoHoldingRepository(_db),
            new CryptoExchangeAdapterRegistry([_binance.Object, _revolutX.Object]),
            _encryption.Object,
            new CostBasisCalculator(),
            NullLogger<SyncExchangeHoldingsCommandHandler>.Instance);

    private async Task<ExchangeCredential> ConnectAsync(string provider)
    {
        var credential = ExchangeCredential.Create(_userId, provider, [1], [0], [0], [2], [0], [0], 1);
        _db.ExchangeCredentials.Add(credential);
        await _db.SaveChangesAsync();
        return credential;
    }

    private void Holdings(Mock<ICryptoExchangeAdapter> adapter, params CryptoAssetBalance[] balances) =>
        adapter
            .Setup(a => a.GetHoldingsAsync("api-key", "api-secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(balances);

    private Task<SyncExchangeHoldingsResult> SyncAsync(string provider) =>
        CreateHandler().Handle(new SyncExchangeHoldingsCommand(_userId, provider), default);

    [Fact]
    public async Task SameAssetOnTwoVenues_IsTwoRows_AndNeitherSyncOverwritesTheOther()
    {
        await ConnectAsync(CryptoExchangeProvider.Binance);
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_binance, new CryptoAssetBalance("BTC", 0.5m, 0m, 30_000m));
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.1m, 0.05m, 9_000m), new CryptoAssetBalance("ETH", 2m, 0m, 6_000m));

        await SyncAsync(CryptoExchangeProvider.Binance);
        await SyncAsync(CryptoExchangeProvider.RevolutX);
        await SyncAsync(CryptoExchangeProvider.Binance);

        var rows = await _db.CryptoHoldings.AsNoTracking().OrderBy(h => h.Provider).ThenBy(h => h.Asset).ToListAsync();
        rows.Select(h => (h.Provider, h.Asset, h.FreeQuantity + h.LockedQuantity, h.UsdValue)).Should().Equal(
            (CryptoExchangeProvider.Binance, "BTC", 0.5m, 30_000m),
            (CryptoExchangeProvider.RevolutX, "BTC", 0.15m, 9_000m),
            (CryptoExchangeProvider.RevolutX, "ETH", 2m, 6_000m));
    }

    [Fact]
    public async Task SoldOutOnOneVenue_RemovesOnlyThatVenuesRow()
    {
        await ConnectAsync(CryptoExchangeProvider.Binance);
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_binance, new CryptoAssetBalance("BTC", 0.5m, 0m, 30_000m));
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.1m, 0m, 6_000m));
        await SyncAsync(CryptoExchangeProvider.Binance);
        await SyncAsync(CryptoExchangeProvider.RevolutX);

        Holdings(_revolutX);
        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var rows = await _db.CryptoHoldings.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle().Which.Provider.Should().Be(CryptoExchangeProvider.Binance);
    }

    [Fact]
    public async Task RevolutXHoldings_AreTaggedRevolutX_AndLeaveCostBasisNull()
    {
        var credential = await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX, new CryptoAssetBalance("SOL", 10m, 0m, 1_500m));

        var result = await SyncAsync(CryptoExchangeProvider.RevolutX);

        result.HoldingsCount.Should().Be(1);
        var row = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        row.Provider.Should().Be(CryptoExchangeProvider.RevolutX);
        row.CostBasisUsd.Should().BeNull("cost basis is never guessed for lots the venue cannot give history for");
        row.AverageBuyPriceUsd.Should().BeNull();
        credential.LastSyncAt.Should().NotBeNull();
        credential.LastSyncError.Should().BeNull();
    }

    [Fact]
    public async Task VenueFailure_IsRecordedOnThatVenuesCredential_AndRethrown()
    {
        var binance = await ConnectAsync(CryptoExchangeProvider.Binance);
        var revolutX = await ConnectAsync(CryptoExchangeProvider.RevolutX);
        _revolutX
            .Setup(a => a.GetHoldingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RevolutXException("Revolut X API error (HTTP 401): API key can only be used from whitelisted IP"));

        var act = () => SyncAsync(CryptoExchangeProvider.RevolutX);

        await act.Should().ThrowAsync<RevolutXException>();
        revolutX.LastSyncError.Should().Contain("whitelisted IP");
        binance.LastSyncError.Should().BeNull();
    }

    [Fact]
    public async Task DisconnectedVenue_IsNotSynced()
    {
        var credential = await ConnectAsync(CryptoExchangeProvider.RevolutX);
        credential.Deactivate();
        await _db.SaveChangesAsync();

        var act = () => SyncAsync(CryptoExchangeProvider.RevolutX);

        await act.Should().ThrowAsync<ExchangeAccountNotFoundException>();
        _revolutX.Verify(a => a.GetHoldingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TradeCursor_IsHandedToTheAdapter_AndAdvancedWithTheTradesItCovers()
    {
        await ConnectAsync(CryptoExchangeProvider.Binance);
        Holdings(_binance, new CryptoAssetBalance("BTC", 1m, 0m, 60_000m));
        await SyncAsync(CryptoExchangeProvider.Binance);
        var holding = await _db.CryptoHoldings.SingleAsync();
        holding.AdvanceTradeCursor("42");
        await _db.SaveChangesAsync();

        _binance
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "BTC", "42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CryptoTradePage(
                [new CryptoTrade("42", "BTC", "USDT", 1m, 50_000m, 50_000m, IsBuyer: true, DateTime.UtcNow)],
                "USDT=43"));

        await SyncAsync(CryptoExchangeProvider.Binance);

        var after = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        after.TradeCursor.Should().Be("USDT=43");
        after.TradeCount.Should().Be(1);
        after.CostBasisUsd.Should().Be(50_000m);
    }

    [Fact]
    public async Task RevolutXHoldings_FlowIntoBookFiguresAsCrypto_NeverAsBankingCash()
    {
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.1m, 0m, 6_000m));
        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var banking = new Mock<IBankingAccountsReader>();
        banking
            .Setup(b => b.GetAccountSummariesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage
            .Setup(b => b.GetHoldingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var book = await new BookFiguresService(
            banking.Object,
            brokerage.Object,
            new CryptoHoldingsReader(new CryptoHoldingRepository(_db)),
            NullLogger<BookFiguresService>.Instance).ReadAsync(_userId);

        book.BankingCashUsd.Should().Be(0m);
        book.CashUsd.Should().Be(0m);
        book.Positions.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Symbol = "BTC",
            AssetClass = AssetClassNormalizer.Crypto,
            UsdValue = 6_000m,
            Provider = CryptoExchangeProvider.RevolutX,
        });
    }
}
