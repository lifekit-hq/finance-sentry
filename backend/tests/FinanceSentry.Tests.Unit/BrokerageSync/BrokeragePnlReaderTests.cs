namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

/// <summary>Issue 982: what the brokerage P&amp;L reader states and what it withholds. Synthetic data only.</summary>
public class BrokeragePnlReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IBrokerageHoldingsReader> _holdings = new();

    private static BrokerageSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private static BrokerageHoldingSummary Position(
        string symbol, decimal quantity, decimal value, decimal? costBasis, string basis = "Verified",
        string type = "STK")
        => new(symbol, type, quantity, value, T0, "ibkr", costBasis, basis);

    private static BrokerageTrade Fill(
        string symbol, decimal quantity, decimal price, int day, Guid? instrumentId = null, Guid? userId = null)
        => new(
            userId ?? UserId, "ibkr", $"{symbol}-{day}-{quantity}", $"{symbol}-{day}", null, null, symbol,
            quantity > 0 ? "O" : "C", null, T0.AddDays(day), quantity, price, -(quantity * price), null, null,
            null, "USD", null, "USD", null, instrumentId);

    private BrokeragePnlReader Sut(BrokerageSyncDbContext db, params BrokerageHoldingSummary[] positions)
    {
        _holdings.Setup(h => h.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(positions);
        return new BrokeragePnlReader(db, _holdings.Object);
    }

    [Fact]
    public async Task UnrealizedCoversOnlyVerifiedPositions_AndNamesTheRest()
    {
        await using var db = NewDb();
        var sut = Sut(
            db,
            Position("AAA", 10m, 1200m, 1000m),
            Position("BBB", 5m, 400m, null, basis: "Unknown"),
            Position("USD", 3m, 3m, null, type: "CASH"));

        var reading = await sut.ReadAsync(UserId, null);

        reading.Unrealized.Usd.Should().Be(200m);
        reading.Unrealized.CostBasisUsd.Should().Be(1000m);
        reading.Unrealized.PositionsMeasured.Should().Be(1);
        reading.Unrealized.PositionsTotal.Should().Be(2);
        reading.Unrealized.MeasuredValueUsd.Should().Be(1200m);
        reading.Unrealized.TotalValueUsd.Should().Be(1600m);
        reading.Unrealized.UnverifiedSymbols.Should().Equal("BBB");
    }

    [Fact]
    public async Task NoFillsAtAll_IsNotMeasurableRatherThanAZero()
    {
        await using var db = NewDb();

        var reading = await Sut(db, Position("AAA", 10m, 1200m, 1000m)).ReadAsync(UserId, null);

        reading.Realized.HasFills.Should().BeFalse();
    }

    [Fact]
    public async Task RealizedSumsExplainedSymbols_IncludingOnesNoLongerHeld()
    {
        await using var db = NewDb();
        db.BrokerageTrades.AddRange(
            Fill("AAA", 10m, 100m, 0), Fill("AAA", -4m, 150m, 5),
            Fill("GONE", 5m, 20m, 1), Fill("GONE", -5m, 10m, 6));
        await db.SaveChangesAsync();

        var reading = await Sut(db, Position("AAA", 6m, 900m, 600m)).ReadAsync(UserId, null);

        reading.Realized.HasFills.Should().BeTrue();
        reading.Realized.Usd.Should().Be(150m); // AAA +200, GONE -50
        reading.Realized.SymbolsComputed.Should().Be(2);
        reading.Realized.UnavailableSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task ASymbolWhoseHistoryTheFillsDoNotExplain_IsLeftOutAndNamed()
    {
        await using var db = NewDb();
        db.BrokerageTrades.AddRange(
            Fill("AAA", 10m, 100m, 0), Fill("AAA", -4m, 150m, 5),
            Fill("OLD", -3m, 50m, 2));
        await db.SaveChangesAsync();

        var reading = await Sut(db, Position("AAA", 6m, 900m, 600m)).ReadAsync(UserId, null);

        reading.Realized.Usd.Should().Be(200m);
        reading.Realized.SymbolsComputed.Should().Be(1);
        reading.Realized.UnavailableSymbols.Select(s => s.Symbol).Should().Equal("OLD");
    }

    [Fact]
    public async Task AnUnexplainedSymbolWithNoSellInTheWindow_DoesNotMakeTheFigurePartial()
    {
        await using var db = NewDb();
        db.BrokerageTrades.AddRange(Fill("OLD", -3m, 50m, 2), Fill("AAA", 10m, 100m, 0), Fill("AAA", -4m, 150m, 40));
        await db.SaveChangesAsync();

        var reading = await Sut(db, Position("AAA", 6m, 900m, 600m))
            .ReadAsync(UserId, DateOnly.FromDateTime(T0.AddDays(30)));

        reading.Realized.UnavailableSymbols.Should().BeEmpty();
        reading.Realized.Usd.Should().Be(200m);
    }

    [Fact]
    public async Task CurrencyConversionTrades_AreNotPositions()
    {
        await using var db = NewDb();
        var instrument = new BrokerageInstrument(UserId, "ibkr", 1L, "EUR.USD", "CASH", null);
        db.BrokerageInstruments.Add(instrument);
        db.BrokerageTrades.Add(Fill("EUR.USD", -1000m, 1.1m, 3, instrument.Id));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, null);

        reading.Realized.UnavailableSymbols.Should().BeEmpty();
        reading.Realized.SymbolsComputed.Should().Be(0);
    }

    [Fact]
    public async Task AnotherUsersFills_AreNeverRead()
    {
        await using var db = NewDb();
        db.BrokerageTrades.AddRange(Fill("AAA", 10m, 100m, 0, userId: Guid.NewGuid()), Fill("AAA", -10m, 200m, 1, userId: Guid.NewGuid()));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, null);

        reading.Realized.HasFills.Should().BeFalse();
    }
}
