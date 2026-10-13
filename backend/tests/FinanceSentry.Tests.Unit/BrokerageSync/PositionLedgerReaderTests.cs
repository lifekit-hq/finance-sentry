namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class PositionLedgerReaderTests
{
    private const string Symbol = "MU";
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Day = new(2026, 3, 2, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Read_FillsOldestFirst_SkipsZeroQuantity()
    {
        var reader = Reader(
            [Trade(Day.AddDays(5), -2m, 110m), Trade(Day, 10m, 100m), Trade(Day.AddDays(1), 0m, 105m)],
            []);

        var ledger = await reader.ReadForSymbolAsync(User, Symbol);

        ledger.Fills.Select(f => f.Quantity).Should().Equal(10m, -2m);
        ledger.Fills[0].Commission.Should().Be(-1m);
        ledger.Fills[0].CommissionCurrency.Should().Be("USD");
        ledger.Fills[0].Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Read_KeepsOnlyDividendIncomeRows()
    {
        var reader = Reader(
            [],
            [
                Cash(Day.AddDays(2), -1.5m, "Withholding Tax"),
                Cash(Day, 10m, "Dividends"),
                Cash(Day, -3m, "Broker Interest Paid"),
                Cash(Day.AddDays(3), 4m, "Distribution crediting"),
                Cash(Day.AddDays(4), 0m, "Dividends"),
            ]);

        var ledger = await reader.ReadForSymbolAsync(User, Symbol);

        ledger.Income.Select(i => i.Amount).Should().Equal(10m, -1.5m, 4m);
    }

    private static PositionLedgerReader Reader(
        IReadOnlyList<BrokerageTrade> trades, IReadOnlyList<BrokerageCashTransaction> cash)
    {
        var tradeRepo = new Mock<IBrokerageTradeRepository>();
        tradeRepo.Setup(r => r.GetBySymbolUnscopedAsync(User, Symbol, It.IsAny<CancellationToken>())).ReturnsAsync(trades);
        var cashRepo = new Mock<IBrokerageCashTransactionRepository>();
        cashRepo.Setup(r => r.GetBySymbolUnscopedAsync(User, Symbol, It.IsAny<CancellationToken>())).ReturnsAsync(cash);
        return new PositionLedgerReader(tradeRepo.Object, cashRepo.Object);
    }

    private static BrokerageTrade Trade(DateTime at, decimal quantity, decimal price)
        => new(User, "ibkr", Guid.NewGuid().ToString("N"), "t", null, null, Symbol, null, null, at,
            quantity, price, -quantity * price, null, null, -1m, "USD", null, "USD", null);

    private static BrokerageCashTransaction Cash(DateTime at, decimal amount, string type)
        => new(User, "ibkr", Guid.NewGuid().ToString("N"), null, null, Symbol, at, amount, type, null, null, "USD", null);
}
