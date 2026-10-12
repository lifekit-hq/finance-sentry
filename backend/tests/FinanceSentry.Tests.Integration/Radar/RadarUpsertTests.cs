namespace FinanceSentry.Tests.Integration.Radar;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The daily-bar and universe-member upserts are each one <c>INSERT ... ON CONFLICT</c> on a unique index
/// (<c>idx_daily_bars_ticker_date</c>, <c>idx_radar_universe_ticker</c>): repeating a key keeps one row, and
/// overlapping writers do not fail on the index. The in-memory provider enforces neither the index nor raw SQL,
/// so this needs real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RadarUpsertTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 10, 9);
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private RadarDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RadarDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            NoCurrentUser.Instance);

    private static DailyBar Bar(string ticker, DateOnly date, decimal price, long volume = 1000) => new()
    {
        Ticker = ticker, Date = date, Open = price, High = price + 1, Low = price - 1, Close = price,
        AdjClose = price, Volume = volume,
    };

    private static RadarUniverseMember Member(string ticker, UniverseKind kind, UniverseSource source) => new()
    {
        Ticker = ticker, Kind = kind, Source = source,
    };

    private async Task<List<DailyBar>> BarsAsync()
    {
        await using var ctx = CreateContext();
        return await ctx.DailyBars.AsNoTracking().ToListAsync();
    }

    private async Task<List<RadarUniverseMember>> MembersAsync()
    {
        await using var ctx = CreateContext();
        return await ctx.UniverseMembers.AsNoTracking().ToListAsync();
    }

    [DockerRequiredFact]
    public async Task A_repeated_bar_upsert_keeps_one_row_per_day_and_overwrites_its_figures()
    {
        await using (var ctx = CreateContext())
        {
            (await new DailyBarRepository(ctx).UpsertRangeAsync([Bar("AAPL", Day, 100m)])).Should().Be(1);
        }

        // A retroactive split rescale: same key, new prices.
        await using (var ctx = CreateContext())
        {
            (await new DailyBarRepository(ctx).UpsertRangeAsync([Bar("AAPL", Day, 50m, volume: 2000)]))
                .Should().Be(0, "the key already existed, so nothing was added");
        }

        var row = (await BarsAsync()).Should().ContainSingle().Subject;
        row.AdjClose.Should().Be(50m);
        row.Close.Should().Be(50m);
        row.Volume.Should().Be(2000);
    }

    [DockerRequiredFact]
    public async Task One_bar_batch_inserts_new_days_updates_existing_ones_and_counts_only_the_new()
    {
        await using (var ctx = CreateContext())
        {
            await new DailyBarRepository(ctx).UpsertRangeAsync([Bar("AAPL", Day, 100m)]);
        }

        await using (var ctx = CreateContext())
        {
            var added = await new DailyBarRepository(ctx).UpsertRangeAsync(
            [
                Bar("AAPL", Day, 110m),
                Bar("AAPL", Day.AddDays(1), 111m),
                Bar("MSFT", Day, 300m),
                Bar("MSFT", Day, 999m), // a repeat inside the batch keeps its first bar
            ]);

            added.Should().Be(2);
        }

        var rows = await BarsAsync();
        rows.Should().HaveCount(3);
        rows.Single(b => b is { Ticker: "AAPL" } && b.Date == Day).AdjClose.Should().Be(110m);
        rows.Single(b => b.Ticker == "MSFT").AdjClose.Should().Be(300m);
    }

    [DockerRequiredFact]
    public async Task An_empty_bar_batch_adds_nothing()
    {
        await using var ctx = CreateContext();

        (await new DailyBarRepository(ctx).UpsertRangeAsync([])).Should().Be(0);
    }

    [DockerRequiredFact]
    public async Task Concurrent_bar_upserts_of_one_key_neither_fail_nor_duplicate()
    {
        var writers = Enumerable.Range(1, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new DailyBarRepository(ctx).UpsertRangeAsync([Bar("AAPL", Day, i * 10m)]);
        });

        await Task.WhenAll(writers);

        (await BarsAsync()).Should().ContainSingle();
    }

    [DockerRequiredFact]
    public async Task A_repeated_member_upsert_keeps_one_row_reactivates_it_and_takes_the_new_kind_and_source()
    {
        await using (var ctx = CreateContext())
        {
            var repo = new RadarUniverseRepository(ctx);
            await repo.UpsertMembersAsync([Member("AAPL", UniverseKind.Watchlist, UniverseSource.Auto)]);
            await repo.DeactivateAsync(["AAPL"]);
        }

        await using (var ctx = CreateContext())
        {
            await new RadarUniverseRepository(ctx).UpsertMembersAsync(
                [Member("AAPL", UniverseKind.Holding, UniverseSource.Seed)]);
        }

        var row = (await MembersAsync()).Should().ContainSingle().Subject;
        row.Active.Should().BeTrue();
        row.Kind.Should().Be(UniverseKind.Holding);
        row.Source.Should().Be(UniverseSource.Seed);
    }

    [DockerRequiredFact]
    public async Task One_member_batch_inserts_new_tickers_and_updates_existing_ones()
    {
        await using (var ctx = CreateContext())
        {
            await new RadarUniverseRepository(ctx).UpsertMembersAsync(
                [Member("AAPL", UniverseKind.Watchlist, UniverseSource.Auto)]);
        }

        await using (var ctx = CreateContext())
        {
            await new RadarUniverseRepository(ctx).UpsertMembersAsync(
            [
                Member("AAPL", UniverseKind.Holding, UniverseSource.Auto),
                Member("NVDA", UniverseKind.Watchlist, UniverseSource.Auto),
            ]);
        }

        var rows = await MembersAsync();
        rows.Should().HaveCount(2);
        rows.Single(m => m.Ticker == "AAPL").Kind.Should().Be(UniverseKind.Holding);
    }

    [DockerRequiredFact]
    public async Task Concurrent_member_upserts_of_one_ticker_neither_fail_nor_duplicate()
    {
        var writers = Enumerable.Range(1, 8).Select(async _ =>
        {
            await using var ctx = CreateContext();
            await new RadarUniverseRepository(ctx).UpsertMembersAsync(
                [Member("AAPL", UniverseKind.Holding, UniverseSource.Auto)]);
        });

        await Task.WhenAll(writers);

        (await MembersAsync()).Should().ContainSingle();
    }
}
