namespace FinanceSentry.Tests.Unit.BankSync.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

/// <summary>The honest day-window net behind the net-worth bridge's savings line.</summary>
public class HonestMonthlyFlowReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly Mock<IFlowBreakdownService> _breakdown = new();
    private readonly BankSyncDbContext _db = new(
        new DbContextOptionsBuilder<BankSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private static BankAccount Account(DateTime createdAt, bool isActive = true, DateTime? updatedAt = null)
    {
        var account = new BankAccount(UserId, Guid.NewGuid().ToString("N"), "Bank", "checking", "1234", "Owner", "USD", UserId, "truelayer")
        {
            IsActive = isActive,
        };
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(account, Guid.NewGuid());
        typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(account, createdAt);
        if (updatedAt is not null)
            typeof(Entity).GetProperty(nameof(Entity.UpdatedAt))!.SetValue(account, updatedAt);
        return account;
    }

    private static FlowBreakdownItem Item(Guid accountId, DateTime date, string bucket, decimal usd)
        => new(Guid.NewGuid(), accountId, "Bank", "1234", "USD", usd, usd, date, "tx", null, null, "in", bucket, null, null);

    private static DateTime At(DateOnly day, int hour = 9) => day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);

    private HonestMonthlyFlowReader Reader() => new(
        Mock.Of<IQueryHandler<GetMoneyFlowStatisticsQuery, IReadOnlyList<MonthlyFlow>>>(), _breakdown.Object, _db);

    private void Breakdown(params FlowBreakdownItem[] items)
        => _breakdown.Setup(b => b.GetRangeBreakdownAsync(
                UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FlowBreakdown(string.Empty, items));

    [Fact]
    public async Task AWindowThatEndsBeforeItStarts_IsZeroWithoutReadingTheBreakdown()
    {
        var net = await Reader().GetWindowNetAsync(UserId, Today, Today.AddDays(-1));

        net.Should().Be(0m);
        _breakdown.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    public async Task ReachesBackFromTodayToTheMonthTheWindowOpensIn(int monthsBack, int expectedMonths)
    {
        var from = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-monthsBack);
        Breakdown();

        await Reader().GetWindowNetAsync(UserId, from, Today);

        _breakdown.Verify(b => b.GetRangeBreakdownAsync(UserId, from, Today, expectedMonths, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task NetsIncomeLessSpending_AndLeavesEveryOtherBucketOut()
    {
        var account = Account(At(Today.AddDays(-90)));
        _db.BankAccounts.Add(account);
        await _db.SaveChangesAsync();
        var day = At(Today.AddDays(-2));
        Breakdown(
            Item(account.Id, day, FlowBuckets.Income, 500m),
            Item(account.Id, day, FlowBuckets.Income, 100m),
            Item(account.Id, day, FlowBuckets.Spending, 180m),
            Item(account.Id, day, FlowBuckets.Invested, 1_000m),
            Item(account.Id, day, FlowBuckets.InvestmentReturn, 70m),
            Item(account.Id, day, FlowBuckets.ExcludedPair, 40m),
            Item(account.Id, day, FlowBuckets.ExcludedRouting, 30m),
            Item(account.Id, day, FlowBuckets.ExcludedTransfer, 20m));

        var net = await Reader().GetWindowNetAsync(UserId, Today.AddDays(-5), Today);

        net.Should().Be(420m);
    }

    [Fact]
    public async Task LeavesOutAnAccountsTransactionsDatedBeforeItWasLinked()
    {
        var linkedOn = Today.AddDays(-3);
        var account = Account(At(linkedOn, hour: 15));
        _db.BankAccounts.Add(account);
        await _db.SaveChangesAsync();
        Breakdown(
            Item(account.Id, At(linkedOn.AddDays(-2)), FlowBuckets.Income, 300m),
            Item(account.Id, At(linkedOn.AddDays(-1)), FlowBuckets.Spending, 40m),
            Item(account.Id, At(linkedOn, hour: 8), FlowBuckets.Income, 25m),
            Item(account.Id, At(linkedOn.AddDays(1)), FlowBuckets.Income, 10m));

        var net = await Reader().GetWindowNetAsync(UserId, Today.AddDays(-5), Today);

        net.Should().Be(35m, "the link day and after count; the imported history before it arrives with the balance");
    }

    [Fact]
    public async Task LeavesOutARemovedAccountsTransactionsDatedAfterItsRemoval()
    {
        var removedOn = Today.AddDays(-2);
        var removed = Account(At(Today.AddDays(-90)), isActive: false, updatedAt: At(removedOn, hour: 12));
        _db.BankAccounts.Add(removed);
        await _db.SaveChangesAsync();
        Breakdown(
            Item(removed.Id, At(removedOn.AddDays(-1)), FlowBuckets.Income, 200m),
            Item(removed.Id, At(removedOn), FlowBuckets.Spending, 50m),
            Item(removed.Id, At(removedOn.AddDays(1)), FlowBuckets.Income, 999m));

        var net = await Reader().GetWindowNetAsync(UserId, Today.AddDays(-5), Today);

        net.Should().Be(150m);
    }

    [Fact]
    public async Task KeepsTransactionsOfAnAccountItCannotPlace_AndNeverReadsAnotherPersonsAccounts()
    {
        var other = Account(At(Today.AddDays(-1)));
        other.UserId = Guid.NewGuid();
        _db.BankAccounts.Add(other);
        await _db.SaveChangesAsync();
        Breakdown(
            Item(Guid.NewGuid(), At(Today.AddDays(-4)), FlowBuckets.Income, 60m),
            Item(other.Id, At(Today.AddDays(-4)), FlowBuckets.Income, 7m));

        var net = await Reader().GetWindowNetAsync(UserId, Today.AddDays(-5), Today);

        net.Should().Be(67m);
    }
}
