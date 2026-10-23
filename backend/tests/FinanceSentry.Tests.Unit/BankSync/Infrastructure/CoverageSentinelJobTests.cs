namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// The coverage sentinel (#1238). The regression is the Revolut shape: steady card spend through August, then it stops
/// (~2026-09-07 is the first empty week) while the syncs keep succeeding.
/// </summary>
public class CoverageSentinelJobTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 22, 4, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Today);
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly BankSyncDbContext _db = new(
        new DbContextOptionsBuilder<BankSyncDbContext>().UseInMemoryDatabase($"coverage-{Guid.NewGuid():N}").Options,
        NoCurrentUser.Instance);

    private CoverageSentinelJob Job(params Guid[] closedUsers) =>
        new(_db, new StubClosedAccountsReader(closedUsers), _alerts.Object, _time, NullLogger<CoverageSentinelJob>.Instance);

    private BankAccount AddAccount(Guid userId, string currency = "EUR", bool recentSuccessfulSync = true)
    {
        var account = new BankAccount(userId, Guid.NewGuid().ToString(), "Revolut", "current", "3725", "Owner", currency,
            Guid.NewGuid(), "truelayer");
        _db.BankAccounts.Add(account);
        _db.SyncJobs.Add(new SyncJob(account.Id, userId)
        {
            Status = recentSuccessfulSync ? "success" : "failed",
            StartedAt = Today.UtcDateTime.AddHours(-3),
            CompletedAt = Today.UtcDateTime.AddHours(-3),
        });
        return account;
    }

    /// <summary>Daily card spend of <paramref name="perDay"/> from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    private void Spend(BankAccount account, DateTime from, DateTime to, decimal perDay, string category = "FOOD_AND_DRINK")
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var tx = new Transaction(account.Id, account.UserId, perDay, d.AddHours(12), "card", Guid.NewGuid().ToString())
            {
                MerchantCategory = category,
                TransactionType = "debit",
                IsActive = true,
            };
            tx.ValidateInvariants();
            _db.Transactions.Add(tx);
        }
    }

    private static readonly DateTime Start = new(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Revolut_shape_steady_spend_then_silence_with_working_syncs_fires_and_records_a_hole()
    {
        var userId = Guid.NewGuid();
        var account = AddAccount(userId);
        Spend(account, Start, new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), 25m); // through Sunday before 09-07
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        _alerts.Verify(a => a.GenerateCoverageGapAlertAsync(
            userId, account.Id, "Revolut ···3725", 2, It.Is<decimal>(b => b > 100m), It.IsAny<CancellationToken>()), Times.Once);
        var hole = await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
        Assert.Equal(account.Id, hole.AccountId);
        Assert.Equal(new DateOnly(2026, 9, 7), hole.From);
        Assert.Null(hole.To);

        var reader = new CoverageHoleReader(_db);
        var holes = await reader.GetHolesAsync(userId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Single(holes);
        Assert.True(holes[0].Covers(new DateOnly(2026, 9, 20)));
        Assert.False(holes[0].Covers(new DateOnly(2026, 9, 6)));
        Assert.Empty(await reader.GetHolesAsync(userId, new DateOnly(2026, 5, 1), new DateOnly(2026, 9, 6)));
        Assert.Empty(await reader.GetHolesAsync(Guid.NewGuid(), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public async Task A_second_run_does_not_stack_a_second_hole_or_alert()
    {
        var account = AddAccount(Guid.NewGuid());
        Spend(account, Start, new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), 25m);
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();
        await Job().ExecuteAsync();

        Assert.Equal(1, await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).CountAsync());
        _alerts.Verify(a => a.GenerateCoverageGapAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Syncs_that_are_not_succeeding_are_a_different_alert_not_a_gap()
    {
        var account = AddAccount(Guid.NewGuid(), recentSuccessfulSync: false);
        Spend(account, Start, new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), 25m);
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        Assert.Empty(await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).ToListAsync());
    }

    [Fact]
    public async Task Spend_that_keeps_flowing_does_not_fire()
    {
        var account = AddAccount(Guid.NewGuid());
        Spend(account, Start, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc), 25m);
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        Assert.Empty(await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).ToListAsync());
    }

    [Fact]
    public async Task Only_day_to_day_categories_count_as_the_spend_that_stopped()
    {
        var account = AddAccount(Guid.NewGuid());
        Spend(account, Start, new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), 25m, "RENT_AND_UTILITIES");
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        Assert.Empty(await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).ToListAsync());
    }

    [Fact]
    public async Task A_closed_account_is_not_watched()
    {
        var userId = Guid.NewGuid();
        var account = AddAccount(userId);
        Spend(account, Start, new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), 25m);
        await _db.SaveChangesAsync();

        await Job(userId).ExecuteAsync();

        Assert.Empty(await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).ToListAsync());
    }

    [Fact]
    public async Task Spend_in_another_currency_is_converted_before_it_is_compared()
    {
        // 25 UAH a day is under a dollar: nowhere near the baseline floor, so it is not "spend that used to flow".
        var account = AddAccount(Guid.NewGuid(), "UAH");
        Spend(account, Start, new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), 25m);
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        Assert.Empty(await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).ToListAsync());
    }

    [Fact]
    public async Task The_hole_closes_and_the_alert_resolves_when_spend_flows_again()
    {
        var userId = Guid.NewGuid();
        var account = AddAccount(userId);
        Spend(account, Start, new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), 25m);
        await _db.SaveChangesAsync();
        await Job().ExecuteAsync();

        // Three weeks later, with the week of 09-28 back to normal.
        Spend(account, new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), 25m);
        _db.SyncJobs.Add(new SyncJob(account.Id, userId)
        {
            Status = "success", StartedAt = Today.UtcDateTime.AddDays(15), CompletedAt = Today.UtcDateTime.AddDays(15),
        });
        await _db.SaveChangesAsync();
        _time.SetUtcNow(Today.AddDays(15)); // 2026-10-07

        await Job().ExecuteAsync();

        var hole = await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
        Assert.Equal(new DateOnly(2026, 9, 27), hole.To);
        _alerts.Verify(a => a.ResolveCoverageGapAlertAsync(userId, account.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_still_quiet_account_keeps_its_hole_open()
    {
        var userId = Guid.NewGuid();
        var account = AddAccount(userId);
        Spend(account, Start, new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), 25m);
        await _db.SaveChangesAsync();
        await Job().ExecuteAsync();

        await Job().ExecuteAsync();

        Assert.Null((await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync()).To);
        _alerts.Verify(a => a.ResolveCoverageGapAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
