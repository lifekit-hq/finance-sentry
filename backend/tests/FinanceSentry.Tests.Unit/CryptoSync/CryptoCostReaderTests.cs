using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class CryptoCostReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2025, 12, 31);

    private static CryptoSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<CryptoSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private static CryptoTradeRecord Fill(DateTime at, decimal? fee, decimal? feeUsd, Guid? userId = null) =>
        CryptoTradeRecord.Create(userId ?? UserId, "binance",
            new CryptoTrade(Guid.NewGuid().ToString(), "BTC", "USDT", 0.1m, 60000m, 6000m, true, at, fee, fee is null ? null : "BNB", feeUsd));

    private static DateTime Utc(int month, int day) => new(2026, month, day, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadAsync_ReadsEachFillsFeeInUsd_AsACommission()
    {
        await using var db = NewDb();
        db.CryptoTrades.AddRange(Fill(Utc(3, 1), 0.001m, 0.6m), Fill(Utc(4, 1), 6m, 6m));
        await db.SaveChangesAsync();

        var reading = await new CryptoCostReader(db).ReadAsync(UserId, From);

        reading.Items.Should().Equal(
            new CostItem(CostKind.Commission, new DateOnly(2026, 3, 1), 0.6m),
            new CostItem(CostKind.Commission, new DateOnly(2026, 4, 1), 6m));
        reading.UnvaluedRows.Should().Be(0);
    }

    [Fact]
    public async Task ReadAsync_CountsAFeeWithNoUsdValueAsUnvalued_AndSkipsFillsThatReportedNoFee()
    {
        await using var db = NewDb();
        db.CryptoTrades.AddRange(Fill(Utc(3, 1), 5m, null), Fill(Utc(3, 2), null, null), Fill(Utc(3, 3), 1m, 2m));
        await db.SaveChangesAsync();

        var reading = await new CryptoCostReader(db).ReadAsync(UserId, From);

        reading.Items.Should().Equal(new CostItem(CostKind.Commission, new DateOnly(2026, 3, 3), 2m));
        reading.UnvaluedRows.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_LeavesOutOtherUsersFills_AndFillsOnOrBeforeFrom()
    {
        await using var db = NewDb();
        db.CryptoTrades.AddRange(
            Fill(Utc(3, 1), 1m, 1m, userId: Guid.NewGuid()),
            Fill(new DateTime(2025, 12, 31, 12, 0, 0, DateTimeKind.Utc), 1m, 1m));
        await db.SaveChangesAsync();

        var reading = await new CryptoCostReader(db).ReadAsync(UserId, From);

        reading.Items.Should().BeEmpty();
    }
}
