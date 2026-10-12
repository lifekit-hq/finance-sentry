namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Pinning a merchant is one <c>INSERT ... ON CONFLICT DO NOTHING</c> on the unique (user, merchant key) index:
/// repeating a key keeps one row and reports it as already pinned, and pins racing on the same key do not fail on
/// the index. The in-memory provider enforces neither the index nor raw SQL, so this needs real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CommittedMerchantPinUpsertTests : IAsyncLifetime
{
    private readonly Guid _user = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext(null);
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private BankSyncDbContext CreateContext(Guid? actingUser) =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private CommittedMerchantPin NewPin(Guid userId, string key, string displayName) =>
        new() { UserId = userId, MerchantKey = key, DisplayName = displayName };

    private async Task<AddCommittedMerchantPinResult> AddAsync(Guid userId, string key, string displayName)
    {
        await using var ctx = CreateContext(userId);
        return await new CommittedMerchantPinRepository(ctx).AddIfAbsentAsync(NewPin(userId, key, displayName));
    }

    private async Task<List<CommittedMerchantPin>> PinsAsync(Guid userId)
    {
        await using var ctx = CreateContext(null);
        return await ctx.CommittedMerchantPins.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync();
    }

    [DockerRequiredFact]
    public async Task A_repeated_add_of_one_key_keeps_one_row_and_reports_the_first()
    {
        var first = await AddAsync(_user, "anytime fitness", "Anytime Fitness");
        var second = await AddAsync(_user, "anytime fitness", "ANYTIME FITNESS");

        first.AlreadyPinned.Should().BeFalse();
        second.AlreadyPinned.Should().BeTrue();
        second.Pin.Id.Should().Be(first.Pin.Id);
        second.Pin.DisplayName.Should().Be("Anytime Fitness", "the first spelling is kept");

        var row = (await PinsAsync(_user)).Should().ContainSingle().Subject;
        row.Id.Should().Be(first.Pin.Id);
        row.DisplayName.Should().Be("Anytime Fitness");
    }

    [DockerRequiredFact]
    public async Task The_same_key_for_another_user_is_a_separate_pin()
    {
        var other = Guid.NewGuid();
        await AddAsync(_user, "netflix", "Netflix");

        var result = await AddAsync(other, "netflix", "Netflix");

        result.AlreadyPinned.Should().BeFalse();
        (await PinsAsync(_user)).Should().ContainSingle();
        (await PinsAsync(other)).Should().ContainSingle();
    }

    [DockerRequiredFact]
    public async Task Concurrent_adds_of_one_key_neither_fail_nor_duplicate_and_one_wins()
    {
        var adds = await Task.WhenAll(Enumerable.Range(1, 8)
            .Select(i => AddAsync(_user, "mario scalas", $"Mario Scalas {i}")));

        adds.Count(r => !r.AlreadyPinned).Should().Be(1);
        var row = (await PinsAsync(_user)).Should().ContainSingle().Subject;
        adds.Should().OnlyContain(r => r.Pin.Id == row.Id);
    }
}
