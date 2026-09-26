namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// fs-435 S5 PR2 — a Flex statement pull must persist trades/cash transactions
/// idempotently on re-pull of an overlapping window, link every trade to a durable
/// instrument record by conid, and never convert money at ingestion.
/// </summary>
public class IbkrFlexTradeSyncServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class FakeInstrumentRepository : IBrokerageInstrumentRepository
    {
        public readonly List<BrokerageInstrument> Store = [];

        public Task<BrokerageInstrument?> GetByConidAsync(Guid userId, string provider, long conid, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(i => i.UserId == userId && i.Provider == provider && i.Conid == conid));

        public Task<BrokerageInstrument?> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(i => i.UserId == userId && i.Id == id));

        public Task<IReadOnlyList<BrokerageInstrument>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerageInstrument>>(Store.Where(i => i.UserId == userId).ToList());

        public Task AddAsync(BrokerageInstrument instrument, CancellationToken ct = default)
        {
            Store.Add(instrument);
            return Task.CompletedTask;
        }

        public void Update(BrokerageInstrument instrument)
        {
            // In-memory: mutations already applied to the tracked instance.
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeTradeRepository : IBrokerageTradeRepository
    {
        public readonly List<BrokerageTrade> Store = [];

        public Task<BrokerageTrade?> GetByExecutionIdAsync(Guid userId, string provider, string ibExecutionId, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(
                t => t.UserId == userId && t.Provider == provider && t.IbExecutionId == ibExecutionId));

        public Task AddAsync(BrokerageTrade trade, CancellationToken ct = default)
        {
            Store.Add(trade);
            return Task.CompletedTask;
        }

        public void Update(BrokerageTrade trade)
        {
            // In-memory: mutations already applied to the tracked instance.
        }

        public Task<IReadOnlyList<BrokerageTrade>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerageTrade>>(Store.Where(t => t.UserId == userId).ToList());

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeCashTransactionRepository : IBrokerageCashTransactionRepository
    {
        public readonly List<BrokerageCashTransaction> Store = [];

        public Task<BrokerageCashTransaction?> GetByIdempotencyKeyAsync(
            Guid userId, string provider, string idempotencyKey, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(
                c => c.UserId == userId && c.Provider == provider && c.IdempotencyKey == idempotencyKey));

        public Task AddAsync(BrokerageCashTransaction transaction, CancellationToken ct = default)
        {
            Store.Add(transaction);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BrokerageCashTransaction>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerageCashTransaction>>(Store.Where(c => c.UserId == userId).ToList());

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static FlexTradeXml SyntheticTrade(string execId, string tradeId = "700000001") => new()
    {
        AccountId = "U0000001",
        Currency = "USD",
        FxRateToBase = "1",
        AssetCategory = "STK",
        Symbol = "ZZZQ",
        Conid = "900000001",
        Isin = "US0000000001",
        TradeId = tradeId,
        TradeDate = "20220103",
        TradeTime = "143000",
        Quantity = "10",
        TradePrice = "123.45",
        Proceeds = "-1234.50",
        Taxes = "0",
        IbCommission = "-1.00",
        IbCommissionCurrency = "USD",
        OpenCloseIndicator = "O",
        CostBasis = "1235.50",
        RealizedPnl = "0",
        IbExecutionId = execId,
        OpenDateTime = "20220103;143000",
    };

    private static FlexCashTransactionXml SyntheticCashTransaction() => new()
    {
        AccountId = "U0000001",
        Currency = "USD",
        FxRateToBase = "1",
        AssetCategory = "STK",
        Symbol = "ZZZQ",
        Conid = "900000001",
        Isin = "US0000000001",
        DateTime = "20220103;000000",
        Amount = "1.00",
        Type = "Dividends",
        TradeId = "",
        Code = "",
        Section871mWithholding = "0.00",
    };

    private static FlexFinancialInstrumentXml SyntheticInstrument() => new()
    {
        AssetCategory = "STK",
        Symbol = "ZZZQ",
        Conid = "900000001",
        Isin = "US0000000001",
    };

    private (
        Mock<IIbkrFlexStatementFetcher> Fetcher,
        FakeInstrumentRepository InstrumentRepo,
        FakeTradeRepository TradeRepo,
        FakeCashTransactionRepository CashRepo,
        IbkrFlexTradeSyncService Service) Wiring()
    {
        var fetcher = new Mock<IIbkrFlexStatementFetcher>(MockBehavior.Strict);
        var instrumentRepo = new FakeInstrumentRepository();
        var tradeRepo = new FakeTradeRepository();
        var cashRepo = new FakeCashTransactionRepository();

        var service = new IbkrFlexTradeSyncService(
            fetcher.Object, instrumentRepo, tradeRepo, cashRepo, NullLogger<IbkrFlexTradeSyncService>.Instance);

        return (fetcher, instrumentRepo, tradeRepo, cashRepo, service);
    }

    [Fact]
    public async Task SyncAsync_NoCredential_ReturnsNoOpResult_AndPersistsNothing()
    {
        var (fetcher, instrumentRepo, tradeRepo, cashRepo, service) = Wiring();
        fetcher.Setup(f => f.FetchAsync(UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FlexStatementXml?)null);

        var result = await service.SyncAsync(UserId);

        result.Should().Be(IbkrFlexTradeSyncResult.NoCredential);
        instrumentRepo.Store.Should().BeEmpty();
        tradeRepo.Store.Should().BeEmpty();
        cashRepo.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncAsync_LinksTradeToInstrumentByConid()
    {
        var (fetcher, instrumentRepo, tradeRepo, _, service) = Wiring();
        var statement = new FlexStatementXml
        {
            AccountId = "U0000001",
            Trades = [SyntheticTrade("0000abcd.00000001.01")],
            FinancialInstruments = [SyntheticInstrument()],
        };
        fetcher.Setup(f => f.FetchAsync(UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(statement);

        await service.SyncAsync(UserId);

        instrumentRepo.Store.Should().ContainSingle();
        var instrument = instrumentRepo.Store[0];
        instrument.Conid.Should().Be(900000001);

        tradeRepo.Store.Should().ContainSingle();
        tradeRepo.Store[0].InstrumentId.Should().Be(instrument.Id);
    }

    [Fact]
    public async Task SyncAsync_RepullOfOverlappingWindow_IsIdempotent_NoDuplicateTradeRows()
    {
        var (fetcher, _, tradeRepo, _, service) = Wiring();
        var statement = new FlexStatementXml
        {
            AccountId = "U0000001",
            Trades = [SyntheticTrade("0000abcd.00000001.01")],
            FinancialInstruments = [SyntheticInstrument()],
        };
        fetcher.Setup(f => f.FetchAsync(UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(statement);

        await service.SyncAsync(UserId);
        await service.SyncAsync(UserId);

        tradeRepo.Store.Should().ContainSingle("re-pulling the same execution id must upsert, not duplicate");
    }

    [Fact]
    public async Task SyncAsync_RepullOfOverlappingWindow_CashTransactions_AreIdempotent()
    {
        var (fetcher, _, _, cashRepo, service) = Wiring();
        var statement = new FlexStatementXml
        {
            AccountId = "U0000001",
            CashTransactions = [SyntheticCashTransaction()],
        };
        fetcher.Setup(f => f.FetchAsync(UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(statement);

        await service.SyncAsync(UserId);
        await service.SyncAsync(UserId);

        cashRepo.Store.Should().ContainSingle("re-pulling the same cash transaction row must upsert, not duplicate");
    }

    [Fact]
    public async Task SyncAsync_StoresTradeAmountsInNativeCurrency_NeverConverted()
    {
        var (fetcher, _, tradeRepo, _, service) = Wiring();
        var statement = new FlexStatementXml
        {
            AccountId = "U0000001",
            Trades = [SyntheticTrade("0000abcd.00000001.01")],
        };
        fetcher.Setup(f => f.FetchAsync(UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(statement);

        await service.SyncAsync(UserId);

        var trade = tradeRepo.Store.Should().ContainSingle().Subject;
        trade.Currency.Should().Be("USD");
        trade.Price.Should().Be(123.45m);
        trade.Proceeds.Should().Be(-1234.50m);
        trade.FxRateToBase.Should().Be(1m);
    }
}
