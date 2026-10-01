namespace FinanceSentry.Tests.Integration.Analytics;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Analytics.Domain;
using FinanceSentry.Modules.Analytics.Infrastructure.Persistence;
using FinanceSentry.Modules.Analytics.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="AnalyticsDbContext"/>: a context acting for one person sees only that
/// person's query-audit rows, and a context with no person in scope sees none. The module only appends audit
/// rows (inserts are not filtered) and runs no job. Real Postgres, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AnalyticsOwnerQueryFilterTests : IAsyncLifetime
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
    private AnalyticsDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options,
            new FixedCurrentUser(actingUser));

    private static QueryAuditRecord NewAudit(Guid userId) =>
        new() { UserId = userId, Sql = "SELECT 1", Outcome = QueryOutcome.Executed };

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
        // The repository appends through a no-person context: inserts are not filtered.
        await using (var seed = CreateContext())
        {
            var repo = new QueryAuditRepository(seed);
            await repo.AppendAsync(NewAudit(_userA));
            await repo.AppendAsync(NewAudit(_userB));
        }

        await using (var asA = CreateContext(_userA))
            (await asA.QueryAudit.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);

        await using (var asB = CreateContext(_userB))
        {
            (await asB.QueryAudit.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.QueryAudit.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }
}
