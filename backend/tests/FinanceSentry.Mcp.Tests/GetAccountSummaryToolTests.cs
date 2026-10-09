using FinanceSentry.Core.Interfaces;
using FinanceSentry.Mcp.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetAccountSummaryToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IBankingAccountsReader> _bankingReader = new();
    private readonly Mock<ICryptoHoldingsReader> _cryptoReader = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerageReader = new();

    private GetAccountSummaryTool CreateSut() =>
        new(_bankingReader.Object, _cryptoReader.Object, _brokerageReader.Object,
            new FakeIdentityResolver { ResolvedUserId = UserId },
            NullLogger<GetAccountSummaryTool>.Instance);

    [Fact]
    public async Task ExecuteAsync_MergesAllProviders_WhenAllReturnData()
    {
        _bankingReader.Setup(r => r.GetAccountSummariesAsync(UserId, default))
            .ReturnsAsync([
                new BankingAccountSummary(
                    Guid.NewGuid(), "Chase", "checking", "1234",
                    "truelayer", "USD", 1000m, 1000m, "synced", null)
            ]);

        _cryptoReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([
                new CryptoHoldingSummary("BTC", 0.5m, 0m, 25000m, DateTime.UtcNow, "binance")
            ]);

        _brokerageReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([
                new BrokerageHoldingSummary("AAPL", "stock", 10m, 1800m, DateTime.UtcNow, "ibkr")
            ]);

        var result = await CreateSut().ExecuteAsync();

        result.Should().HaveCount(3);

        var banking = result.Single(e => e.Provider == "truelayer");
        banking.Name.Should().Be("Chase");
        banking.Currency.Should().Be("USD");
        banking.Balance.Should().Be(1000m);

        var crypto = result.Single(e => e.Provider == "binance");
        crypto.AccountId.Should().Be("BTC");
        crypto.Name.Should().Be("BTC");
        crypto.Currency.Should().Be("USD");
        crypto.Balance.Should().Be(25000m);

        var brokerage = result.Single(e => e.Provider == "ibkr");
        brokerage.AccountId.Should().Be("AAPL");
        brokerage.Currency.Should().Be("USD");
        brokerage.Balance.Should().Be(1800m);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenAllProvidersReturnEmpty()
    {
        _bankingReader.Setup(r => r.GetAccountSummariesAsync(UserId, default))
            .ReturnsAsync([]);
        _cryptoReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([]);
        _brokerageReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_SkipsBankingProvider_WhenItThrows()
    {
        _bankingReader.Setup(r => r.GetAccountSummariesAsync(UserId, default))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        _cryptoReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([
                new CryptoHoldingSummary("ETH", 1m, 0m, 2000m, DateTime.UtcNow, "binance")
            ]);

        _brokerageReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync();

        result.Should().HaveCount(1);
        result.Single().Provider.Should().Be("binance");
    }

    [Fact]
    public async Task ExecuteAsync_SkipsCryptoProvider_WhenItThrows()
    {
        _bankingReader.Setup(r => r.GetAccountSummariesAsync(UserId, default))
            .ReturnsAsync([
                new BankingAccountSummary(
                    Guid.NewGuid(), "BofA", "savings", "5678",
                    "monobank", "USD", 500m, null, "synced", null)
            ]);

        _cryptoReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ThrowsAsync(new HttpRequestException("binance unreachable"));

        _brokerageReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync();

        result.Should().HaveCount(1);
        result.Single().Provider.Should().Be("monobank");
    }

    [Fact]
    public async Task ExecuteAsync_SkipsBrokerageProvider_WhenItThrows()
    {
        _bankingReader.Setup(r => r.GetAccountSummariesAsync(UserId, default))
            .ReturnsAsync([]);

        _cryptoReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([]);

        _brokerageReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ThrowsAsync(new TimeoutException("ibkr timeout"));

        var result = await CreateSut().ExecuteAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_UsesZeroBalance_WhenBankingBalanceIsNull()
    {
        _bankingReader.Setup(r => r.GetAccountSummariesAsync(UserId, default))
            .ReturnsAsync([
                new BankingAccountSummary(
                    Guid.NewGuid(), "FirstBank", "checking", "9999",
                    "truelayer", "EUR", null, null, "pending", null)
            ]);

        _cryptoReader.Setup(r => r.GetHoldingsAsync(UserId, default)).ReturnsAsync([]);
        _brokerageReader.Setup(r => r.GetHoldingsAsync(UserId, default)).ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync();

        result.Single().Balance.Should().Be(0m);
        result.Single().Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task ExecuteAsync_FlagsCashEntries_AndLeavesPositionsUnflagged()
    {
        _bankingReader.Setup(r => r.GetAccountSummariesAsync(UserId, default))
            .ReturnsAsync([
                new BankingAccountSummary(
                    Guid.NewGuid(), "Chase", "checking", "1234",
                    "truelayer", "USD", 1000m, 1000m, "synced", null)
            ]);
        _cryptoReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([
                new CryptoHoldingSummary("BTC", 0.5m, 0m, 25000m, DateTime.UtcNow, "binance"),
                new CryptoHoldingSummary("EUR", 100m, 0m, 110m, DateTime.UtcNow, "revolutx", IsVenueFiat: true)
            ]);
        _brokerageReader.Setup(r => r.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([
                new BrokerageHoldingSummary("AAPL", "STK", 10m, 1800m, DateTime.UtcNow, "ibkr"),
                new BrokerageHoldingSummary("USD Cash", "CASH", 500m, 500m, DateTime.UtcNow, "ibkr"),
                new BrokerageHoldingSummary("UAH Cash", "CASH", 1000m, 24m, DateTime.UtcNow, "inzhur")
            ]);

        var result = await CreateSut().ExecuteAsync();

        result.Where(e => e.IsCash).Select(e => e.AccountId)
            .Should().BeEquivalentTo(result.Single(e => e.Provider == "truelayer").AccountId, "EUR", "USD Cash", "UAH Cash");
        result.Where(e => !e.IsCash).Select(e => e.AccountId).Should().BeEquivalentTo("BTC", "AAPL");
    }
}
