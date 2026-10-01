namespace FinanceSentry.Tests.Integration.Budgets;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Infrastructure.Jobs;
using FinanceSentry.Modules.Budgets.Infrastructure.Persistence;
using FinanceSentry.Modules.Budgets.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="BudgetsDbContext"/>: a context acting for one person sees only that
/// person's budgets, a context with no person in scope sees none, and the budget-breach sentinel (which runs
/// with no person in scope) opts out explicitly, so it still sweeps every user's budgets.
/// Real Postgres, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BudgetsOwnerQueryFilterTests : IAsyncLifetime
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
    private BudgetsDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<BudgetsDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options,
            new FixedCurrentUser(actingUser));

    private async Task<(Budget A, Budget B)> SeedAsync()
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        var a = Budget.Create(_userA, "FOOD_AND_DRINK", 100m, "USD");
        var b = Budget.Create(_userB, "FOOD_AND_DRINK", 100m, "USD");
        seed.Budgets.AddRange(a, b);
        await seed.SaveChangesAsync();
        return (a, b);
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
        await SeedAsync();

        await using (var asA = CreateContext(_userA))
            (await asA.Budgets.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);

        await using (var asB = CreateContext(_userB))
        {
            (await asB.Budgets.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.Budgets.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task Repository_reads_follow_the_acting_person_and_the_unscoped_read_serves_callers_without_one()
    {
        var (a, b) = await SeedAsync();

        await using (var asA = CreateContext(_userA))
        {
            var repo = new BudgetRepository(asA);
            (await repo.GetByUserIdAsync(_userA)).Should().ContainSingle();
            (await repo.GetByIdAsync(a.Id)).Should().NotBeNull();
            (await repo.GetByIdAsync(b.Id)).Should().BeNull("naming another person's row does not lift the owner scope");
            (await repo.FindByUserAndCategoryAsync(_userB, b.Category)).Should().BeNull();
        }

        await using var asNoOne = CreateContext();
        (await new BudgetRepository(asNoOne).GetAllUnscopedAsync())
            .Select(x => x.UserId).Should().BeEquivalentTo([_userA, _userB]);
    }

    [DockerRequiredFact]
    public async Task Breach_sentinel_with_no_person_checks_every_users_budget()
    {
        await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var slot = new DateTimeOffset(now.Year, now.Month, now.Day, 23, 55, 0, TimeSpan.Zero);
        var spending = new Mock<IMerchantSpendingReader>();
        spending.Setup(s => s.GetSpendingByCategoryUsdAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["FOOD_AND_DRINK"] = 150m });
        var normalization = new Mock<ICategoryNormalizationService>();
        normalization.Setup(n => n.Normalize(It.IsAny<string>())).Returns((string c) => c);
        var alerts = new Mock<IAlertGeneratorService>();

        await using (var asNoOne = CreateContext())
        {
            await new BudgetBreachDetectionJob(
                new BudgetRepository(asNoOne), spending.Object, normalization.Object, alerts.Object,
                new FixedTimeProvider(slot), NullLogger<BudgetBreachDetectionJob>.Instance).ExecuteAsync();
        }

        foreach (var user in new[] { _userA, _userB })
        {
            alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
                user, It.IsAny<Guid>(), "FOOD_AND_DRINK", 150m, 100m, slot.Year, slot.Month, It.IsAny<CancellationToken>()),
                Times.Once, "a filtered read would find no budget and raise nothing");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
