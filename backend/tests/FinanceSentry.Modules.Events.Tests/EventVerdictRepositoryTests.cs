namespace FinanceSentry.Modules.Events.Tests;

using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Infrastructure.Persistence;
using FinanceSentry.Modules.Events.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>Feature 049 US3: one verdict per (user, event); a later record replaces it.</summary>
public sealed class EventVerdictRepositoryTests : IDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly EventsDbContext _db;
    private readonly EventVerdictRepository _repo;

    public EventVerdictRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<EventsDbContext>()
            .UseInMemoryDatabase($"events-{Guid.NewGuid():N}")
            .Options;
        _db = new EventsDbContext(options);
        _repo = new EventVerdictRepository(_db);
    }

    [Fact]
    public async Task Upsert_replaces_the_verdict_for_the_same_event()
    {
        var eventId = Guid.NewGuid();
        await _repo.UpsertAsync(new EventVerdict { UserId = UserId, CompanionEventId = eventId, Verdict = "first", Notified = false });
        await _repo.UpsertAsync(new EventVerdict { UserId = UserId, CompanionEventId = eventId, Verdict = "revised", Notified = true });

        var rows = await _repo.ListByEventIdsAsync(UserId, [eventId]);

        var row = rows.Should().ContainSingle().Subject;
        row.Verdict.Should().Be("revised");
        row.Notified.Should().BeTrue();
        _db.Verdicts.Count().Should().Be(1);
    }

    [Fact]
    public async Task List_is_scoped_to_the_user_and_the_requested_events()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        await _repo.UpsertAsync(new EventVerdict { UserId = UserId, CompanionEventId = mine, Verdict = "mine" });
        await _repo.UpsertAsync(new EventVerdict { UserId = UserId, CompanionEventId = Guid.NewGuid(), Verdict = "not asked for" });
        await _repo.UpsertAsync(new EventVerdict { UserId = Guid.NewGuid(), CompanionEventId = other, Verdict = "someone else's" });

        var rows = await _repo.ListByEventIdsAsync(UserId, [mine, other]);

        rows.Should().ContainSingle().Which.Verdict.Should().Be("mine");
        (await _repo.ListByEventIdsAsync(UserId, [])).Should().BeEmpty();
    }

    public void Dispose() => _db.Dispose();
}
