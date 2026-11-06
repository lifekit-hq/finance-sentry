namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

/// <summary>
/// The open-hole repair against real Postgres: the dry run writes nothing (not even the backup table), apply backs the
/// original row up before moving the start, a second apply changes nothing and keeps the original in the backup, and only
/// an open hole whose start is later than its first quiet week is touched. Every figure here is synthetic.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CoverageHoleStartRepairPostgresTests : IAsyncLifetime
{
    private static readonly DateTime Start = new(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime QuietFromSunday = new(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly FirstQuietWeek = new(2026, 9, 7);
    private static readonly DateOnly RecordedFrom = new(2026, 10, 5);
    private const decimal UsualWeek = 175m;

    private TestDatabase? _database;
    private BankSyncDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres14.ReserveDatabaseAsync();
        _db = new BankSyncDbContext(
            new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database.ConnectionString).Options,
            NoCurrentUser.Instance);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task A_dry_run_reports_the_move_and_writes_nothing()
    {
        var hole = await SeedLateHoleAsync();

        var report = await RunAsync(accountId: null, apply: false);

        var row = Assert.Single(report.Rows);
        Assert.Equal(CoverageHoleRepairStatus.Pending, row.Status);
        Assert.Equal(RecordedFrom, row.OldFrom);
        Assert.Equal(FirstQuietWeek, row.NewFrom);
        Assert.False(report.Applied);
        Assert.Equal(RecordedFrom, (await HoleAsync(hole)).From);
        Assert.False(await BackupExistsAsync());
    }

    [DockerRequiredFact]
    public async Task Apply_backs_the_original_up_then_moves_the_start_and_a_second_apply_changes_nothing()
    {
        var hole = await SeedLateHoleAsync();

        var first = await RunAsync(accountId: null, apply: true);
        var second = await RunAsync(accountId: null, apply: true);

        Assert.True(first.Applied);
        Assert.Equal(1, first.Pending);
        Assert.Equal(FirstQuietWeek, (await HoleAsync(hole)).From);
        Assert.Null((await HoleAsync(hole)).To);
        Assert.False(second.Applied);
        Assert.Equal(CoverageHoleRepairStatus.Unchanged, Assert.Single(second.Rows).Status);

        var backups = await BackedUpFromsAsync();
        Assert.Equal([RecordedFrom], backups); // the original start, kept once however often it runs
    }

    [DockerRequiredFact]
    public async Task A_hole_already_at_its_first_quiet_week_and_a_closed_hole_are_left_alone()
    {
        var userId = Guid.NewGuid();
        var atStart = AddAccount(userId);
        Spend(atStart, Start, QuietFromSunday);
        var atStartHole = AddHole(atStart, FirstQuietWeek, to: null);

        var closedAccount = AddAccount(userId);
        Spend(closedAccount, Start, QuietFromSunday);
        var closedHole = AddHole(closedAccount, RecordedFrom, to: new DateOnly(2026, 10, 18));
        await _db.SaveChangesAsync();

        var report = await RunAsync(accountId: null, apply: true);

        var row = Assert.Single(report.Rows);
        Assert.Equal(atStartHole.Id, row.HoleId);
        Assert.Equal(CoverageHoleRepairStatus.Unchanged, row.Status);
        Assert.Equal(FirstQuietWeek, (await HoleAsync(atStartHole.Id)).From);
        Assert.Equal(RecordedFrom, (await HoleAsync(closedHole.Id)).From);
        Assert.False(await BackupExistsAsync());
    }

    [DockerRequiredFact]
    public async Task The_account_option_limits_the_run_to_that_accounts_hole()
    {
        var userId = Guid.NewGuid();
        var first = AddAccount(userId);
        var second = AddAccount(userId);
        foreach (var account in new[] { first, second })
        {
            Spend(account, Start, QuietFromSunday);
            Spend(account, new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc));
        }

        var firstHole = AddHole(first, RecordedFrom, to: null);
        var secondHole = AddHole(second, RecordedFrom, to: null);
        await _db.SaveChangesAsync();

        await RunAsync(first.Id, apply: true);

        Assert.Equal(FirstQuietWeek, (await HoleAsync(firstHole.Id)).From);
        Assert.Equal(RecordedFrom, (await HoleAsync(secondHole.Id)).From);
    }

    // The shape the sentinel left behind: spend of 25 a day to 09-06, two purchases in the week of 09-14 (a stray week), and
    // an open hole recorded from 10-05, weeks after the first quiet week 09-07.
    private async Task<Guid> SeedLateHoleAsync()
    {
        var account = AddAccount(Guid.NewGuid());
        Spend(account, Start, QuietFromSunday);
        Spend(account, new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc));
        var hole = AddHole(account, RecordedFrom, to: null);
        await _db.SaveChangesAsync();
        return hole.Id;
    }

    private async Task<CoverageHoleRepairReport> RunAsync(Guid? accountId, bool apply)
    {
        await using var db = new BankSyncDbContext(
            new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            NoCurrentUser.Instance);
        return await new CoverageHoleStartRepairService(db).RepairAsync(accountId, apply);
    }

    private async Task<CoverageHole> HoleAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        return await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync(h => h.Id == id);
    }

    private CoverageHole AddHole(BankAccount account, DateOnly from, DateOnly? to)
    {
        var hole = new CoverageHole
        {
            UserId = account.UserId,
            AccountId = account.Id,
            From = from,
            To = to,
            BaselineWeeklySpendUsd = UsualWeek,
        };
        _db.CoverageHoles.Add(hole);
        return hole;
    }

    private BankAccount AddAccount(Guid userId)
    {
        var account = new BankAccount(userId, Guid.NewGuid().ToString(), "Revolut", "current", "3725", "Owner", "USD",
            Guid.NewGuid(), "truelayer");
        _db.BankAccounts.Add(account);
        return account;
    }

    private void Spend(BankAccount account, DateTime from, DateTime to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var tx = new Transaction(account.Id, account.UserId, 25m, d.AddHours(12), "card", Guid.NewGuid().ToString())
            {
                MerchantCategory = "FOOD_AND_DRINK",
                TransactionType = "debit",
                IsActive = true,
            };
            tx.ValidateInvariants();
            _db.Transactions.Add(tx);
        }
    }

    private async Task<bool> BackupExistsAsync()
    {
        await using var connection = new NpgsqlConnection(_database!.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('bank_sync." + CoverageHoleStartRepairService.BackupTable + "') IS NOT NULL", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<DateOnly>> BackedUpFromsAsync()
    {
        await using var connection = new NpgsqlConnection(_database!.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"From\" FROM bank_sync." + CoverageHoleStartRepairService.BackupTable + " ORDER BY \"From\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var froms = new List<DateOnly>();
        while (await reader.ReadAsync())
            froms.Add(reader.GetFieldValue<DateOnly>(0));
        return froms;
    }
}
