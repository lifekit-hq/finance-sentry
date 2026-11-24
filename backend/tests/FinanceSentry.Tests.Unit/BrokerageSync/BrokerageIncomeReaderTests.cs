namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

public class BrokerageIncomeReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 1, 1);

    private readonly Mock<IHistoricalExchangeRateService> _fx = new();

    public BrokerageIncomeReaderTests()
    {
        // 1 UAH = 0.025 USD on every day; USD passes through.
        _fx.Setup(f => f.ConvertToUsdAsync(It.IsAny<decimal>(), "USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal amount, string _, DateOnly _, CancellationToken _) => new ConvertResult(amount, FxStatus.Exact));
        _fx.Setup(f => f.ConvertToUsdAsync(It.IsAny<decimal>(), "UAH", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal amount, string _, DateOnly _, CancellationToken _) => new ConvertResult(amount * 0.025m, FxStatus.Exact));
    }

    private static BrokerageSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private static BrokerageCashTransaction Cash(
        string provider, string type, decimal amount, string currency, DateTime at, string? symbol, Guid? userId = null)
        => new(userId ?? UserId, provider, Guid.NewGuid().ToString(), null, null, symbol, at, amount, type, null, null, currency, null);

    private static DateTime Utc(int month, int day) => new(2026, month, day, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadAsync_ReturnsIncomeAndWithholdingSigned_ConvertedAtEachRowsCurrency()
    {
        await using var db = NewDb();
        db.BrokerageCashTransactions.AddRange(
            Cash("ibkr", "Dividends", 10m, "USD", Utc(3, 1), "aapl"),
            Cash("ibkr", "Withholding Tax", -1.5m, "USD", Utc(3, 1), "AAPL"),
            Cash("ibkr", "Payment In Lieu Of Dividends", 2m, "USD", Utc(4, 1), "MSFT"),
            Cash("ibkr", "Broker Interest Received", 4m, "USD", Utc(5, 1), null),
            Cash("inzhur", "Dividend crediting", 400m, "UAH", Utc(6, 1), "ABC"),
            Cash("inzhur", "Distribution tax", -40m, "UAH", Utc(6, 1), "ABC"));
        await db.SaveChangesAsync();

        var receipts = await new BrokerageIncomeReader(db, _fx.Object).ReadAsync(UserId, From);

        receipts.Should().Equal(
            new IncomeReceipt("AAPL", IncomeReceiptKind.Dividend, new DateOnly(2026, 3, 1), 10m, false),
            new IncomeReceipt("AAPL", IncomeReceiptKind.Dividend, new DateOnly(2026, 3, 1), -1.5m, true),
            new IncomeReceipt("MSFT", IncomeReceiptKind.Dividend, new DateOnly(2026, 4, 1), 2m, false),
            new IncomeReceipt("CASH", IncomeReceiptKind.Interest, new DateOnly(2026, 5, 1), 4m, false),
            new IncomeReceipt("ABC", IncomeReceiptKind.Dividend, new DateOnly(2026, 6, 1), 10m, false),
            new IncomeReceipt("ABC", IncomeReceiptKind.Dividend, new DateOnly(2026, 6, 1), -1m, true));
    }

    [Fact]
    public async Task ReadAsync_CarriesTheFxStatusOfEachRowsConversion()
    {
        _fx.Setup(f => f.ConvertToUsdAsync(It.IsAny<decimal>(), "PLN", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal amount, string _, DateOnly _, CancellationToken _) => new ConvertResult(amount, FxStatus.Unknown1To1));
        await using var db = NewDb();
        db.BrokerageCashTransactions.AddRange(
            Cash("ibkr", "Dividends", 10m, "USD", Utc(3, 1), "KO"),
            Cash("ibkr", "Dividends", 100m, "PLN", Utc(4, 1), "PKN"));
        await db.SaveChangesAsync();

        var receipts = await new BrokerageIncomeReader(db, _fx.Object).ReadAsync(UserId, From);

        receipts.Select(r => (r.Symbol, r.Fx)).Should().Equal(("KO", FxStatus.Exact), ("PKN", FxStatus.Unknown1To1));
    }

    [Fact]
    public async Task ReadAsync_LeavesOutNonIncomeRows_OtherUsersRows_AndRowsOnOrBeforeFrom()
    {
        await using var db = NewDb();
        db.BrokerageCashTransactions.AddRange(
            Cash("ibkr", "Deposits/Withdrawals", 500m, "USD", Utc(3, 1), null),
            Cash("ibkr", "Broker Interest Paid", -3m, "USD", Utc(3, 2), null),
            Cash("inzhur", "Payment", -100m, "UAH", Utc(3, 3), "ABC"),
            Cash("ibkr", "Dividends", 99m, "USD", Utc(3, 4), "AAPL", userId: Guid.NewGuid()),
            Cash("ibkr", "Dividends", 7m, "USD", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), "AAPL"),
            Cash("ibkr", "Dividends", 0m, "USD", Utc(3, 5), "AAPL"),
            Cash("ibkr", "Dividends", 5m, "USD", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), "KO"));
        await db.SaveChangesAsync();

        var receipts = await new BrokerageIncomeReader(db, _fx.Object).ReadAsync(UserId, From);

        receipts.Should().ContainSingle()
            .Which.Should().Be(new IncomeReceipt("KO", IncomeReceiptKind.Dividend, new DateOnly(2026, 1, 2), 5m, false));
    }

    [Fact]
    public async Task ReadAsync_WithNoRows_IsEmpty()
    {
        await using var db = NewDb();

        var receipts = await new BrokerageIncomeReader(db, _fx.Object).ReadAsync(UserId, From);

        receipts.Should().BeEmpty();
    }
}
