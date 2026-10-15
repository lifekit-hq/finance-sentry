namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

public class BrokerageFlowReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime IbkrLinked = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InzhurLinked = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IHistoricalExchangeRateService> _fx = new();

    public BrokerageFlowReaderTests()
    {
        // 1 UAH = 0.025 USD on every day but 2026-10-12, when it is 0.024: the reader must ask for the row's own day.
        _fx.Setup(f => f.ConvertToUsdAsync(It.IsAny<decimal>(), "UAH", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal amount, string _, DateOnly date, CancellationToken _) =>
                new ConvertResult(amount * (date == new DateOnly(2026, 10, 12) ? 0.024m : 0.025m), FxStatus.Exact));
        _fx.Setup(f => f.ConvertToUsdAsync(It.IsAny<decimal>(), "USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal amount, string _, DateOnly _, CancellationToken _) => new ConvertResult(amount, FxStatus.Exact));
    }

    private static BrokerageSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private static IBKRCredential Ibkr(Guid userId, DateTime createdAt)
    {
        var credential = new IBKRCredential(userId, "key", "token", "dh", [], [], [], [], [], [], [], [], [], 1);
        typeof(IBKRCredential).GetProperty(nameof(IBKRCredential.CreatedAt))!.SetValue(credential, createdAt);
        return credential;
    }

    private static InzhurCredential Inzhur(DateTime createdAt)
    {
        var credential = new InzhurCredential(UserId);
        typeof(InzhurCredential).GetProperty(nameof(InzhurCredential.CreatedAt))!.SetValue(credential, createdAt);
        return credential;
    }

    private static BrokerageCashTransaction Cash(
        string provider, string type, decimal amount, string currency, DateTime at, Guid? userId = null)
        => new(userId ?? UserId, provider, Guid.NewGuid().ToString(), null, null, null, at, amount, type, null, null, currency, null);

    private BrokerageFlowReader Sut(BrokerageSyncDbContext db) => new(db, _fx.Object);

    [Fact]
    public async Task ReadAsync_WithNoLinkedProvider_IsEmpty()
    {
        await using var db = NewDb();
        db.BrokerageCashTransactions.Add(Cash("ibkr", "Deposits/Withdrawals", 100m, "USD", IbkrLinked.AddDays(5)));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Sources.Should().BeEmpty();
        reading.Flows.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_ConvertsEachFlowAtItsOwnDate_AndSkipsRowsUpToTheLinkDay()
    {
        await using var db = NewDb();
        db.InzhurCredentials.Add(Inzhur(InzhurLinked));
        db.BrokerageCashTransactions.AddRange(
            Cash("inzhur", "Deposit", 2200m, "UAH", new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc)),
            Cash("inzhur", "Deposit", 3000m, "UAH", new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)),
            Cash("inzhur", "Dividend crediting", 40m, "UAH", new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc)),
            Cash("inzhur", "Purchase", -3000m, "UAH", new DateTime(2026, 10, 12, 13, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Sources.Should().Equal(new FlowSourceCoverage("inzhur", new DateOnly(2026, 10, 7), true));
        reading.ClassifiedFlows.Should().Equal(
            new ClassifiedFlow(new DateOnly(2026, 10, 12), FlowClass.ExternalInflow, 72m),
            new ClassifiedFlow(new DateOnly(2026, 10, 13), FlowClass.Income, 1m));
        reading.SourcesWithoutFlows(new DateOnly(2026, 10, 14)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_NamesAProviderThatHasNeverReportedADeposit()
    {
        await using var db = NewDb();
        db.IBKRCredentials.Add(Ibkr(UserId, IbkrLinked));
        db.BrokerageCashTransactions.Add(Cash("ibkr", "Dividends", 12m, "USD", IbkrLinked.AddDays(10)));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Sources.Should().Equal(new FlowSourceCoverage("ibkr", new DateOnly(2026, 7, 1), false));
        reading.ClassifiedFlows.Should().ContainSingle(f => f.Class == FlowClass.Income && f.AmountUsd == 12m);
        reading.SourcesWithoutFlows(new DateOnly(2026, 10, 14)).Should().Equal("ibkr");
    }

    [Fact]
    public async Task ReadAsync_ReadsOnlyThatUsersRows_AndOnlyAfterFrom()
    {
        await using var db = NewDb();
        var other = Guid.NewGuid();
        db.IBKRCredentials.AddRange(Ibkr(UserId, IbkrLinked), Ibkr(other, IbkrLinked));
        db.BrokerageCashTransactions.AddRange(
            Cash("ibkr", "Deposits/Withdrawals", 100m, "USD", IbkrLinked.AddDays(2)),
            Cash("ibkr", "Deposits/Withdrawals", -40m, "USD", IbkrLinked.AddDays(20)),
            Cash("ibkr", "Deposits/Withdrawals", 999m, "USD", IbkrLinked.AddDays(20), other));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.FromDateTime(IbkrLinked.AddDays(10)));

        reading.ClassifiedFlows.Should().Equal(
            new ClassifiedFlow(DateOnly.FromDateTime(IbkrLinked.AddDays(20)), FlowClass.ExternalOutflow, 40m));
        reading.Sources.Single().FlowsRecorded.Should().BeTrue();
    }
}
