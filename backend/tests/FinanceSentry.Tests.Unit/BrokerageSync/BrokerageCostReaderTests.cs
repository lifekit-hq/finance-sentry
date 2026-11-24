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

public class BrokerageCostReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2025, 12, 31);

    private readonly Mock<IHistoricalExchangeRateService> _fx = new();

    public BrokerageCostReaderTests()
    {
        // 1 EUR = 2 USD and 1 UAH = 0.025 USD on every day; USD passes through.
        Convert("USD", 1m, FxStatus.Exact);
        Convert("EUR", 2m, FxStatus.Exact);
        Convert("UAH", 0.025m, FxStatus.Exact);
    }

    private void Convert(string currency, decimal rate, FxStatus status)
        => _fx.Setup(f => f.ConvertToUsdAsync(It.IsAny<decimal>(), currency, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal amount, string _, DateOnly _, CancellationToken _) => new ConvertResult(amount * rate, status));

    private static BrokerageSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private static BrokerageTrade Trade(
        DateTime at, decimal? commission, string? commissionCurrency, decimal? taxes, string currency = "USD",
        Guid? instrumentId = null, Guid? userId = null)
        => new(userId ?? UserId, "ibkr", Guid.NewGuid().ToString(), "t", null, null, "AAPL", "O", null, at, 1m, 100m, -100m,
            null, null, commission, commissionCurrency, taxes, currency, null, instrumentId);

    private static BrokerageCashTransaction Cash(string provider, string type, decimal amount, string currency, DateTime at, Guid? userId = null)
        => new(userId ?? UserId, provider, Guid.NewGuid().ToString(), null, null, null, at, amount, type, null, null, currency, null);

    private static DateTime Utc(int month, int day) => new(2026, month, day, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadAsync_ReadsTradeCommissionsAndTaxes_PaidAsPositiveCosts_InUsd()
    {
        await using var db = NewDb();
        db.BrokerageTrades.AddRange(
            Trade(Utc(3, 1), -1.5m, "USD", null),
            Trade(Utc(4, 1), -2m, "EUR", -0.5m, currency: "EUR"),
            Trade(Utc(5, 1), 0.25m, "USD", null));
        await db.SaveChangesAsync();

        var reading = await new BrokerageCostReader(db, _fx.Object).ReadAsync(UserId, From);

        reading.Items.Should().Equal(
            new CostItem(CostKind.Commission, new DateOnly(2026, 3, 1), 1.5m),
            new CostItem(CostKind.Commission, new DateOnly(2026, 4, 1), 4m),
            new CostItem(CostKind.Fee, new DateOnly(2026, 4, 1), 1m),
            new CostItem(CostKind.Commission, new DateOnly(2026, 5, 1), -0.25m));
        reading.UnvaluedRows.Should().Be(0);
    }

    [Fact]
    public async Task ReadAsync_ReadsACurrencyPairTradesCommissionAsTheCostOfConverting()
    {
        await using var db = NewDb();
        var pair = new BrokerageInstrument(UserId, "ibkr", 1, "EUR.USD", "CASH");
        var stock = new BrokerageInstrument(UserId, "ibkr", 2, "AAPL", "STK");
        db.BrokerageInstruments.AddRange(pair, stock);
        db.BrokerageTrades.AddRange(
            Trade(Utc(3, 1), -2m, "USD", null, instrumentId: pair.Id),
            Trade(Utc(3, 2), -1m, "USD", null, instrumentId: stock.Id));
        await db.SaveChangesAsync();

        var reading = await new BrokerageCostReader(db, _fx.Object).ReadAsync(UserId, From);

        reading.Items.Should().Equal(
            new CostItem(CostKind.FxConversion, new DateOnly(2026, 3, 1), 2m),
            new CostItem(CostKind.Commission, new DateOnly(2026, 3, 2), 1m));
    }

    [Fact]
    public async Task ReadAsync_ReadsFeeRowsConversionSurchargesAndWithholding_AndLeavesOtherCashRowsOut()
    {
        await using var db = NewDb();
        db.BrokerageCashTransactions.AddRange(
            Cash("ibkr", "Other Fees", -10m, "USD", Utc(3, 1)),
            Cash("ibkr", "Commission Adjustments", 1m, "USD", Utc(3, 2)),
            Cash("ibkr", "Withholding Tax", -1.5m, "USD", Utc(3, 3)),
            Cash("ibkr", "Withholding Tax", 0.5m, "USD", Utc(3, 4)),
            Cash("inzhur", "Conversion Surcharge", -400m, "UAH", Utc(3, 5)),
            Cash("inzhur", "Dividend tax", -40m, "UAH", Utc(3, 6)),
            Cash("ibkr", "Dividends", 10m, "USD", Utc(3, 7)),
            Cash("ibkr", "Deposits/Withdrawals", -500m, "USD", Utc(3, 8)),
            Cash("ibkr", "Broker Interest Paid", -3m, "USD", Utc(3, 9)));
        await db.SaveChangesAsync();

        var reading = await new BrokerageCostReader(db, _fx.Object).ReadAsync(UserId, From);

        reading.Items.Should().Equal(
            new CostItem(CostKind.Fee, new DateOnly(2026, 3, 1), 10m),
            new CostItem(CostKind.Commission, new DateOnly(2026, 3, 2), -1m),
            new CostItem(CostKind.Withholding, new DateOnly(2026, 3, 3), 1.5m),
            new CostItem(CostKind.Withholding, new DateOnly(2026, 3, 4), -0.5m),
            new CostItem(CostKind.FxConversion, new DateOnly(2026, 3, 5), 10m),
            new CostItem(CostKind.Withholding, new DateOnly(2026, 3, 6), 1m));
    }

    [Fact]
    public async Task ReadAsync_LeavesOutOtherUsersRows_AndRowsOnOrBeforeFrom()
    {
        await using var db = NewDb();
        db.BrokerageTrades.AddRange(
            Trade(Utc(3, 1), -1m, "USD", null, userId: Guid.NewGuid()),
            Trade(new DateTime(2025, 12, 31, 12, 0, 0, DateTimeKind.Utc), -1m, "USD", null));
        db.BrokerageCashTransactions.AddRange(
            Cash("ibkr", "Other Fees", -1m, "USD", Utc(3, 1), userId: Guid.NewGuid()),
            Cash("ibkr", "Other Fees", -1m, "USD", new DateTime(2025, 12, 30, 12, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var reading = await new BrokerageCostReader(db, _fx.Object).ReadAsync(UserId, From);

        reading.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_CarriesTheFxStatusOfEachRowsConversion()
    {
        Convert("PLN", 1m, FxStatus.Unknown1To1);
        await using var db = NewDb();
        db.BrokerageCashTransactions.Add(Cash("ibkr", "Other Fees", -10m, "PLN", Utc(3, 1)));
        await db.SaveChangesAsync();

        var reading = await new BrokerageCostReader(db, _fx.Object).ReadAsync(UserId, From);

        reading.Items.Should().Equal(new CostItem(CostKind.Fee, new DateOnly(2026, 3, 1), 10m, FxStatus.Unknown1To1));
    }
}
