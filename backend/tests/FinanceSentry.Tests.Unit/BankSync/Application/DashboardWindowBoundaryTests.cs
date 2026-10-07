namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The dashboard's day-level windows (1W, MTD) through the real flow, category and
/// counterparty services: <c>windowFrom</c> is a UTC calendar day, inclusive, so a transaction
/// one second before midnight of that day is out and one at midnight is in — across month and
/// year edges and for counterparty movements too.
/// </summary>
public class DashboardWindowBoundaryTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0)
        => new(y, m, d, h, min, s, DateTimeKind.Utc);

    private static Transaction Tx(Guid accountId, decimal amount, string type, DateTime date, string description = "desc", string? category = null)
        => new(accountId, UserId, amount, date, description, Guid.NewGuid().ToString("N"), isPending: false)
        {
            TransactionType = type,
            PostedDate = date,
            IsActive = true,
            MerchantCategory = category,
        };

    private static BankAccount NewAccount()
        => new(UserId, $"item_{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", "USD", UserId, "truelayer");

    private static Counterparty Mom()
    {
        var cp = new Counterparty { UserId = Guid.Empty, Name = "Mom", FlowRole = FlowRoles.FamilySupport };
        cp.Rules.Add(new CounterpartyRule { CounterpartyId = cp.Id, MatchType = "description_contains", Pattern = "mom" });
        return cp;
    }

    private static DashboardQueryService Build(
        DateTime now, BankAccount account, IReadOnlyList<Transaction> transactions, params Counterparty[] counterparties)
    {
        var txRepo = new Mock<ITransactionRepository>();
        // Flow and classification read the whole loaded window (the sub-services clock their own
        // window start); the category reader loads from the day it is handed, as the real
        // repository does, so a window start that is not honoured would leak rows into it.
        txRepo.Setup(r => r.GetByUserIdSinceUnscopedAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(transactions);
        txRepo.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((Guid _, DateTime since, CancellationToken _) =>
                  (IReadOnlyList<Transaction>)transactions.Where(t => (t.PostedDate ?? t.TransactionDate) >= since).ToList());

        var accountRepo = new Mock<IBankAccountRepository>();
        accountRepo.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([account]);
        accountRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([account]);

        var counterpartyRepo = new Mock<ICounterpartyRepository>();
        counterpartyRepo.Setup(r => r.GetForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(counterparties.ToList());

        var policy = new Mock<ICommittedOutflowPolicy>();
        policy.Setup(p => p.LoadForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new CommittedOutflowRules { ActiveCommitmentKeys = new HashSet<string>(), PinnedMerchantKeys = new HashSet<string>() });

        var aggregation = new Mock<IAggregationService>();
        aggregation.Setup(s => s.GetAggregatedBalanceAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        aggregation.Setup(s => s.GetAccountCountByTypeAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var detection = new TransferDetectionService();
        return new DashboardQueryService(
            aggregation.Object,
            new MoneyFlowStatisticsService(txRepo.Object, accountRepo.Object, detection, policy.Object),
            new MerchantCategoryStatisticsService(txRepo.Object, accountRepo.Object, detection),
            new CounterpartyClassificationService(counterpartyRepo.Object, txRepo.Object, accountRepo.Object),
            new Mock<ISyncJobRepository>().Object,
            clock: new FixedClock(new DateTimeOffset(now)));
    }

    private static DateTime Parse(string iso)
        => DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);

    // (now, windowFrom, debit instant, expected inside the window)
    public static TheoryData<string, string, string, bool> Boundaries => new()
    {
        // MTD on the 1st: just the current day.
        { "2026-10-01T12:00:00Z", "2026-10-01", "2026-09-30T23:59:59Z", false },
        { "2026-10-01T12:00:00Z", "2026-10-01", "2026-10-01T00:00:00Z", true },
        // MTD mid-month: the 1st is in, the last second of the previous month is not.
        { "2026-10-17T08:00:00Z", "2026-10-01", "2026-10-01T00:00:00Z", true },
        { "2026-10-17T08:00:00Z", "2026-10-01", "2026-09-30T23:59:59Z", false },
        // 1W straddling a month: 7 days including today = from the 25th.
        { "2026-10-01T09:00:00Z", "2026-09-25", "2026-09-25T00:00:00Z", true },
        { "2026-10-01T09:00:00Z", "2026-09-25", "2026-09-24T23:59:59Z", false },
        // 1W straddling the year edge.
        { "2027-01-03T09:00:00Z", "2026-12-28", "2026-12-28T00:00:00Z", true },
        { "2027-01-03T09:00:00Z", "2026-12-28", "2026-12-27T23:59:59Z", false },
        // MTD in January: the previous December is out.
        { "2027-01-15T09:00:00Z", "2027-01-01", "2026-12-31T23:59:59Z", false },
        { "2027-01-15T09:00:00Z", "2027-01-01", "2027-01-01T00:00:00Z", true },
        // Leap-day edge: 1W from Feb 26 in a leap year on Mar 3.
        { "2028-03-03T09:00:00Z", "2028-02-26", "2028-02-26T00:00:00Z", true },
        { "2028-03-03T09:00:00Z", "2028-02-26", "2028-02-25T23:59:59Z", false },
    };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task WindowFrom_IncludesTheStartDayAndExcludesTheSecondBefore(
        string now, string windowFrom, string debitAt, bool expectedInside)
    {
        var account = NewAccount();
        var debit = Tx(account.Id, 100m, "debit", Parse(debitAt), "Groceries", "GROCERIES");
        var sut = Build(Parse(now), account, [debit]);

        var data = await sut.GetDashboardDataAsync(UserId, months: 1, windowFrom: DateOnly.Parse(windowFrom));

        var expected = expectedInside ? 100m : 0m;
        data.WindowFlow!.Sum(f => f.OutflowUsd).Should().Be(expected);
        data.TopCategories.Sum(c => c.TotalSpend).Should().Be(expected);
    }

    [Fact]
    public async Task WindowFrom_LeavesTheWholeMonthHistoryUntouched()
    {
        var account = NewAccount();
        var before = Tx(account.Id, 40m, "debit", Utc(2026, 9, 30, 23, 59, 59));
        var inside = Tx(account.Id, 60m, "debit", Utc(2026, 10, 1));
        var sut = Build(Utc(2026, 10, 17), account, [before, inside]);

        var data = await sut.GetDashboardDataAsync(UserId, months: 1, windowFrom: new DateOnly(2026, 10, 1));

        data.MonthlyFlow.Single(f => f.Month == "2026-09").OutflowUsd.Should().Be(40m);
        data.MonthlyFlow.Single(f => f.Month == "2026-10").OutflowUsd.Should().Be(60m);
        data.WindowFlow!.Select(f => f.Month).Should().Equal("2026-10");
    }

    [Fact]
    public async Task WindowFlow_IsAbsentWithoutWindowFrom()
    {
        var account = NewAccount();
        var sut = Build(Utc(2026, 10, 17), account, [Tx(account.Id, 60m, "debit", Utc(2026, 10, 1))]);

        var data = await sut.GetDashboardDataAsync(UserId, months: 1);

        data.WindowFlow.Should().BeNull();
    }

    [Fact]
    public async Task WindowFrom_AFutureDayClampsToToday()
    {
        var account = NewAccount();
        var yesterday = Tx(account.Id, 10m, "debit", Utc(2026, 10, 16, 23, 59, 59));
        var today = Tx(account.Id, 25m, "debit", Utc(2026, 10, 17));
        var sut = Build(Utc(2026, 10, 17, 8), account, [yesterday, today]);

        var data = await sut.GetDashboardDataAsync(UserId, months: 1, windowFrom: new DateOnly(2026, 11, 30));

        data.WindowFlow!.Sum(f => f.OutflowUsd).Should().Be(25m);
    }

    [Fact]
    public async Task WindowFrom_BeforeTheLoadedHistoryClampsToItsFirstDay()
    {
        var account = NewAccount();
        var inHistory = Tx(account.Id, 30m, "debit", Utc(2026, 9, 1));
        var sut = Build(Utc(2026, 10, 17), account, [inHistory]);

        var data = await sut.GetDashboardDataAsync(UserId, months: 1, windowFrom: new DateOnly(2020, 1, 1));

        data.WindowFlow!.Sum(f => f.OutflowUsd).Should().Be(30m);
    }

    [Fact]
    public async Task WindowFrom_CutsCounterpartyMovementsAtTheDayButKeepsThemOutOfNormalFlow()
    {
        var account = NewAccount();
        var early = Tx(account.Id, 300m, "debit", Utc(2026, 9, 30, 23, 59, 59), "To Mom");
        var late = Tx(account.Id, 200m, "debit", Utc(2026, 10, 1), "To Mom");
        var sut = Build(Utc(2026, 10, 17), account, [early, late], Mom());

        var data = await sut.GetDashboardDataAsync(UserId, months: 1, windowFrom: new DateOnly(2026, 10, 1));

        // The pre-window transfer is a counterparty movement, so it is not ordinary spending
        // inside the window either - the window simply does not include it.
        data.WindowFlow!.Sum(f => f.OutflowUsd).Should().Be(200m);
        data.WindowFlow!.Sum(f => f.FamilySupportOutflowUsd).Should().Be(200m);
        data.TopCategories.Single(c => c.Category == CategoryKeys.FamilySupport).TotalSpend.Should().Be(200m);
    }

    // YTD stays month-based: windowMonths reaches back to January 1 whatever the date.
    [Theory]
    [InlineData("2027-01-15T09:00:00Z", 1, 1, "2026-12-31T23:59:59Z", false)]
    [InlineData("2027-01-15T09:00:00Z", 1, 1, "2027-01-01T00:00:00Z", true)]
    [InlineData("2026-12-31T23:00:00Z", 11, 12, "2025-12-31T23:59:59Z", false)]
    [InlineData("2026-12-31T23:00:00Z", 11, 12, "2026-01-01T00:00:00Z", true)]
    public async Task Ytd_WindowMonthsStartsOnJanuaryFirstAtTheYearEdges(
        string now, int months, int windowMonths, string debitAt, bool expectedInside)
    {
        var account = NewAccount();
        var debit = Tx(account.Id, 100m, "debit", Parse(debitAt), "Groceries", "GROCERIES");
        var sut = Build(Parse(now), account, [debit]);

        var data = await sut.GetDashboardDataAsync(UserId, months, windowMonths);

        data.TopCategories.Sum(c => c.TotalSpend).Should().Be(expectedInside ? 100m : 0m);
    }
}
