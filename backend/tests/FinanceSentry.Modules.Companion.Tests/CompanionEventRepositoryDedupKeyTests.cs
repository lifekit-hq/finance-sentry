namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>049: the dedup-key lookup is user-scoped and exact.</summary>
public sealed class CompanionEventRepositoryDedupKeyTests : IDisposable
{
    private static readonly Guid User = Guid.NewGuid();
    private readonly CompanionDbContext _db;
    private readonly CompanionEventRepository _repo;

    public CompanionEventRepositoryDedupKeyTests()
    {
        _db = new CompanionDbContext(new DbContextOptionsBuilder<CompanionDbContext>()
            .UseInMemoryDatabase($"companion-{Guid.NewGuid():N}").Options);
        _repo = new CompanionEventRepository(_db);
    }

    private CompanionEvent Add(Guid user, string key)
    {
        var evt = new CompanionEvent
        {
            UserId = user, Kind = CompanionEventKind.NewsCluster, Subject = "MU", Severity = "Warning",
            Summary = "s", DedupKey = key, SourceModule = "alerts", OccurredAt = DateTimeOffset.UtcNow,
            CapturedAt = DateTimeOffset.UtcNow,
        };
        _db.Events.Add(evt);
        return evt;
    }

    [Fact]
    public async Task Returns_the_users_rows_for_the_requested_keys_only()
    {
        var mine = Add(User, "alert:1");
        Add(User, "alert:2");
        Add(Guid.NewGuid(), "alert:3");
        await _db.SaveChangesAsync();

        var rows = await _repo.ListByDedupKeysAsync(User, ["alert:1", "alert:3", "alert:9"]);

        rows.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        (await _repo.ListByDedupKeysAsync(User, [])).Should().BeEmpty();
    }

    public void Dispose() => _db.Dispose();
}
