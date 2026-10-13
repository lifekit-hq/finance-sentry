namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// #1069: recommendations are owner-scoped like the rest of <see cref="ResearchDbContext"/>. The Decisions queue reads
/// only its person's rows; the rule sweep, the expiry job and the outcome job run with no person in scope and use the
/// unscoped reads; the partial unique index keeps one pending proposal per person and ticker. Real Postgres is required.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RecommendationOwnerQueryFilterTests : IAsyncLifetime
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

    // Null acts as a background job: no person in scope.
    private ResearchDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static Recommendation NewRecommendation(
        Guid userId, string ticker = "GRAB", RecommendationStatus status = RecommendationStatus.Pending,
        DateTimeOffset? expiresAt = null, DateTimeOffset? decidedAt = null) => new()
    {
        UserId = userId,
        Ticker = ticker,
        Action = RecommendationAction.Trim,
        TriggerKind = RecommendationTriggerKind.RiskRule,
        TriggerRef = RecommendationEvidenceFactory.MaxPositionWeightRule,
        Rationale = "Over the cap.",
        InvalidationTrigger = "Back under the cap.",
        Evidence = [RecommendationEvidenceFactory.OverCap(0.23m, 0.15m), RecommendationEvidenceFactory.NoThesis()],
        Status = status,
        ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(14),
        DecidedAt = decidedAt,
    };

    private async Task SeedAsync(params Recommendation[] rows)
    {
        await using var seed = CreateContext();
        seed.AddRange(rows);
        await seed.SaveChangesAsync();
    }

    [DockerRequiredFact]
    public async Task The_decisions_queue_shows_each_person_only_their_own_proposals()
    {
        var a = NewRecommendation(_userA);
        var b = NewRecommendation(_userB);
        await SeedAsync(a, b);

        await using (var asA = CreateContext(_userA))
        {
            var repo = new RecommendationRepository(asA);
            (await repo.ListPendingAsync(_userA)).Select(r => r.Id).Should().Equal(a.Id);
            (await repo.GetAsync(_userA, b.Id)).Should().BeNull();
            (await repo.ListPendingAsync(_userB)).Should().BeEmpty("the filter holds even when a read names another person");
            (await repo.FindPendingAsync(_userA, "GRAB"))!.Evidence.Should().HaveCount(2, "evidence round-trips through jsonb");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.Recommendations.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task The_sweep_and_the_jobs_read_every_persons_rows_with_no_person_in_scope()
    {
        var past = DateTimeOffset.UtcNow.AddDays(-1);
        var expiredA = NewRecommendation(_userA, "DRAM", expiresAt: past);
        var pendingB = NewRecommendation(_userB);
        var decidedA = NewRecommendation(_userA, "MU", RecommendationStatus.Confirmed, decidedAt: past);
        await SeedAsync(expiredA, pendingB, decidedA);

        await using var asNoOne = CreateContext();
        var repo = new RecommendationRepository(asNoOne);

        (await repo.FindPendingUnscopedAsync(_userB, "GRAB"))!.Id.Should().Be(pendingB.Id);
        (await repo.ExistsForTriggerSinceUnscopedAsync(
            _userA, "DRAM", RecommendationTriggerKind.RiskRule, RecommendationEvidenceFactory.MaxPositionWeightRule, past.AddDays(-1)))
            .Should().BeTrue();
        (await repo.ListExpiredUnscopedAsync(DateTimeOffset.UtcNow)).Select(r => r.Id).Should().Equal(expiredA.Id);
        (await repo.ListOutcomeOpenUnscopedAsync()).Select(r => r.Id).Should().Equal(decidedA.Id);
    }

    [DockerRequiredFact]
    public async Task A_second_pending_proposal_on_the_same_ticker_is_a_conflict_but_another_person_may_have_one()
    {
        await SeedAsync(NewRecommendation(_userA));

        await using (var asNoOne = CreateContext())
        {
            var act = () => new RecommendationRepository(asNoOne).AddPendingAsync(NewRecommendation(_userA));
            await act.Should().ThrowAsync<RecommendationConflictException>();
        }

        await using (var again = CreateContext())
        {
            await new RecommendationRepository(again).AddPendingAsync(NewRecommendation(_userB));
            await new RecommendationRepository(again).AddPendingAsync(NewRecommendation(_userA, status: RecommendationStatus.Rejected));
        }

        await using var check = CreateContext();
        (await check.Recommendations.IgnoreQueryFilters().CountAsync()).Should().Be(3,
            "the unique index covers pending rows only, per person");
    }
}
