namespace FinanceSentry.Tests.Integration.BrokerageSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The holdings upsert is one <c>INSERT ... ON CONFLICT</c> on the unique (user, symbol, provider) index:
/// repeating a key keeps one row, and two syncs racing on the same key do not fail on the index. The
/// in-memory provider enforces neither the index nor raw SQL, so this needs real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BrokerageHoldingUpsertTests : IAsyncLifetime
{
    private readonly Guid _user = Guid.NewGuid();
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

    private BrokerageSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private async Task<List<BrokerageHolding>> HoldingsAsync()
    {
        await using var ctx = CreateContext();
        return await ctx.BrokerageHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(h => h.UserId == _user)
            .ToListAsync();
    }

    [DockerRequiredFact]
    public async Task A_repeated_upsert_of_one_key_keeps_one_row_and_updates_its_figures()
    {
        var instrument = new BrokerageInstrument(_user, "ibkr", 265598, "AAPL", "STK");
        await using (var seed = CreateContext())
        {
            seed.BrokerageInstruments.Add(instrument);
            await seed.SaveChangesAsync();
        }

        var instrumentId = instrument.Id;
        var first = new BrokerageHolding(_user, "AAPL", "STK", 2m, 400m, "ibkr", instrumentId: instrumentId);
        await using (var ctx = CreateContext())
        {
            await new BrokerageHoldingRepository(ctx).UpsertRangeAsync([first]);
        }

        var flexDate = new DateOnly(2026, 10, 9);
        await using (var ctx = CreateContext())
        {
            await new BrokerageHoldingRepository(ctx).UpsertRangeAsync(
                [new BrokerageHolding(_user, "AAPL", "STK", 5m, 1100m, "ibkr", flexAsOfDate: flexDate)]);
        }

        var row = (await HoldingsAsync()).Should().ContainSingle().Subject;
        row.Id.Should().Be(first.Id, "the conflict updates the existing row");
        row.Quantity.Should().Be(5m);
        row.UsdValue.Should().Be(1100m);
        row.FlexAsOfDate.Should().Be(flexDate);
        row.InstrumentId.Should().Be(instrumentId, "a sync without an instrument id never clears the link");
    }

    [DockerRequiredFact]
    public async Task An_upsert_recomputes_cost_basis_from_the_stored_average_cost()
    {
        await using (var ctx = CreateContext())
        {
            await new BrokerageHoldingRepository(ctx).UpsertRangeAsync(
                [new BrokerageHolding(_user, "MSFT", "STK", 2m, 800m, "ibkr", averageCostUsd: 100.5m)]);
        }

        await using (var ctx = CreateContext())
        {
            await new BrokerageHoldingRepository(ctx).UpsertRangeAsync(
                [new BrokerageHolding(_user, "MSFT", "STK", 4m, 1600m, "ibkr")]);
        }

        var row = (await HoldingsAsync()).Should().ContainSingle().Subject;
        row.AverageCostUsd.Should().Be(100.5m);
        row.CostBasisUsd.Should().Be(402m, "cost basis follows the new quantity while an average cost is known");
    }

    [DockerRequiredFact]
    public async Task One_batch_inserts_new_keys_and_updates_existing_ones()
    {
        await using (var ctx = CreateContext())
        {
            await new BrokerageHoldingRepository(ctx).UpsertRangeAsync(
                [new BrokerageHolding(_user, "AAPL", "STK", 1m, 100m, "ibkr")]);
        }

        await using (var ctx = CreateContext())
        {
            await new BrokerageHoldingRepository(ctx).UpsertRangeAsync(
            [
                new BrokerageHolding(_user, "AAPL", "STK", 3m, 300m, "ibkr"),
                new BrokerageHolding(_user, "AAPL", "STK", 9m, 900m, "inzhur"),
                new BrokerageHolding(_user, "NVDA", "STK", 7m, 700m, "ibkr"),
            ]);
        }

        var rows = await HoldingsAsync();
        rows.Should().HaveCount(3);
        rows.Single(h => h is { Symbol: "AAPL", Provider: "ibkr" }).Quantity.Should().Be(3m);
        rows.Single(h => h.Provider == "inzhur").Quantity.Should().Be(9m);
    }

    [DockerRequiredFact]
    public async Task Concurrent_upserts_of_one_key_neither_fail_nor_duplicate()
    {
        var syncs = Enumerable.Range(1, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new BrokerageHoldingRepository(ctx).UpsertRangeAsync(
                [new BrokerageHolding(_user, "AAPL", "STK", i, i * 100m, "ibkr")]);
        });

        await Task.WhenAll(syncs);

        (await HoldingsAsync()).Should().ContainSingle();
    }
}
