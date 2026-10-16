namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// A retired account, or one the provider's list stopped naming for 24h, leaves every live balance (readers,
/// Accounts totals, aggregation) while its history keeps its own currency, and reopening restores both.
/// </summary>
public class ClosedAccountLiveTotalsTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly List<BankAccount> _accounts = [];

    private BankAccount Add(string currency, decimal balance, string externalId)
    {
        var account = new BankAccount(UserId, externalId, "Bank", "checking", "1234", "Owner", currency, UserId, "monobank")
        {
            CurrentBalance = balance,
            SyncStatus = "active",
        };
        _accounts.Add(account);
        return account;
    }

    private Mock<IBankAccountRepository> Repo()
    {
        var repo = new Mock<IBankAccountRepository>();
        repo.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _accounts.ToList());
        repo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _accounts.ToList());
        return repo;
    }

    private static Mock<ISyncJobRepository> Jobs()
    {
        var jobs = new Mock<ISyncJobRepository>();
        jobs.Setup(j => j.GetLastSuccessfulSyncTimesByUserUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DateTime>());
        jobs.Setup(j => j.GetLastSuccessfulSyncTimesByUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DateTime>());
        return jobs;
    }

    private BankingAccountsReader AccountsReader() => new(Repo().Object, Jobs().Object, Mock.Of<IClosedAccountsReader>(), _clock);

    private BankingTotalsReader TotalsReader() => new(Repo().Object, Jobs().Object, Mock.Of<IClosedAccountsReader>(), _clock);

    [Fact]
    public void An_account_counts_until_it_is_closed_or_has_been_absent_for_24_hours()
    {
        var account = Add("EUR", 10m, "a");
        account.CountsInLiveTotals(Now).Should().BeTrue();

        account.AbsentSince = Now;
        account.CountsInLiveTotals(Now.AddHours(23)).Should().BeTrue();
        account.CountsInLiveTotals(Now.AddHours(24)).Should().BeFalse();
        account.IsVanished(Now.AddHours(24)).Should().BeTrue();

        account.AbsentSince = null;
        account.Retire(Now, AccountClosedReasons.AbsentFromList);
        account.CountsInLiveTotals(Now).Should().BeFalse();
        account.IsVanished(Now).Should().BeFalse("a closed account is retired, not merely vanished");
    }

    [Fact]
    public void Reopening_clears_the_closure_and_absence_marks_and_the_last_failure()
    {
        var account = Add("EUR", 10m, "a");
        account.AbsentSince = Now;
        account.ApplyHealth(account.Health with { LastFailureAt = Now.AddDays(-9) });
        account.Retire(Now, AccountClosedReasons.AbsentFromList);

        account.Reopen();

        account.IsClosed.Should().BeFalse();
        (account.ClosedReason, account.ClosedBy, account.AbsentSince).Should().Be((null, null, null));
        account.Health.LastFailureAt.Should().BeNull();
        account.CountsInLiveTotals(Now).Should().BeTrue();
    }

    [Fact]
    public async Task A_retired_account_drops_out_of_the_live_readers_and_returns_on_reopen()
    {
        Add("USD", 100m, "live");
        var retired = Add("UAH", 4_000m, "gone");
        retired.Retire(Now, AccountClosedReasons.AbsentFromList);

        (await AccountsReader().GetAccountSummariesAsync(UserId)).Select(a => a.AccountId).Should().NotContain(retired.Id);
        (await AccountsReader().GetActiveAccountSnapshotsAsync(UserId)).Should().ContainSingle();
        (await TotalsReader().GetTotalUsdAsync(UserId)).Should().Be(100m);

        retired.Reopen();

        (await AccountsReader().GetAccountSummariesAsync(UserId)).Should().HaveCount(2);
        (await TotalsReader().GetTotalUsdAsync(UserId)).Should().Be(100m + CurrencyConverter.ToUsd(4_000m, "UAH"));
    }

    [Fact]
    public async Task A_closed_account_with_uah_history_still_converts_as_uah()
    {
        var retired = Add("UAH", 4_000m, "gone");
        retired.Retire(Now, AccountClosedReasons.Reissued);

        var nonLive = await AccountsReader().GetNonLiveAccountSummariesAsync(UserId);

        var summary = nonLive.Should().ContainSingle().Subject;
        summary.AccountId.Should().Be(retired.Id);
        summary.Currency.Should().Be("UAH");
        summary.Provider.Should().Be("monobank");
        retired.IsActive.Should().BeTrue("closing is not removal: history keeps its currency");
        CurrencyConverter.ToUsd(10m, summary.Currency).Should().NotBe(10m);
    }

    [Fact]
    public async Task A_vanished_account_leaves_live_totals_after_24h_and_returns_when_listed_again()
    {
        Add("USD", 100m, "live");
        var vanishing = Add("USD", 50m, "vanishing");
        vanishing.AbsentSince = Now;

        (await TotalsReader().GetTotalUsdAsync(UserId)).Should().Be(150m);

        _clock.Advance(TimeSpan.FromHours(25));
        (await TotalsReader().GetTotalUsdAsync(UserId)).Should().Be(100m);
        (await AccountsReader().GetNonLiveAccountSummariesAsync(UserId)).Select(a => a.AccountId).Should().Equal(vanishing.Id);

        vanishing.AbsentSince = null;
        (await TotalsReader().GetTotalUsdAsync(UserId)).Should().Be(150m);
    }

    [Fact]
    public async Task Aggregation_sums_live_accounts_only()
    {
        Add("USD", 100m, "live");
        Add("USD", 900m, "gone").Retire(Now, AccountClosedReasons.AbsentFromList);
        var vanished = Add("EUR", 50m, "vanished");
        vanished.AbsentSince = Now.AddHours(-30);
        var service = new AggregationService(Repo().Object, _clock);

        (await service.GetAggregatedBalanceAsync(UserId)).Should().Equal(new Dictionary<string, decimal> { ["USD"] = 100m });
        (await service.GetTotalNetWorthUsdAsync(UserId)).Should().Be(100m);
        (await service.GetAccountCountByTypeAsync(UserId)).Should().Equal(new Dictionary<string, int> { ["checking"] = 1 });
    }

    [Fact]
    public async Task The_accounts_list_shows_a_retired_account_with_its_marks_but_does_not_add_its_balance()
    {
        Add("USD", 100m, "live");
        var retired = Add("USD", 900m, "gone");
        retired.Retire(Now, AccountClosedReasons.Reissued);
        retired.PredecessorAccountId = null;
        var successor = Add("USD", 5m, "new");
        successor.PredecessorAccountId = retired.Id;
        var handler = new GetAccountsQueryHandler(Repo().Object, Jobs().Object, _clock);

        var result = await handler.Handle(new GetAccountsQuery(UserId), default);

        result.TotalCount.Should().Be(3);
        result.CurrencyTotals["USD"].Should().Be(105m);
        var closed = result.Accounts.Single(a => a.AccountId == retired.Id);
        closed.ClosedAt.Should().Be(Now);
        closed.ClosedReason.Should().Be(AccountClosedReasons.Reissued);
        result.Accounts.Single(a => a.AccountId == successor.Id).PredecessorAccountId.Should().Be(retired.Id);
    }
}
