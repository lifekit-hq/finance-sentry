namespace FinanceSentry.Tests.Integration.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

/// <summary>
/// The July 2026 history repair (numbers report R1) against real Postgres: the dry run writes nothing (not even the
/// backup tables), apply backs the originals up before rewriting, a second apply changes nothing, only the named
/// person's rows are touched, and apply refuses when a planned day is missing or holds an unexpected total.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetWorthHistoryRepairTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private const decimal CryptoSleeve = 500m;
    private const decimal FrozenBrokerage = 9_000m;
    private const decimal CreditLimitCash = 100m;

    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(Owner);
        await SeedAsync(Other);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // The verb runs with no person in scope: the Owner filter matches nothing unless the service opts out.
    private WealthDbContext Context() =>
        new(new DbContextOptionsBuilder<WealthDbContext>().UseNpgsql(_database!.ConnectionString).Options, new FixedCurrentUser(null));

    private async Task<NetWorthHistoryRepairReport> RunAsync(Guid user, bool apply)
    {
        await using var db = Context();
        return await new NetWorthHistoryRepairService(db).RepairAsync(user, apply);
    }

    // A row as the plan expects to find it: stored total = ExpectedTotal, banking carrying the credit limit. A day whose
    // brokerage the plan lowers holds a frozen quote; the others hold a small sleeve (or $0 on the days it was lost).
    private static NetWorthSnapshot SeedRow(Guid user, NetWorthRepairDay day)
    {
        var brokerage = !day.BrokerageRederived ? 1_000m : day.BrokerageDelta < 0m ? FrozenBrokerage : 0m;
        var invested = brokerage == 0m ? 0m : brokerage - 100m;
        var banking = day.ExpectedTotal - brokerage - CryptoSleeve;
        return new NetWorthSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = user,
            SnapshotDate = day.Date,
            BankingTotal = banking,
            BrokerageTotal = brokerage,
            CryptoTotal = CryptoSleeve,
            TotalNetWorth = day.ExpectedTotal,
            Currency = "USD",
            TakenAt = new DateTimeOffset(day.Date.ToDateTime(new TimeOnly(7, 0)), TimeSpan.Zero),
            StaleSleeves = day.BrokerageRederived && brokerage == 0m ? "brokerage" : null,
            CashTotal = banking + (brokerage - invested),
            BrokerageInvested = invested,
            CryptoInvested = CryptoSleeve,
            FxStatus = FxStatus.Exact,
        };
    }

    private async Task SeedAsync(Guid user)
    {
        await using var db = Context();
        foreach (var day in JulyNetWorthHistoryRepairPlan.Days)
        {
            var row = SeedRow(user, day);
            db.NetWorthSnapshots.Add(row);
            db.NetWorthSnapshotSources.AddRange(
                new NetWorthSnapshotSource { SnapshotId = row.Id, SourceId = "banking", Kind = EconomyKind.Bank, Total = row.BankingTotal, Cash = row.BankingTotal, Invested = 0m, Health = SourceHealth.Complete },
                new NetWorthSnapshotSource { SnapshotId = row.Id, SourceId = "brokerage", Kind = EconomyKind.Brokerage, Total = row.BrokerageTotal, Cash = row.BrokerageTotal - row.BrokerageInvested, Invested = row.BrokerageInvested, Health = SourceHealth.Complete },
                new NetWorthSnapshotSource { SnapshotId = row.Id, SourceId = "crypto", Kind = EconomyKind.CryptoVenue, Total = CryptoSleeve, Cash = 0m, Invested = CryptoSleeve, Health = SourceHealth.Complete });
        }

        await db.SaveChangesAsync();
    }

    private async Task<List<NetWorthSnapshot>> RowsAsync(Guid user)
    {
        await using var db = Context();
        return await db.NetWorthSnapshots.IgnoreQueryFilters([FinanceSentry.Core.Auth.OwnerQueryFilter.Name])
            .AsNoTracking().Where(s => s.UserId == user).OrderBy(s => s.SnapshotDate).ToListAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private Task<bool> BackupExistsAsync(string table) => ScalarAsync<bool>($"SELECT to_regclass('public.{table}') IS NOT NULL");

    private static void ShouldMatch(IEnumerable<NetWorthSnapshot> before, IEnumerable<NetWorthSnapshot> after)
        => after.Should().BeEquivalentTo(before);

    [DockerRequiredFact]
    public async Task DryRun_ReportsEveryRowOldAgainstNew_AndWritesNothing()
    {
        var before = await RowsAsync(Owner);

        var report = await RunAsync(Owner, apply: false);

        report.Applied.Should().BeFalse();
        report.Pending.Should().Be(61);
        report.Rows.Should().OnlyContain(r => r.NewTotal != r.OldTotal && r.OldTotal != null);
        report.Rows.Select(r => r.NewTotal).Should().BeEquivalentTo(JulyNetWorthHistoryRepairPlan.Days.Select(d => (decimal?)d.ProposedTotal));
        ShouldMatch(before, await RowsAsync(Owner));
        (await BackupExistsAsync(JulyNetWorthHistoryRepairPlan.BackupTable)).Should().BeFalse();
        (await BackupExistsAsync(JulyNetWorthHistoryRepairPlan.SourcesBackupTable)).Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Apply_BacksTheOriginalsUpThenRewritesTheRows()
    {
        var before = await RowsAsync(Owner);

        var report = await RunAsync(Owner, apply: true);

        report.Applied.Should().BeTrue();
        var after = await RowsAsync(Owner);
        after.Select(s => s.TotalNetWorth).Should().Equal(JulyNetWorthHistoryRepairPlan.Days.OrderBy(d => d.Date).Select(d => d.ProposedTotal));
        after.Should().OnlyContain(s => s.BankingTotal + s.BrokerageTotal + s.CryptoTotal == s.TotalNetWorth);
        after.Should().OnlyContain(s => s.CashTotal + s.BrokerageInvested + s.CryptoInvested == s.TotalNetWorth);

        (await ScalarAsync<long>($"SELECT count(*) FROM public.{JulyNetWorthHistoryRepairPlan.BackupTable}")).Should().Be(61);
        (await ScalarAsync<long>($"SELECT count(*) FROM public.{JulyNetWorthHistoryRepairPlan.SourcesBackupTable}")).Should().Be(61 * 3);
        // The backup holds the originals, row for row.
        foreach (var original in before)
        {
            (await ScalarAsync<decimal>(
                $"SELECT \"TotalNetWorth\" FROM public.{JulyNetWorthHistoryRepairPlan.BackupTable} WHERE \"Id\" = '{original.Id}'"))
                .Should().Be(original.TotalNetWorth);
        }
    }

    [DockerRequiredFact]
    public async Task Apply_FlagsTheBrokerageEpisodeDaysAndOnlyThose()
    {
        await RunAsync(Owner, apply: true);

        await using var db = Context();
        var rederived = JulyNetWorthHistoryRepairPlan.Days.Where(d => d.BrokerageRederived).Select(d => d.Date).ToHashSet();
        var after = await RowsAsync(Owner);
        foreach (var row in after)
        {
            var isEpisode = rederived.Contains(row.SnapshotDate);
            row.IsApproximate.Should().Be(isEpisode, because: row.SnapshotDate.ToString("O"));
            row.FxStatus.Should().Be(isEpisode ? FxStatus.TodayRate : FxStatus.Exact);
            (row.StaleSleeves?.Contains("brokerage") ?? false).Should().Be(isEpisode);
        }

        var episodeIds = after.Where(r => rederived.Contains(r.SnapshotDate)).Select(r => r.Id).ToList();
        var brokerageSources = await db.NetWorthSnapshotSources.IgnoreQueryFilters([FinanceSentry.Core.Auth.OwnerQueryFilter.Name])
            .Where(x => episodeIds.Contains(x.SnapshotId) && x.SourceId == "brokerage").ToListAsync();
        brokerageSources.Should().HaveCount(10);
        brokerageSources.Should().OnlyContain(x => x.Health == SourceHealth.Estimated
            && x.Reasons.Contains(SourceReasons.EstimatedValue) && x.Reasons.Contains(SourceReasons.StaleSource) && x.Reasons.Contains(FxReasons.TodayRate));
        // Each source row follows its snapshot's column.
        foreach (var row in after)
        {
            var sources = await db.NetWorthSnapshotSources.IgnoreQueryFilters([FinanceSentry.Core.Auth.OwnerQueryFilter.Name])
                .Where(x => x.SnapshotId == row.Id).ToDictionaryAsync(x => x.SourceId);
            sources["banking"].Total.Should().Be(row.BankingTotal);
            sources["brokerage"].Total.Should().Be(row.BrokerageTotal);
        }
    }

    [DockerRequiredFact]
    public async Task Apply_Twice_ChangesNothingTheSecondTime_AndKeepsTheOriginalBackup()
    {
        await RunAsync(Owner, apply: true);
        var afterFirst = await RowsAsync(Owner);

        var second = await RunAsync(Owner, apply: true);

        second.Applied.Should().BeFalse();
        second.Pending.Should().Be(0);
        second.AlreadyRepaired.Should().Be(61);
        ShouldMatch(afterFirst, await RowsAsync(Owner));
        (await ScalarAsync<long>($"SELECT count(*) FROM public.{JulyNetWorthHistoryRepairPlan.BackupTable}")).Should().Be(61);
        // Still the originals, not the repaired figures.
        (await ScalarAsync<long>(
            $"SELECT count(*) FROM public.{JulyNetWorthHistoryRepairPlan.BackupTable} b JOIN public.net_worth_snapshots s ON s.\"Id\" = b.\"Id\" WHERE b.\"TotalNetWorth\" = s.\"TotalNetWorth\""))
            .Should().Be(0);
    }

    [DockerRequiredFact]
    public async Task Apply_DryRunAfterApply_ReportsEveryRowAlreadyRepaired()
    {
        await RunAsync(Owner, apply: true);

        var report = await RunAsync(Owner, apply: false);

        report.Pending.Should().Be(0);
        report.AlreadyRepaired.Should().Be(61);
        report.Blockers.Should().BeEmpty();
    }

    [DockerRequiredFact]
    public async Task Apply_TouchesOnlyTheNamedPersonsRows()
    {
        var otherBefore = await RowsAsync(Other);

        var report = await RunAsync(Owner, apply: true);

        report.UserId.Should().Be(Owner);
        ShouldMatch(otherBefore, await RowsAsync(Other));
        (await ScalarAsync<long>(
            $"SELECT count(*) FROM public.{JulyNetWorthHistoryRepairPlan.BackupTable} WHERE \"UserId\" = '{Other}'")).Should().Be(0);
    }

    [DockerRequiredFact]
    public async Task Apply_WhenARowHoldsAnUnexpectedTotal_RefusesAndChangesNothing()
    {
        await ExecuteAsync("UPDATE public.net_worth_snapshots SET \"TotalNetWorth\" = \"TotalNetWorth\" + 1 "
            + $"WHERE \"UserId\" = '{Owner}' AND \"SnapshotDate\" = DATE '2026-07-09'");
        var before = await RowsAsync(Owner);

        var dry = await RunAsync(Owner, apply: false);
        var apply = () => RunAsync(Owner, apply: true);

        dry.Blockers.Should().ContainSingle().Which.Should().Match<NetWorthRepairRow>(
            r => r.Date == new DateOnly(2026, 7, 9) && r.Status == NetWorthRepairRowStatus.Unexpected);
        (await apply.Should().ThrowAsync<NetWorthHistoryRepairRefusedException>()).Which.Blockers.Should().ContainSingle();
        ShouldMatch(before, await RowsAsync(Owner));
        (await BackupExistsAsync(JulyNetWorthHistoryRepairPlan.BackupTable)).Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Apply_WhenAPlannedDayIsMissing_RefusesAndChangesNothing()
    {
        await ExecuteAsync($"DELETE FROM public.net_worth_snapshots WHERE \"UserId\" = '{Owner}' AND \"SnapshotDate\" = DATE '2026-08-01'");
        var before = await RowsAsync(Owner);

        var apply = () => RunAsync(Owner, apply: true);

        (await apply.Should().ThrowAsync<NetWorthHistoryRepairRefusedException>())
            .Which.Blockers.Should().ContainSingle().Which.Status.Should().Be(NetWorthRepairRowStatus.Missing);
        ShouldMatch(before, await RowsAsync(Owner));
    }

    [DockerRequiredFact]
    public async Task Apply_WhenThePersonHasNoSnapshots_IsANoOpDryRunButRefusesToApply()
    {
        var stranger = Guid.NewGuid();

        var dry = await RunAsync(stranger, apply: false);
        var apply = () => RunAsync(stranger, apply: true);

        dry.Rows.Should().OnlyContain(r => r.Status == NetWorthRepairRowStatus.Missing);
        await apply.Should().ThrowAsync<NetWorthHistoryRepairRefusedException>();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
