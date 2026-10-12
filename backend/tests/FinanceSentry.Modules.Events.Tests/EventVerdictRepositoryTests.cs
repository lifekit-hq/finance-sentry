namespace FinanceSentry.Modules.Events.Tests;

using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Infrastructure.Persistence;
using FinanceSentry.Modules.Events.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>Feature 049 US3: verdicts are found by alert id, scoped to the user. The upsert is covered against Postgres in <c>EventVerdictUpsertTests</c>.</summary>
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
        // Acts as UserId, as the controller and MCP callers do.
        _db = new EventsDbContext(options, new FixedCurrentUser(UserId));
        _repo = new EventVerdictRepository(_db);
    }

    [Fact]
    public async Task List_is_scoped_to_the_user_and_the_requested_alerts()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        // Seeded directly: the repository's upsert is a raw INSERT ... ON CONFLICT that only Postgres runs.
        _db.Verdicts.AddRange(
            new EventVerdict { UserId = UserId, CompanionEventId = Guid.NewGuid(), AlertId = mine, Verdict = "mine" },
            new EventVerdict { UserId = UserId, CompanionEventId = Guid.NewGuid(), AlertId = Guid.NewGuid(), Verdict = "not asked for" },
            new EventVerdict { UserId = Guid.NewGuid(), CompanionEventId = Guid.NewGuid(), AlertId = other, Verdict = "someone else's" });
        await _db.SaveChangesAsync();

        var rows = await _repo.ListByAlertIdsAsync(UserId, [mine, other]);

        rows.Should().ContainSingle().Which.Verdict.Should().Be("mine");
        (await _repo.ListByAlertIdsAsync(UserId, [])).Should().BeEmpty();
    }

    public void Dispose() => _db.Dispose();
}
