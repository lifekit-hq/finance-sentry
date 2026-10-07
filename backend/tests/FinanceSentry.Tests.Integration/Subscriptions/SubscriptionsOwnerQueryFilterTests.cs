namespace FinanceSentry.Tests.Integration.Subscriptions;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Application.Services;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="SubscriptionsDbContext"/>, whose UserId column is a string holding
/// <see cref="Guid.ToString()"/>: a context acting for one person sees only that person's rows, a context with no
/// person in scope sees none, and the detection job and the cross-module readers (which run with no person in scope)
/// opt out explicitly, so they neither see nothing nor re-insert rows they could not find. Real Postgres is required,
/// matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SubscriptionsOwnerQueryFilterTests : IAsyncLifetime
{
    private const string Merchant = "netflix";

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
    private SubscriptionsDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<SubscriptionsDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static DetectedSubscription NewSubscription(
        Guid userId, decimal amount = 15.99m, DateOnly? lastCharge = null)
    {
        var last = lastCharge ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return DetectedSubscription.Create(
            userId.ToString(), Merchant, "Netflix", "monthly", amount, amount, "USD",
            last, last.AddMonths(1), occurrenceCount: 3, confidenceScore: 90, category: null);
    }

    private static DetectedSubscriptionData Detection(decimal amount) => new(
        Merchant, "Netflix", "monthly", amount, amount, "USD",
        DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
        OccurrenceCount: 4, ConfidenceScore: 90, Category: null);

    private async Task SeedAsync(params DetectedSubscription[] subscriptions)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.DetectedSubscriptions.AddRange(subscriptions);
        await seed.SaveChangesAsync();
    }

    private async Task<List<DetectedSubscription>> AllSubscriptionsAsync()
    {
        await using var read = CreateContext();
        return await read.DetectedSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .ToListAsync();
    }

    [Fact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes().Where(e => e.FindProperty("UserId") is not null).ToList();

        perUser.Should().ContainSingle().Which.ClrType.Should().Be<DetectedSubscription>();
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_subscriptions_and_no_person_sees_none()
    {
        var a = NewSubscription(_userA);
        var b = NewSubscription(_userB);
        await SeedAsync(a, b);

        await using (var asA = CreateContext(_userA))
            (await asA.DetectedSubscriptions.Select(x => x.Id).ToListAsync()).Should().Equal(a.Id);

        await using (var asB = CreateContext(_userB))
        {
            (await asB.DetectedSubscriptions.Select(x => x.Id).ToListAsync()).Should().Equal(b.Id);
            (await asB.DetectedSubscriptions.AnyAsync(x => x.UserId == _userA.ToString())).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
            (await new DetectedSubscriptionRepository(asB).GetByIdAsync(a.Id)).Should().BeNull(
                "a request for another person's subscription by id finds nothing");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.DetectedSubscriptions.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task Detection_with_no_person_updates_the_existing_row_instead_of_inserting_a_duplicate()
    {
        var a = NewSubscription(_userA, amount: 15.99m);
        var b = NewSubscription(_userB, amount: 15.99m);
        await SeedAsync(a, b);

        await using (var ctx = CreateContext())
        {
            var detection = new SubscriptionDetectionResultService(new DetectedSubscriptionRepository(ctx));
            await detection.UpsertDetectedSubscriptionsAsync(_userA.ToString(), [Detection(17.99m)]);
        }

        var rows = await AllSubscriptionsAsync();
        rows.Should().HaveCount(2);
        rows.Single(s => s.Id == a.Id).LastKnownAmount.Should().Be(17.99m);
        rows.Single(s => s.Id == b.Id).LastKnownAmount.Should().Be(15.99m, "another person's row is untouched");
    }

    [DockerRequiredFact]
    public async Task Stale_sweep_with_no_person_marks_only_that_users_lapsed_subscriptions()
    {
        var lapsed = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-90);
        var a = NewSubscription(_userA, lastCharge: lapsed);
        var b = NewSubscription(_userB, lastCharge: lapsed);
        await SeedAsync(a, b);

        await using (var ctx = CreateContext())
        {
            var detection = new SubscriptionDetectionResultService(new DetectedSubscriptionRepository(ctx));
            await detection.MarkStaleAsPotentiallyCancelledAsync(_userA.ToString());
        }

        var rows = await AllSubscriptionsAsync();
        rows.Single(s => s.Id == a.Id).Status.Should().Be(SubscriptionStatus.PotentiallyCancelled);
        rows.Single(s => s.Id == b.Id).Status.Should().Be(SubscriptionStatus.Active);
    }

    [DockerRequiredFact]
    public async Task Active_reader_with_no_person_returns_that_users_subscriptions_only()
    {
        await SeedAsync(NewSubscription(_userA, amount: 15.99m), NewSubscription(_userB, amount: 9.99m));

        await using var ctx = CreateContext();
        var reader = new ActiveSubscriptionsReader(new DetectedSubscriptionRepository(ctx));

        var active = await reader.GetActiveSubscriptionsAsync(_userA);

        active.Should().ContainSingle().Which.AverageAmount.Should().Be(15.99m);
    }

    [DockerRequiredFact]
    public async Task Hygiene_reader_with_no_person_returns_every_users_active_subscriptions()
    {
        var a = NewSubscription(_userA);
        var b = NewSubscription(_userB);
        await SeedAsync(a, b);

        await using var ctx = CreateContext();
        var summaries = await new SubscriptionHygieneSummaryReader(ctx).GetAllActiveAsync();

        summaries.Select(s => (s.Id, s.UserId)).Should().BeEquivalentTo([(a.Id, _userA), (b.Id, _userB)]);
    }
}
