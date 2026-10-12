namespace FinanceSentry.Tests.Integration.Events;

using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Infrastructure.Persistence;
using FinanceSentry.Modules.Events.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The verdict write is one <c>INSERT ... ON CONFLICT</c> on the unique (UserId, CompanionEventId) index: recording an
/// event again replaces its verdict in place, and writers racing on one event do not fail on the index. The in-memory
/// provider enforces neither the index nor raw SQL, so this needs real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EventVerdictUpsertTests : IAsyncLifetime
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

    // The acting person, as the controller and MCP callers run.
    private EventsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<EventsDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(_user));

    private EventVerdict Verdict(Guid eventId, Guid? alertId, string text, bool notified) => new()
    {
        UserId = _user, CompanionEventId = eventId, AlertId = alertId, Verdict = text, Notified = notified,
    };

    private async Task<List<EventVerdict>> VerdictsAsync()
    {
        await using var ctx = CreateContext();
        return await ctx.Verdicts.AsNoTracking().ToListAsync();
    }

    [DockerRequiredFact]
    public async Task A_repeated_verdict_for_one_event_keeps_one_row_and_replaces_it()
    {
        var eventId = Guid.NewGuid();
        var alertId = Guid.NewGuid();
        var first = Verdict(eventId, alertId, "first", notified: false);
        await using (var ctx = CreateContext())
            await new EventVerdictRepository(ctx).UpsertAsync(first);

        var revised = Verdict(eventId, alertId: null, "revised", notified: true);
        await using (var ctx = CreateContext())
            await new EventVerdictRepository(ctx).UpsertAsync(revised);

        var row = (await VerdictsAsync()).Should().ContainSingle().Subject;
        row.Id.Should().Be(first.Id, "the row is replaced in place");
        row.Verdict.Should().Be("revised");
        row.Notified.Should().BeTrue();
        row.AlertId.Should().BeNull();
        row.RecordedAt.Should().BeCloseTo(revised.RecordedAt, TimeSpan.FromMilliseconds(1));
    }

    [DockerRequiredFact]
    public async Task Different_events_keep_separate_rows()
    {
        await using (var ctx = CreateContext())
        {
            var repo = new EventVerdictRepository(ctx);
            await repo.UpsertAsync(Verdict(Guid.NewGuid(), Guid.NewGuid(), "one", notified: true));
            await repo.UpsertAsync(Verdict(Guid.NewGuid(), Guid.NewGuid(), "two", notified: false));
        }

        (await VerdictsAsync()).Should().HaveCount(2);
    }

    [DockerRequiredFact]
    public async Task Concurrent_verdicts_for_one_event_leave_one_row_without_failing()
    {
        var eventId = Guid.NewGuid();
        var writes = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new EventVerdictRepository(ctx).UpsertAsync(Verdict(eventId, Guid.NewGuid(), $"v{i}", notified: i % 2 == 0));
        });

        await Task.WhenAll(writes);

        (await VerdictsAsync()).Should().ContainSingle();
    }
}
