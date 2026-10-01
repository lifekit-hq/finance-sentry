namespace FinanceSentry.Tests.Integration.Events;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Infrastructure.Persistence;
using FinanceSentry.Modules.Events.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="EventsDbContext"/>: a context acting for one person sees only that
/// person's event verdicts, and a context with no person in scope sees none. Every caller is a request or an
/// MCP tool acting for the signed-in person; the module runs no job. Real Postgres, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EventsOwnerQueryFilterTests : IAsyncLifetime
{
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    // Null acts as a background job: no person in scope.
    private EventsDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<EventsDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options,
            new FixedCurrentUser(actingUser));

    private static EventVerdict NewVerdict(Guid userId, Guid companionEventId, Guid alertId) => new()
    {
        UserId = userId, CompanionEventId = companionEventId, AlertId = alertId, Verdict = "judged",
    };

    private async Task SeedAsync(params EventVerdict[] verdicts)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.Verdicts.AddRange(verdicts);
        await seed.SaveChangesAsync();
    }

    [Fact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes().Where(e => e.FindProperty("UserId") is not null).ToList();

        perUser.Should().HaveCount(1);
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_rows_and_no_person_sees_none()
    {
        await SeedAsync(NewVerdict(_userA, Guid.NewGuid(), Guid.NewGuid()), NewVerdict(_userB, Guid.NewGuid(), Guid.NewGuid()));

        await using (var asA = CreateContext(_userA))
            (await asA.Verdicts.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);

        await using (var asB = CreateContext(_userB))
        {
            (await asB.Verdicts.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.Verdicts.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task Repository_reads_follow_the_acting_person()
    {
        var eventId = Guid.NewGuid();
        var alertId = Guid.NewGuid();
        await SeedAsync(NewVerdict(_userA, eventId, alertId));

        await using (var asA = CreateContext(_userA))
        {
            var repo = new EventVerdictRepository(asA);
            (await repo.ListByAlertIdsAsync(_userA, [alertId])).Should().ContainSingle();
            (await repo.ListByCompanionEventIdsAsync(_userA, [eventId])).Should().ContainSingle();
        }

        await using var asB = CreateContext(_userB);
        var asBRepo = new EventVerdictRepository(asB);
        (await asBRepo.ListByAlertIdsAsync(_userA, [alertId])).Should().BeEmpty(
            "naming another person does not lift the owner scope");
        (await asBRepo.ListByCompanionEventIdsAsync(_userA, [eventId])).Should().BeEmpty();
    }
}
