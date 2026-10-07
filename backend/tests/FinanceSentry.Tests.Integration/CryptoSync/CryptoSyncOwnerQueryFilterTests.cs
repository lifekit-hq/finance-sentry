namespace FinanceSentry.Tests.Integration.CryptoSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="CryptoSyncDbContext"/>: a context acting for one person sees only
/// that person's rows, a context with no person in scope sees none, and the scheduled-sync repositories and
/// the key rotation (which run with no person in scope) opt out explicitly, so they neither see nothing nor
/// re-insert rows they could not find. Real Postgres is required, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CryptoSyncOwnerQueryFilterTests : IAsyncLifetime
{
    private const string Provider = CryptoExchangeProvider.Binance;

    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
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

    // Null acts as a background job: no person in scope.
    private CryptoSyncDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<CryptoSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static CryptoHolding NewHolding(Guid userId, decimal free = 1m) =>
        CryptoHolding.Create(userId, Provider, "BTC", free, 0m, free * 100m);

    private static ExchangeCredential NewCredential(Guid userId) =>
        ExchangeCredential.Create(userId, Provider, [1], [1], [1], [1], [1], [1], 1);

    private async Task SeedAsync(params object[] entities)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    private async Task<List<CryptoHolding>> AllHoldingsAsync()
    {
        await using var read = CreateContext();
        return await read.CryptoHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(h => h.UserId == _userA || h.UserId == _userB)
            .ToListAsync();
    }

    [Fact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes().Where(e => e.FindProperty("UserId") is not null).ToList();

        perUser.Should().HaveCount(3);
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_holdings_and_no_person_sees_none()
    {
        var a = NewHolding(_userA);
        var b = NewHolding(_userB);
        await SeedAsync(a, b);

        await using (var asA = CreateContext(_userA))
            (await asA.CryptoHoldings.Select(x => x.Id).ToListAsync()).Should().Equal(a.Id);

        await using (var asB = CreateContext(_userB))
        {
            (await asB.CryptoHoldings.Select(x => x.Id).ToListAsync()).Should().Equal(b.Id);
            (await asB.CryptoHoldings.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.CryptoHoldings.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task Holdings_sync_with_no_person_updates_the_existing_row_instead_of_inserting_a_duplicate()
    {
        await SeedAsync(NewHolding(_userA, free: 1m), NewHolding(_userB, free: 1m));

        await using (var ctx = CreateContext())
        {
            var repository = new CryptoHoldingRepository(ctx);
            await repository.UpsertRangeAsync([NewHolding(_userA, free: 5m)]);
            await repository.SaveChangesAsync();
        }

        var rows = await AllHoldingsAsync();
        rows.Should().HaveCount(2);
        rows.Single(h => h.UserId == _userA).FreeQuantity.Should().Be(5m);
        rows.Single(h => h.UserId == _userB).FreeQuantity.Should().Be(1m);
    }

    [DockerRequiredFact]
    public async Task Holdings_delete_with_no_person_removes_only_the_named_users_rows()
    {
        await SeedAsync(NewHolding(_userA), NewHolding(_userB));

        await using (var ctx = CreateContext())
            await new CryptoHoldingRepository(ctx).DeleteByUserAndProviderAsync(_userA, Provider);

        (await AllHoldingsAsync()).Should().ContainSingle(h => h.UserId == _userB);
    }

    [DockerRequiredFact]
    public async Task Holdings_reads_follow_the_acting_person_and_the_unscoped_reads_serve_the_sync_without_one()
    {
        await SeedAsync(NewHolding(_userA), NewHolding(_userB));

        await using (var asA = CreateContext(_userA))
        {
            var repository = new CryptoHoldingRepository(asA);
            (await repository.GetByUserIdAsync(_userA)).Should().ContainSingle(h => h.UserId == _userA);
            (await repository.GetByUserIdAsync(_userB)).Should().BeEmpty(
                "naming another person does not lift the owner scope");
        }

        await using var asNoOne = CreateContext();
        var noPerson = new CryptoHoldingRepository(asNoOne);
        (await noPerson.GetByUserIdAsync(_userA)).Should().BeEmpty();
        (await noPerson.GetByUserIdUnscopedAsync(_userA)).Should().ContainSingle(h => h.UserId == _userA);
        (await noPerson.GetByUserAndProviderUnscopedAsync(_userA, Provider)).Should().ContainSingle(h => h.UserId == _userA);
        (await noPerson.GetAllByUserAndProviderUnscopedAsync(_userB, Provider)).Should().ContainSingle(h => h.UserId == _userB);
    }

    [DockerRequiredFact]
    public async Task Credential_reads_follow_the_acting_person_and_the_unscoped_read_serves_jobs()
    {
        await SeedAsync(NewCredential(_userA), NewCredential(_userB));

        await using (var asA = CreateContext(_userA))
        {
            var repository = new ExchangeCredentialRepository(asA);
            (await repository.GetAsync(_userA, Provider)).Should().NotBeNull();
            (await repository.GetAsync(_userB, Provider)).Should().BeNull();
        }

        await using var asNoOne = CreateContext();
        var noPerson = new ExchangeCredentialRepository(asNoOne);
        (await noPerson.GetAsync(_userA, Provider)).Should().BeNull();
        (await noPerson.GetUnscopedAsync(_userA, Provider))!.UserId.Should().Be(_userA);
    }

    [DockerRequiredFact]
    public async Task Trade_ingest_with_no_person_skips_trades_it_already_stored()
    {
        var trade = new CryptoTrade("t-1", "BTC", "USDT", 1m, 100m, 100m, true, DateTime.UtcNow);

        for (var run = 0; run < 2; run++)
        {
            await using var ctx = CreateContext();
            await new CryptoTradeRepository(ctx).AddNewAsync(_userA, Provider, [trade]);
            await ctx.SaveChangesAsync();
        }

        await using var read = CreateContext();
        (await read.CryptoTrades.IgnoreQueryFilters([OwnerQueryFilter.Name]).CountAsync(t => t.UserId == _userA))
            .Should().Be(1, "a filtered dedup read would find nothing and trip the unique index");
    }

    [DockerRequiredFact]
    public async Task Credential_sweep_with_no_person_sees_every_users_active_credentials()
    {
        await SeedAsync(NewCredential(_userA), NewCredential(_userB));

        await using var ctx = CreateContext();
        var active = await new ExchangeCredentialRepository(ctx).GetAllActiveUnscopedAsync(Provider);

        active.Select(c => c.UserId).Should().Contain([_userA, _userB]);
    }

    [DockerRequiredFact]
    public async Task Rotation_target_with_no_person_rotates_every_users_credentials()
    {
        var encryption = new Mock<ICredentialEncryptionService>();
        encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>())).Returns("t");
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns(new EncryptionResult([2], [2], [2], 2));
        await SeedAsync(NewCredential(_userA), NewCredential(_userB));

        await using var ctx = CreateContext();
        var rotated = await new ExchangeCredentialRotationTarget(ctx, encryption.Object).RotateAsync(2, default);

        rotated.Should().Be(2);
    }
}
