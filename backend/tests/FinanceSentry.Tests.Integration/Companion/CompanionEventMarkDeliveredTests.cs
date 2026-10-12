namespace FinanceSentry.Tests.Integration.Companion;

using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// <see cref="CompanionEventRepository.MarkDeliveredAsync"/> is one set-based <c>ExecuteUpdateAsync</c>: it
/// delivers the named events of the named person, reports how many it changed, and leaves everything else
/// (another person's events, unnamed events, events already delivered) as it was. Real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CompanionEventMarkDeliveredTests : IAsyncLifetime
{
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

    private CompanionDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<CompanionDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static CompanionEvent NewEvent(Guid userId, EventDisposition disposition) => new()
    {
        UserId = userId,
        Kind = CompanionEventKind.NewsCluster,
        Subject = "MU",
        Severity = "info",
        Summary = "News cluster: MU",
        DedupKey = $"test:{Guid.NewGuid():N}",
        SourceModule = "alerts",
        Disposition = disposition,
        OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
    };

    private async Task SeedAsync(params CompanionEvent[] events)
    {
        await using var seed = CreateContext();
        seed.Events.AddRange(events);
        await seed.SaveChangesAsync();
    }

    private async Task<CompanionEvent> ReloadAsync(CompanionEvent evt)
    {
        await using var read = CreateContext(evt.UserId);
        return await read.Events.AsNoTracking().SingleAsync(e => e.Id == evt.Id);
    }

    [DockerRequiredFact]
    public async Task MarkDelivered_delivers_only_the_named_undelivered_events_of_the_person()
    {
        var pending = NewEvent(_userA, EventDisposition.Pending);
        var held = NewEvent(_userA, EventDisposition.HeldForDigest);
        var unnamed = NewEvent(_userA, EventDisposition.Pending);
        var othersEvent = NewEvent(_userB, EventDisposition.Pending);
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-2);
        var alreadyDelivered = NewEvent(_userA, EventDisposition.Delivered);
        alreadyDelivered.DeliveredAt = deliveredAt;
        await SeedAsync(pending, held, unnamed, othersEvent, alreadyDelivered);

        var before = DateTimeOffset.UtcNow;
        int changed;
        await using (var ctx = CreateContext(_userA))
        {
            changed = await new CompanionEventRepository(ctx).MarkDeliveredAsync(
                _userA, [pending.Id, held.Id, othersEvent.Id, alreadyDelivered.Id]);
        }

        changed.Should().Be(2, "only the person's own, not-yet-delivered, named events change");
        foreach (var delivered in new[] { pending, held })
        {
            var row = await ReloadAsync(delivered);
            row.Disposition.Should().Be(EventDisposition.Delivered);
            row.DeliveredAt.Should().BeOnOrAfter(before);
        }

        var untouched = await ReloadAsync(unnamed);
        untouched.Disposition.Should().Be(EventDisposition.Pending);
        untouched.DeliveredAt.Should().BeNull();

        var others = await ReloadAsync(othersEvent);
        others.Disposition.Should().Be(EventDisposition.Pending);
        others.DeliveredAt.Should().BeNull();

        var kept = await ReloadAsync(alreadyDelivered);
        kept.DeliveredAt.Should().BeCloseTo(deliveredAt, TimeSpan.FromSeconds(1));
    }

    [DockerRequiredFact]
    public async Task MarkDelivered_with_no_ids_changes_nothing()
    {
        var pending = NewEvent(_userA, EventDisposition.Pending);
        await SeedAsync(pending);

        await using (var ctx = CreateContext(_userA))
            (await new CompanionEventRepository(ctx).MarkDeliveredAsync(_userA, [])).Should().Be(0);

        (await ReloadAsync(pending)).Disposition.Should().Be(EventDisposition.Pending);
    }

    [DockerRequiredFact]
    public async Task MarkDelivered_acting_for_another_person_changes_nothing()
    {
        var pending = NewEvent(_userA, EventDisposition.Pending);
        await SeedAsync(pending);

        await using (var ctx = CreateContext(_userB))
            (await new CompanionEventRepository(ctx).MarkDeliveredAsync(_userA, [pending.Id])).Should().Be(0);

        (await ReloadAsync(pending)).Disposition.Should().Be(EventDisposition.Pending);
    }
}
