namespace FinanceSentry.Tests.Integration.CryptoSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The holdings upsert is one <c>INSERT ... ON CONFLICT</c> on the unique (user, provider, asset) index:
/// repeating a key keeps one row, and two syncs racing on the same key do not fail on the index. The
/// in-memory provider enforces neither the index nor raw SQL, so this needs real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CryptoHoldingUpsertTests : IAsyncLifetime
{
    private const string Provider = CryptoExchangeProvider.Binance;

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

    private CryptoSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CryptoSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private async Task<List<CryptoHolding>> HoldingsAsync()
    {
        await using var ctx = CreateContext();
        return await ctx.CryptoHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(h => h.UserId == _user)
            .ToListAsync();
    }

    [DockerRequiredFact]
    public async Task A_repeated_upsert_of_one_key_keeps_one_row_and_updates_its_figures()
    {
        var first = CryptoHolding.Create(_user, Provider, "BTC", 1m, 0.5m, 100m);
        await using (var ctx = CreateContext())
        {
            await new CryptoHoldingRepository(ctx).UpsertRangeAsync([first]);
        }

        await using (var ctx = CreateContext())
        {
            await new CryptoHoldingRepository(ctx).UpsertRangeAsync(
                [CryptoHolding.Create(_user, Provider, "BTC", 2m, 0m, 250m)]);
        }

        var row = (await HoldingsAsync()).Should().ContainSingle().Subject;
        row.Id.Should().Be(first.Id, "the conflict updates the existing row");
        row.FreeQuantity.Should().Be(2m);
        row.LockedQuantity.Should().Be(0m);
        row.UsdValue.Should().Be(250m);
    }

    [DockerRequiredFact]
    public async Task An_upsert_reopens_a_closed_row_and_keeps_its_cost_basis_and_cursor()
    {
        await using (var ctx = CreateContext())
        {
            await new CryptoHoldingRepository(ctx).UpsertRangeAsync([CryptoHolding.Create(_user, Provider, "ETH", 3m, 0m, 900m)]);
        }

        await using (var ctx = CreateContext())
        {
            var holding = await ctx.CryptoHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync(h => h.UserId == _user);
            holding.SetCostBasis(500m, 166m, 12m, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 4);
            holding.AdvanceTradeCursor("cursor-1");
            holding.Close();
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            await new CryptoHoldingRepository(ctx).UpsertRangeAsync([CryptoHolding.Create(_user, Provider, "ETH", 1m, 0m, 300m)]);
        }

        var row = (await HoldingsAsync()).Should().ContainSingle().Subject;
        row.ClosedAt.Should().BeNull("a reappearing asset reopens its row");
        row.FreeQuantity.Should().Be(1m);
        row.CostBasisUsd.Should().Be(500m);
        row.TradeCursor.Should().Be("cursor-1");
        row.TradeCount.Should().Be(4);
    }

    [DockerRequiredFact]
    public async Task The_same_asset_on_two_providers_stays_two_rows()
    {
        await using (var ctx = CreateContext())
        {
            await new CryptoHoldingRepository(ctx).UpsertRangeAsync(
            [
                CryptoHolding.Create(_user, Provider, "BTC", 1m, 0m, 100m),
                CryptoHolding.Create(_user, CryptoExchangeProvider.RevolutX, "BTC", 2m, 0m, 200m),
            ]);
        }

        await using (var ctx = CreateContext())
        {
            await new CryptoHoldingRepository(ctx).UpsertRangeAsync([CryptoHolding.Create(_user, Provider, "BTC", 4m, 0m, 400m)]);
        }

        var rows = await HoldingsAsync();
        rows.Should().HaveCount(2);
        rows.Single(h => h.Provider == Provider).FreeQuantity.Should().Be(4m);
        rows.Single(h => h.Provider == CryptoExchangeProvider.RevolutX).FreeQuantity.Should().Be(2m);
    }

    [DockerRequiredFact]
    public async Task Concurrent_upserts_of_one_key_neither_fail_nor_duplicate()
    {
        var syncs = Enumerable.Range(1, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new CryptoHoldingRepository(ctx).UpsertRangeAsync(
                [CryptoHolding.Create(_user, Provider, "BTC", i, 0m, i * 100m)]);
        });

        await Task.WhenAll(syncs);

        (await HoldingsAsync()).Should().ContainSingle();
    }
}
