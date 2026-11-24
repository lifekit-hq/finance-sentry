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

public class CryptoIncomeReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 1, 1);

    private static CryptoSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<CryptoSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private static CryptoFlowEvent Event(string type, DateTime at, decimal amount, string unit, decimal? priceUsd, Guid? userId = null) =>
        CryptoFlowEvent.Create(userId ?? UserId, "binance",
            new CryptoFlow(Guid.NewGuid().ToString(), type, at, amount, unit, false, priceUsd, null, null, null, "completed", null, null),
            at);

    private static DateTime Utc(int month, int day) => new(2026, month, day, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadAsync_ValuesRewardsAndAirdropsAtTheirPrice_AndLeavesAnUnpricedRowNull()
    {
        await using var db = NewDb();
        db.CryptoFlowEvents.AddRange(
            Event(CryptoFlowTypes.Reward, Utc(3, 1), 0.5m, " eth ", 2000m),
            Event(CryptoFlowTypes.Airdrop, Utc(4, 1), 2m, "SOL", null));
        await db.SaveChangesAsync();

        var receipts = await new CryptoIncomeReader(db).ReadAsync(UserId, From);

        receipts.Should().Equal(
            new IncomeReceipt("ETH", IncomeReceiptKind.Staking, new DateOnly(2026, 3, 1), 1000m, false),
            new IncomeReceipt("SOL", IncomeReceiptKind.Staking, new DateOnly(2026, 4, 1), null, false));
    }

    [Fact]
    public async Task ReadAsync_LeavesOutOtherFlowTypes_OtherUsersRows_AndRowsOnOrBeforeFrom()
    {
        await using var db = NewDb();
        db.CryptoFlowEvents.AddRange(
            Event(CryptoFlowTypes.FiatDeposit, Utc(3, 1), 1000m, "USD", 1m),
            Event(CryptoFlowTypes.CardBuy, Utc(3, 2), 1m, "BTC", 60000m),
            Event(CryptoFlowTypes.Reward, Utc(3, 3), 1m, "ETH", 2000m, userId: Guid.NewGuid()),
            Event(CryptoFlowTypes.Reward, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), 1m, "ETH", 2000m),
            Event(CryptoFlowTypes.Reward, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 3m, "DOT", 5m));
        await db.SaveChangesAsync();

        var receipts = await new CryptoIncomeReader(db).ReadAsync(UserId, From);

        receipts.Should().Equal(new IncomeReceipt("DOT", IncomeReceiptKind.Staking, new DateOnly(2026, 1, 2), 15m, false));
    }
}
