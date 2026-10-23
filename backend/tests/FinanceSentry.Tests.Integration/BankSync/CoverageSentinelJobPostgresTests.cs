namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The coverage sentinel against a real Postgres, with the real alert generator writing to a real Alerts database. The
/// unit tests run on the in-memory provider, which evaluates every predicate client-side and so cannot see a query EF Core
/// fails to translate to SQL; this one runs the job's queries as the relational provider compiles them and asserts the
/// persisted <c>CoverageGap</c> alert rows, not a mock's call log.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CoverageSentinelJobPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Today = new(2026, 9, 22, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Start = new(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime QuietFromSunday = new(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);

    // The test's own clock: a recovery test moves it to 2026-10-07, whose latest complete week is 09-28..10-04.
    private readonly SettableTimeProvider _time = new(Today);
    private readonly HashSet<Guid> _failingResolves = [];
    private readonly HashSet<Guid> _closedUsers = [];

    private TestDatabase? _bankDatabase;
    private TestDatabase? _alertsDatabase;
    private BankSyncDbContext _db = null!;
    private AlertsDbContext _alertsDb = null!;

    public async Task InitializeAsync()
    {
        _bankDatabase = await PostgresServer.Postgres14.ReserveDatabaseAsync();
        _alertsDatabase = await PostgresServer.Postgres14.CreateDatabaseAsync();

        _db = new BankSyncDbContext(
            new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_bankDatabase.ConnectionString).Options,
            NoCurrentUser.Instance);
        await _db.Database.MigrateAsync();

        _alertsDb = new AlertsDbContext(
            new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(_alertsDatabase.ConnectionString).Options,
            NoCurrentUser.Instance);
        await _alertsDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
        if (_alertsDb is not null)
            await _alertsDb.DisposeAsync();
        if (_bankDatabase is not null)
            await _bankDatabase.DisposeAsync();
        if (_alertsDatabase is not null)
            await _alertsDatabase.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task Revolut_shape_runs_against_postgres_and_stores_the_hole_and_raises_the_alert()
    {
        var userId = Guid.NewGuid();
        var account = AddAccount(userId, "USD");
        Spend(account, Start, QuietFromSunday, 25m); // $25 a day, then nothing: the first empty week is 09-07
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        var alert = Assert.Single(await GapAlertsAsync());
        Assert.Equal(userId, alert.UserId);
        Assert.Equal(account.Id, alert.ReferenceId);
        Assert.False(alert.IsResolved);
        Assert.Contains("Revolut ···3725", alert.Title);
        var hole = await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
        Assert.Equal(account.Id, hole.AccountId);
        Assert.Equal(new DateOnly(2026, 9, 7), hole.From);
        Assert.Null(hole.To);
    }

    [DockerRequiredFact]
    public async Task Spend_that_keeps_flowing_raises_no_alert_and_no_hole()
    {
        var account = AddAccount(Guid.NewGuid(), "USD");
        Spend(account, Start, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc), 25m); // through the latest complete week
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        Assert.Empty(await GapAlertsAsync());
        Assert.Empty(await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).ToListAsync());
    }

    [DockerRequiredFact]
    public async Task Syncs_that_are_not_succeeding_are_a_different_alert_not_a_coverage_gap()
    {
        var account = AddAccount(Guid.NewGuid(), "USD", recentSuccessfulSync: false);
        Spend(account, Start, QuietFromSunday, 25m);
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        Assert.Empty(await GapAlertsAsync());
        Assert.Empty(await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).ToListAsync());
    }

    [DockerRequiredFact]
    public async Task The_hole_closes_and_the_alert_resolves_after_two_recovered_weeks()
    {
        var userId = Guid.NewGuid();
        var account = await OpenGapAsync(userId);

        // The weeks of 09-21 and 09-28 are both back to normal.
        await RecoverAsync(account, new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc));
        await Job().ExecuteAsync();

        var hole = await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2026, 9, 20), hole.To);
        var alert = Assert.Single(await GapAlertsAsync());
        Assert.True(alert.IsResolved);
    }

    [DockerRequiredFact]
    public async Task One_stray_purchase_week_does_not_close_the_hole_or_resolve_the_alert()
    {
        var userId = Guid.NewGuid();
        var account = await OpenGapAsync(userId);

        // Only the week of 09-28 is back to normal; the week of 09-21 stayed quiet.
        await RecoverAsync(account, new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));
        await Job().ExecuteAsync();

        Assert.Null((await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync()).To);
        Assert.False(Assert.Single(await GapAlertsAsync()).IsResolved);
    }

    [DockerRequiredFact]
    public async Task A_failing_resolve_leaves_the_hole_open_and_the_next_run_retries_it()
    {
        var userId = Guid.NewGuid();
        var account = await OpenGapAsync(userId);
        await RecoverAsync(account, new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc));
        _failingResolves.Add(account.Id);

        await Job().ExecuteAsync();

        Assert.Null((await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync()).To);
        Assert.False(Assert.Single(await GapAlertsAsync()).IsResolved);

        _failingResolves.Clear();
        await Job().ExecuteAsync();

        Assert.Equal(new DateOnly(2026, 9, 20), (await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync()).To);
        Assert.True(Assert.Single(await GapAlertsAsync()).IsResolved);
    }

    [DockerRequiredFact]
    public async Task One_accounts_failure_does_not_drop_another_accounts_close()
    {
        var failingUser = Guid.NewGuid();
        var failing = AddAccount(failingUser, "USD");
        var retiredUser = Guid.NewGuid();
        var retired = AddAccount(retiredUser, "USD");
        Spend(failing, Start, QuietFromSunday, 25m);
        Spend(retired, Start, QuietFromSunday, 25m);
        await _db.SaveChangesAsync();
        await Job().ExecuteAsync();
        Assert.Equal(2, (await GapAlertsAsync()).Count);

        // The first account's spend is back but resolving its alert fails; the second account is retired, so its hole
        // closes in the same run, after that failure cleared the change tracker.
        await RecoverAsync(failing, new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc));
        retired.Retire(Today, "closed by the bank");
        await _db.SaveChangesAsync();
        _failingResolves.Add(failing.Id);

        await Job().ExecuteAsync();

        var holes = await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().ToListAsync();
        Assert.Null(holes.Single(h => h.AccountId == failing.Id).To);
        Assert.Equal(new DateOnly(2026, 10, 4), holes.Single(h => h.AccountId == retired.Id).To);
        var alerts = await GapAlertsAsync();
        Assert.False(alerts.Single(a => a.ReferenceId == failing.Id).IsResolved);
        Assert.True(alerts.Single(a => a.ReferenceId == retired.Id).IsResolved);
    }

    [DockerRequiredFact]
    public Task A_hole_closes_and_its_alert_resolves_once_the_account_is_closed() => AssertHoleClosesWhenUnwatchedAsync(closeThePerson: false);

    [DockerRequiredFact]
    public Task A_hole_closes_and_its_alert_resolves_once_the_person_is_closed() => AssertHoleClosesWhenUnwatchedAsync(closeThePerson: true);

    private async Task AssertHoleClosesWhenUnwatchedAsync(bool closeThePerson)
    {
        var userId = Guid.NewGuid();
        var account = await OpenGapAsync(userId);

        if (closeThePerson)
            _closedUsers.Add(userId);
        else
        {
            account.Retire(Today, "closed by the bank");
            await _db.SaveChangesAsync();
        }

        await Job().ExecuteAsync();
        await Job().ExecuteAsync(); // a second run finds nothing more to do

        // Today's latest complete week is 09-14..09-20: the hole ends with it.
        Assert.Equal(new DateOnly(2026, 9, 20), (await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync()).To);
        Assert.True(Assert.Single(await GapAlertsAsync()).IsResolved);
    }

    [DockerRequiredFact]
    public async Task A_second_run_stacks_no_second_hole_or_alert()
    {
        var userId = Guid.NewGuid();
        var account = await OpenGapAsync(userId);

        await Job().ExecuteAsync();

        Assert.Equal(1, await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).CountAsync());
        var alert = Assert.Single(await GapAlertsAsync());
        Assert.Equal(account.Id, alert.ReferenceId);
        Assert.False(alert.IsResolved);
    }

    [DockerRequiredFact]
    public async Task Spend_in_another_currency_is_converted_before_it_is_compared()
    {
        // 25 UAH a day is under a dollar: converted, nowhere near the baseline floor. Unconverted it would be $25 a day.
        var uah = AddAccount(Guid.NewGuid(), "UAH");
        // The same 25 a day in EUR is real spend in dollars, so its silence is a gap.
        var eur = AddAccount(Guid.NewGuid(), "EUR");
        Spend(uah, Start, QuietFromSunday, 25m);
        Spend(eur, Start, QuietFromSunday, 25m);
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        var alert = Assert.Single(await GapAlertsAsync());
        Assert.Equal(eur.Id, alert.ReferenceId);
    }

    [DockerRequiredFact]
    public async Task Only_day_to_day_categories_count_as_the_spend_that_stopped()
    {
        var rentOnly = AddAccount(Guid.NewGuid(), "USD");
        // Day-to-day spend stopped on this one while a monthly category keeps flowing: still a gap.
        var mixed = AddAccount(Guid.NewGuid(), "USD");
        Spend(rentOnly, Start, QuietFromSunday, 25m, "RENT_AND_UTILITIES");
        Spend(mixed, Start, QuietFromSunday, 25m);
        Spend(mixed, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc), 25m, "RENT_AND_UTILITIES");
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync();

        var alert = Assert.Single(await GapAlertsAsync());
        Assert.Equal(mixed.Id, alert.ReferenceId);
    }

    /// <summary>A Revolut-shaped account whose gap is open and alerted as of <see cref="Today"/>.</summary>
    private async Task<BankAccount> OpenGapAsync(Guid userId)
    {
        var account = AddAccount(userId, "USD");
        Spend(account, Start, QuietFromSunday, 25m);
        await _db.SaveChangesAsync();
        await Job().ExecuteAsync();
        Assert.Null((await _db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync()).To);
        Assert.False(Assert.Single(await GapAlertsAsync()).IsResolved);
        return account;
    }

    /// <summary>Spend back to normal from <paramref name="from"/> through 2026-10-04, a fresh successful sync, and the clock at 2026-10-07.</summary>
    private async Task RecoverAsync(BankAccount account, DateTime from)
    {
        Spend(account, from, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), 25m);
        var syncedAt = Today.UtcDateTime.AddDays(15);
        _db.SyncJobs.Add(new SyncJob(account.Id, account.UserId) { Status = "success", StartedAt = syncedAt, CompletedAt = syncedAt });
        await _db.SaveChangesAsync();
        _time.Set(Today.AddDays(15));
    }

    private CoverageSentinelJob Job()
    {
        var real = new AlertGeneratorService(new AlertRepository(_alertsDb), new Mock<IPolicyAckReader>().Object);
        var alerts = new Mock<IAlertGeneratorService>();
        alerts.Setup(a => a.GenerateCoverageGapAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns((Guid u, Guid a, string n, int w, decimal b, CancellationToken ct) => real.GenerateCoverageGapAlertAsync(u, a, n, w, b, ct));
        alerts.Setup(a => a.ResolveCoverageGapAlertAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid u, Guid a, CancellationToken ct) => _failingResolves.Contains(a)
                ? throw new InvalidOperationException("alerts down")
                : real.ResolveCoverageGapAlertAsync(u, a, ct));

        var closedAccounts = new Mock<IClosedAccountsReader>();
        closedAccounts.Setup(c => c.GetClosedUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _closedUsers.ToHashSet());
        return new CoverageSentinelJob(_db, closedAccounts.Object, alerts.Object, _time, NullLogger<CoverageSentinelJob>.Instance);
    }

    private async Task<List<Alert>> GapAlertsAsync()
    {
        _alertsDb.ChangeTracker.Clear();
        return await _alertsDb.Alerts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(a => a.Type == AlertType.CoverageGap)
            .ToListAsync();
    }

    private BankAccount AddAccount(Guid userId, string currency, bool recentSuccessfulSync = true)
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

    private sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Set(DateTimeOffset value) => _now = value;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
