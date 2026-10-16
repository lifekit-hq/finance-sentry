using FinanceSentry.Tests.Unit.BankSync;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Services;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Unit.CryptoSync.RevolutX;
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
    private static readonly Guid SharedUserId = Guid.NewGuid();
    private readonly Guid _userId = SharedUserId;
    private readonly CryptoSyncDbContext _db = new(
        new DbContextOptionsBuilder<CryptoSyncDbContext>()
            .UseInMemoryDatabase($"crypto-sync-{Guid.NewGuid()}")
            .Options, new FixedCurrentUser(SharedUserId));

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
                .Setup(a => a.GetTradesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, string, string?, CryptoTradeWalk, CancellationToken>((_, _, _, cursor, _, _) =>
                    Task.FromResult(new CryptoTradePage([], cursor)));
            adapter
                .Setup(a => a.GetFlowsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CryptoFlowWalk>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, CryptoFlowWalk, CancellationToken>((_, _, walk, _) =>
                    Task.FromResult(new CryptoFlowPage([], walk.AsOf)));
        }

        _revolutX.SetupGet(a => a.TradeHistoryStartsAtConnect).Returns(true);
    }

    public void Dispose() => _db.Dispose();

    private SyncExchangeHoldingsCommandHandler CreateHandler() =>
        new(
            new ExchangeCredentialRepository(_db),
            new InMemoryUpsertHoldings(_db),
            new CryptoTradeRepository(_db),
            new CryptoFlowEventRepository(_db),
            new CryptoExchangeAdapterRegistry([_binance.Object, _revolutX.Object]),
            _encryption.Object,
            new CostBasisCalculator(),
            new ForwardCostBasisLedger(),
            new FixedTimeProvider(Now),
            NullLogger<SyncExchangeHoldingsCommandHandler>.Instance);

    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

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
    public async Task SoldOutOnOneVenue_ClosesOnlyThatVenuesRow()
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
        rows.Should().ContainSingle(h => !h.IsClosed).Which.Provider.Should().Be(CryptoExchangeProvider.Binance);
        rows.Should().ContainSingle(h => h.IsClosed).Which.Provider.Should().Be(CryptoExchangeProvider.RevolutX);
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
        row.UntrackedQuantity.Should().Be(10m, "the whole position predates connect");
        row.TrackedQuantity.Should().Be(0m);
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

    private static CryptoFlow ReceiveFromRevolut(string id) =>
        new(id, CryptoFlowTypes.Receive, Now.UtcDateTime.AddDays(-1), 500m, "EUR", true, null, null, null, "revolut", "completed");

    [Fact]
    public async Task FlowWalk_StartsAtConnect_RecordsTheMovements_AndAdvancesTheCursor()
    {
        var credential = await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX);
        CryptoFlowWalk? seen = null;
        var walkedTo = Now.UtcDateTime.AddMinutes(-5);
        _revolutX
            .Setup(a => a.GetFlowsAsync("api-key", "api-secret", It.IsAny<CryptoFlowWalk>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CryptoFlowWalk, CancellationToken>((_, _, walk, _) => seen = walk)
            .ReturnsAsync(new CryptoFlowPage([ReceiveFromRevolut("tx-1")], walkedTo));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        seen.Should().Be(new CryptoFlowWalk(credential.FirstLinkedAt, Now.UtcDateTime));
        credential.FlowsWalkedTo.Should().Be(walkedTo);
        var row = (await _db.CryptoFlowEvents.AsNoTracking().ToListAsync()).Should().ContainSingle().Subject;
        (row.UserId, row.Provider, row.EventId, row.ProviderType, row.Amount, row.Unit, row.CounterpartyRef)
            .Should().Be((_userId, CryptoExchangeProvider.RevolutX, "tx-1", CryptoFlowTypes.Receive, 500m, "EUR", "revolut"));
        row.RecordedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task FlowWalk_WithinAnHourOfTheCursor_IsNotRepeated()
    {
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX);

        await SyncAsync(CryptoExchangeProvider.RevolutX);
        await SyncAsync(CryptoExchangeProvider.RevolutX);

        _revolutX.Verify(
            a => a.GetFlowsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CryptoFlowWalk>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FlowWalk_ResumesAfterTheCursor_AndRewalkedMovementsAreNotDuplicated()
    {
        var credential = await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX);
        var walkedTo = Now.UtcDateTime.AddHours(-2);
        var walks = new List<CryptoFlowWalk>();
        _revolutX
            .Setup(a => a.GetFlowsAsync("api-key", "api-secret", It.IsAny<CryptoFlowWalk>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CryptoFlowWalk, CancellationToken>((_, _, walk, _) => walks.Add(walk))
            .ReturnsAsync(new CryptoFlowPage([ReceiveFromRevolut("tx-1")], walkedTo));

        await SyncAsync(CryptoExchangeProvider.RevolutX);
        await SyncAsync(CryptoExchangeProvider.RevolutX);

        walks.Select(w => w.From).Should().Equal(credential.FirstLinkedAt, walkedTo.AddMilliseconds(1));
        (await _db.CryptoFlowEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FlowWalk_HandsTheVenuesLaneBack_OnTheNextRun()
    {
        await ConnectAsync(CryptoExchangeProvider.Binance);
        Holdings(_binance);
        var walkedTo = Now.UtcDateTime.AddHours(-2);
        var walks = new List<CryptoFlowWalk>();
        _binance
            .Setup(a => a.GetFlowsAsync("api-key", "api-secret", It.IsAny<CryptoFlowWalk>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CryptoFlowWalk, CancellationToken>((_, _, walk, _) => walks.Add(walk))
            .ReturnsAsync(new CryptoFlowPage([], walkedTo, NextLane: 2));

        await SyncAsync(CryptoExchangeProvider.Binance);
        await SyncAsync(CryptoExchangeProvider.Binance);

        walks.Select(w => w.Lane).Should().Equal(0, 2);
    }

    [Fact]
    public async Task FlowWalk_AfterAReconnect_RestartsFromTheFirstLinkDay()
    {
        var credential = await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX);
        var firstLinkedAt = credential.FirstLinkedAt;
        var walks = new List<CryptoFlowWalk>();
        _revolutX
            .Setup(a => a.GetFlowsAsync("api-key", "api-secret", It.IsAny<CryptoFlowWalk>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CryptoFlowWalk, CancellationToken>((_, _, walk, _) => walks.Add(walk))
            .ReturnsAsync(new CryptoFlowPage([], Now.UtcDateTime.AddHours(-2)));
        await SyncAsync(CryptoExchangeProvider.RevolutX);

        credential.Deactivate();
        credential.Reconnect([1], [0], [0], [2], [0], [0], 1);
        await _db.SaveChangesAsync();
        await SyncAsync(CryptoExchangeProvider.RevolutX);

        credential.FirstLinkedAt.Should().Be(firstLinkedAt);
        walks.Select(w => w.From).Should().Equal(firstLinkedAt, firstLinkedAt);
    }

    [Fact]
    public async Task FlowWalkRefused_KeepsTheCursor_AndTheHoldingsSyncSucceeds()
    {
        var credential = await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.1m, 0m, 6_000m));
        _revolutX
            .Setup(a => a.GetFlowsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CryptoFlowWalk>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RevolutXException("Revolut X API error (HTTP 403): forbidden"));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        credential.FlowsWalkedTo.Should().BeNull();
        credential.LastSyncError.Should().BeNull();
        (await _db.CryptoHoldings.CountAsync()).Should().Be(1);
        (await _db.CryptoFlowEvents.CountAsync()).Should().Be(0);
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
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "BTC", "42", It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
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
    public async Task WalkedFills_ArePersisted_ToTheCryptoTradesTable()
    {
        await ConnectAsync(CryptoExchangeProvider.Binance);
        Holdings(_binance, new CryptoAssetBalance("BTC", 1m, 0m, 60_000m));
        _binance
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "BTC", null, It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CryptoTradePage(
                [new CryptoTrade("42", "BTC", "USDT", 1m, 50_000m, 50_000m, IsBuyer: true, Now.UtcDateTime)],
                "USDT=43"));

        await SyncAsync(CryptoExchangeProvider.Binance);

        var row = await _db.CryptoTrades.AsNoTracking().SingleAsync();
        row.UserId.Should().Be(_userId);
        row.Provider.Should().Be(CryptoExchangeProvider.Binance);
        row.TradeId.Should().Be("42");
        row.Asset.Should().Be("BTC");
        row.QuoteAsset.Should().Be("USDT");
        row.Quantity.Should().Be(1m);
        row.PriceUsd.Should().Be(50_000m);
        row.QuoteQuantityUsd.Should().Be(50_000m);
        row.IsBuyer.Should().BeTrue();
        row.Timestamp.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task ReWalkingTheSamePage_AddsNothing()
    {
        await ConnectAsync(CryptoExchangeProvider.Binance);
        Holdings(_binance, new CryptoAssetBalance("BTC", 1m, 0m, 60_000m));
        var page = new CryptoTradePage(
            [new CryptoTrade("42", "BTC", "USDT", 1m, 50_000m, 50_000m, IsBuyer: true, Now.UtcDateTime)],
            "USDT=43");
        _binance
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "BTC", It.IsAny<string?>(), It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        await SyncAsync(CryptoExchangeProvider.Binance);
        await SyncAsync(CryptoExchangeProvider.Binance);

        var rows = await _db.CryptoTrades.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle().Which.TradeId.Should().Be("42");
    }

    [Fact]
    public async Task ClosedPosition_KeepsItsPersistedFills()
    {
        await ConnectAsync(CryptoExchangeProvider.Binance);
        Holdings(_binance, new CryptoAssetBalance("BTC", 1m, 0m, 60_000m));
        _binance
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "BTC", null, It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CryptoTradePage(
                [new CryptoTrade("42", "BTC", "USDT", 1m, 50_000m, 50_000m, IsBuyer: true, Now.UtcDateTime)],
                "USDT=43"));
        await SyncAsync(CryptoExchangeProvider.Binance);

        // Position fully sold out on the venue: the holding row is soft-closed next sync.
        Holdings(_binance);
        await SyncAsync(CryptoExchangeProvider.Binance);

        (await _db.CryptoHoldings.AsNoTracking().SingleAsync()).IsClosed.Should().BeTrue();
        var row = await _db.CryptoTrades.AsNoTracking().SingleAsync();
        row.TradeId.Should().Be("42", "a persisted fill outlives the holding closing");
    }

    private static CryptoTradePage BuyPage(string id, string cursor) =>
        new([new CryptoTrade(id, "BTC", "USDT", 1m, 50_000m, 50_000m, IsBuyer: true, Now.UtcDateTime)], cursor);

    [Fact]
    public async Task ClosedPosition_KeepsRowCursorAndRealizedTotal_AndReadsExcludeIt()
    {
        await ConnectAsync(CryptoExchangeProvider.Binance);
        Holdings(_binance, new CryptoAssetBalance("BTC", 1m, 0m, 60_000m));
        _binance
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "BTC", null, It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuyPage("1", "USDT=2"));
        await SyncAsync(CryptoExchangeProvider.Binance);
        _binance
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "BTC", "USDT=2", It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CryptoTradePage(
                [new CryptoTrade("2", "BTC", "USDT", 1m, 70_000m, 70_000m, IsBuyer: false, Now.UtcDateTime)],
                "USDT=3"));

        Holdings(_binance);
        await SyncAsync(CryptoExchangeProvider.Binance);

        var row = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        row.IsClosed.Should().BeTrue();
        row.FreeQuantity.Should().Be(0m);
        row.UsdValue.Should().Be(0m);
        row.TradeCursor.Should().Be("USDT=3");
        row.RealizedPnlUsd.Should().Be(20_000m);

        var repo = new CryptoHoldingRepository(_db);
        (await repo.GetByUserIdAsync(_userId)).Should().BeEmpty();
        (await repo.GetByUserAndProviderUnscopedAsync(_userId, CryptoExchangeProvider.Binance)).Should().BeEmpty();
        (await repo.GetAllByUserAndProviderAsync(_userId, CryptoExchangeProvider.Binance)).Should().ContainSingle();
    }

    [Fact]
    public async Task ReappearingPosition_ReopensTheSameRow_AndContinuesItsCursor()
    {
        await ConnectAsync(CryptoExchangeProvider.Binance);
        Holdings(_binance, new CryptoAssetBalance("BTC", 1m, 0m, 60_000m));
        _binance
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "BTC", null, It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuyPage("1", "USDT=2"));
        await SyncAsync(CryptoExchangeProvider.Binance);
        var id = (await _db.CryptoHoldings.AsNoTracking().SingleAsync()).Id;
        Holdings(_binance);
        await SyncAsync(CryptoExchangeProvider.Binance);

        Holdings(_binance, new CryptoAssetBalance("BTC", 2m, 0m, 120_000m));
        _binance
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "BTC", "USDT=2", It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuyPage("3", "USDT=4"));
        await SyncAsync(CryptoExchangeProvider.Binance);

        var row = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        row.Id.Should().Be(id);
        row.IsClosed.Should().BeFalse();
        row.FreeQuantity.Should().Be(2m);
        row.TradeCursor.Should().Be("USDT=4");
        (await new CryptoHoldingRepository(_db).GetByUserIdAsync(_userId)).Should().ContainSingle();
        (await _db.CryptoTrades.AsNoTracking().Select(t => t.TradeId).OrderBy(t => t).ToListAsync())
            .Should().Equal("1", "3");
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
            [
                new CryptoWealthSource(new CryptoHoldingsReader(new CryptoHoldingRepository(_db))),
                new BrokerageWealthSource(brokerage.Object),
                new BankingWealthSource(banking.Object),
            ],
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

    private static CryptoTrade Fill(string id, decimal quantity, decimal priceUsd, bool buy, int minutesAgo) =>
        new(id, "BTC", "USD", quantity, priceUsd, quantity * priceUsd, buy, Now.UtcDateTime.AddMinutes(-minutesAgo));

    private void Trades(string asset, string? cursor, CryptoTradePage page) =>
        _revolutX
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", asset, cursor, It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

    [Fact]
    public async Task RevolutX_WalkStartsAtConnect_AndStopsWhereTheBalanceSnapshotWasTaken()
    {
        var credential = await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.1m, 0m, 6_000m));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        _revolutX.Verify(a => a.GetTradesAsync(
            "api-key", "api-secret", "BTC", null,
            new CryptoTradeWalk(credential.CreatedAt, Now.UtcDateTime),
            It.IsAny<CancellationToken>()), Times.Once);
        _binance.Verify(a => a.GetTradesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevolutX_AssetBoughtAfterConnect_AccumulatesCostBasisAcrossRuns()
    {
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.3m, 0m, 18_000m));
        Trades("BTC", null, new CryptoTradePage(
            [Fill("t1", 0.1m, 50_000m, buy: true, 30), Fill("t2", 0.2m, 56_000m, buy: true, 20)],
            "v1:1"));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var first = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        first.CostBasisUsd.Should().Be(16_200m);
        first.AverageBuyPriceUsd.Should().Be(54_000m);
        first.TradeCount.Should().Be(2);
        first.TradeCursor.Should().Be("v1:1");

        // Next run: half sold at 60k — realized against the known average.
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.15m, 0m, 9_000m));
        Trades("BTC", "v1:1", new CryptoTradePage([Fill("t3", 0.15m, 60_000m, buy: false, 5)], "v1:2"));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var second = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        second.CostBasisUsd.Should().Be(8_100m);
        second.AverageBuyPriceUsd.Should().Be(54_000m);
        second.RealizedPnlUsd.Should().Be(900m);
        second.TradeCount.Should().Be(3);
        second.TradeCursor.Should().Be("v1:2");
    }

    [Fact]
    public async Task RevolutX_PositionHeldBeforeConnect_KeepsCostBasisNull_EvenAfterNewBuys()
    {
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.6m, 0m, 36_000m));
        Trades("BTC", null, new CryptoTradePage([Fill("t1", 0.1m, 50_000m, buy: true, 10)], "v1:1"));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var row = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        row.CostBasisUsd.Should().BeNull();
        row.AverageBuyPriceUsd.Should().BeNull();
        row.UntrackedQuantity.Should().Be(0.5m);
        row.TrackedQuantity.Should().Be(0.1m);
        row.TradeCount.Should().Be(1);
    }

    [Fact]
    public async Task RevolutX_TradeWalkFailure_KeepsTheRest_LeavesTheCursor_AndFailsTheSync()
    {
        var credential = await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX,
            new CryptoAssetBalance("BTC", 0.1m, 0m, 6_000m),
            new CryptoAssetBalance("ETH", 1m, 0m, 3_000m));
        Trades("BTC", null, new CryptoTradePage([Fill("t1", 0.1m, 60_000m, buy: true, 10)], "v1:1"));
        _revolutX
            .Setup(a => a.GetTradesAsync("api-key", "api-secret", "ETH", null, It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RevolutXException("Revolut X API error (HTTP 429): too many requests"));

        var act = () => SyncAsync(CryptoExchangeProvider.RevolutX);

        (await act.Should().ThrowAsync<CryptoTradeHistoryException>())
            .Which.Message.Should().Contain("ETH").And.Contain("HTTP 429");
        credential.LastSyncError.Should().Contain("ETH");

        var rows = await _db.CryptoHoldings.AsNoTracking().ToDictionaryAsync(h => h.Asset);
        rows.Keys.Should().BeEquivalentTo(["BTC", "ETH"], "the holdings snapshot still lands");
        rows["BTC"].CostBasisUsd.Should().Be(6_000m);
        rows["BTC"].TradeCursor.Should().Be("v1:1");
        rows["ETH"].TradeCursor.Should().BeNull("the failed walk resumes from where it was");
        rows["ETH"].TrackedQuantity.Should().BeNull();
    }

    [Fact]
    public async Task RevolutX_IncompleteWalk_AppliesFills_ButLeavesTransfersForTheWalkThatCatchesUp()
    {
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.1m, 0m, 6_000m));
        Trades("BTC", null, new CryptoTradePage([Fill("t1", 0.1m, 50_000m, buy: true, 90)], "v1:1"));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.3m, 0m, 18_000m));
        Trades("BTC", "v1:1", new CryptoTradePage([Fill("t2", 0.05m, 50_000m, buy: true, 60)], "v1:2", IsComplete: false));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var partial = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        partial.UntrackedQuantity.Should().Be(0m);
        partial.TrackedQuantity.Should().Be(0.15m);
        partial.TradeCursor.Should().Be("v1:2");

        Trades("BTC", "v1:2", new CryptoTradePage([Fill("t3", 0.15m, 56_000m, buy: true, 30)], "v1:3"));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var caughtUp = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        caughtUp.UntrackedQuantity.Should().Be(0m);
        caughtUp.CostBasisUsd.Should().Be(15_900m);
    }

    [Fact]
    public async Task RevolutX_FirstWalkLongerThanOneRun_IsWalkedToTheSnapshot_AndNeverRealizesAgainstLotsHeldAtConnect()
    {
        // 1 BTC held at connect; bought 1 at 50k, sold 1 at 60k, sold the rest, then rebought 0.5 —
        // the row was recreated after the sell-out, so its walk starts again at connect.
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 0.5m, 0m, 30_000m));
        Trades("BTC", null, new CryptoTradePage(
            [Fill("t1", 1m, 50_000m, buy: true, 50_000), Fill("t2", 1m, 60_000m, buy: false, 45_000)],
            "v1:26",
            IsComplete: false));
        Trades("BTC", "v1:26", new CryptoTradePage(
            [Fill("t3", 1m, 65_000m, buy: false, 20_000), Fill("t4", 0.5m, 58_000m, buy: true, 10)],
            "v1:40"));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var row = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        row.RealizedPnlUsd.Should().Be(0m, "the lots sold were partly held at connect, at a cost never seen");
        row.CostBasisUsd.Should().Be(29_000m);
        row.UntrackedQuantity.Should().Be(0m);
        row.TradeCount.Should().Be(4);
        row.TradeCursor.Should().Be("v1:40");
    }

    [Fact]
    public async Task RevolutX_FirstWalkThatStopsAdvancing_IsHeldBack_AndLeavesTheLedgerUntouched()
    {
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX, new CryptoAssetBalance("BTC", 1m, 0m, 60_000m));
        Trades("BTC", null, new CryptoTradePage(
            [Fill("t1", 1m, 50_000m, buy: true, 90), Fill("t2", 1m, 60_000m, buy: false, 60)],
            null,
            IsComplete: false));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var row = await _db.CryptoHoldings.AsNoTracking().SingleAsync();
        row.TrackedQuantity.Should().BeNull();
        row.RealizedPnlUsd.Should().BeNull();
        row.TradeCursor.Should().BeNull();
        row.TradeCount.Should().Be(0);
    }

    [Fact]
    public async Task VenueFiat_IsStoredFlagged_NeverWalked_AndReadAsVenueCash()
    {
        await ConnectAsync(CryptoExchangeProvider.RevolutX);
        Holdings(_revolutX,
            new CryptoAssetBalance("BTC", 0.1m, 0m, 6_000m),
            new CryptoAssetBalance("EUR", 1_000m, 0m, 1m, IsFiat: true));

        await SyncAsync(CryptoExchangeProvider.RevolutX);

        var eur = await _db.CryptoHoldings.AsNoTracking().SingleAsync(h => h.Asset == "EUR");
        eur.IsFiat.Should().BeTrue();
        eur.TrackedQuantity.Should().BeNull();
        _revolutX.Verify(a => a.GetTradesAsync(
            It.IsAny<string>(), It.IsAny<string>(), "EUR", It.IsAny<string?>(),
            It.IsAny<CryptoTradeWalk>(), It.IsAny<CancellationToken>()), Times.Never);

        var summaries = await new CryptoHoldingsReader(new CryptoHoldingRepository(_db)).GetHoldingsAsync(_userId);
        var eurSummary = summaries.Single(h => h.Asset == "EUR");
        eurSummary.IsVenueFiat.Should().BeTrue();
        eurSummary.UsdValue.Should().Be(Math.Round(CurrencyConverter.ToUsd(1_000m, "EUR"), 4),
            "venue fiat is converted at the reader boundary with the current rate, not the stored one");
        eurSummary.CostBasisUsd.Should().BeNull();

        var book = await ReadBookAsync();
        book.BankingCashUsd.Should().Be(0m, "venue fiat is never bank cash");
        book.VenueCashUsd.Should().Be(eurSummary.UsdValue);
        book.CashUsd.Should().Be(eurSummary.UsdValue);
        book.Positions.Should().ContainSingle().Which.Symbol.Should().Be("BTC");
        book.TotalValueUsd.Should().Be(6_000m + eurSummary.UsdValue);
    }

    private async Task<BookFigures> ReadBookAsync()
    {
        var banking = new Mock<IBankingAccountsReader>();
        banking
            .Setup(b => b.GetAccountSummariesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage
            .Setup(b => b.GetHoldingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return await new BookFiguresService(
            [
                new CryptoWealthSource(new CryptoHoldingsReader(new CryptoHoldingRepository(_db))),
                new BrokerageWealthSource(brokerage.Object),
                new BankingWealthSource(banking.Object),
            ],
            NullLogger<BookFiguresService>.Instance).ReadAsync(_userId);
    }
}

/// <summary>
/// The real repository for every read and write except the upsert, which is raw
/// <c>INSERT ... ON CONFLICT</c> SQL the in-memory provider cannot run (it is covered on Postgres by
/// <c>CryptoHoldingUpsertTests</c>); here it does the equivalent query-then-insert so the handler's
/// orchestration is still exercised.
/// </summary>
file sealed class InMemoryUpsertHoldings(CryptoSyncDbContext db) : ICryptoHoldingRepository
{
    private readonly CryptoHoldingRepository _inner = new(db);

    public async Task UpsertRangeAsync(IReadOnlyList<CryptoHolding> holdings, CancellationToken ct = default)
    {
        foreach (var holding in holdings)
        {
            var existing = await db.CryptoHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .FirstOrDefaultAsync(h => h.UserId == holding.UserId
                    && h.Provider == holding.Provider
                    && h.Asset == holding.Asset, ct);

            if (existing is null)
                await db.CryptoHoldings.AddAsync(holding, ct);
            else
                existing.Update(holding.FreeQuantity, holding.LockedQuantity, holding.UsdValue, holding.IsFiat);
        }
    }

    public Task<IReadOnlyList<CryptoHolding>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _inner.GetByUserIdAsync(userId, ct);

    public Task<IReadOnlyList<CryptoHolding>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default)
        => _inner.GetByUserIdUnscopedAsync(userId, ct);

    public Task<IReadOnlyList<CryptoHolding>> GetByUserAndProviderUnscopedAsync(
        Guid userId, string provider, CancellationToken ct = default)
        => _inner.GetByUserAndProviderUnscopedAsync(userId, provider, ct);

    public Task<IReadOnlyList<CryptoHolding>> GetAllByUserAndProviderAsync(
        Guid userId, string provider, CancellationToken ct = default)
        => _inner.GetAllByUserAndProviderAsync(userId, provider, ct);

    public Task<IReadOnlyList<CryptoHolding>> GetAllByUserAndProviderUnscopedAsync(
        Guid userId, string provider, CancellationToken ct = default)
        => _inner.GetAllByUserAndProviderUnscopedAsync(userId, provider, ct);

    public void RemoveRange(IEnumerable<CryptoHolding> holdings) => _inner.RemoveRange(holdings);

    public Task DeleteByUserAndProviderAsync(Guid userId, string provider, CancellationToken ct = default)
        => _inner.DeleteByUserAndProviderAsync(userId, provider, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _inner.SaveChangesAsync(ct);
}
