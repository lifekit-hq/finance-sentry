namespace FinanceSentry.Modules.Research.Tests.PolicyReviews;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

/// <summary>
/// Recording a review writes the statement's last-reviewed timestamp in the same unit of work
/// (#696) — over real SQL (SQLite in memory), no Docker and no network.
/// </summary>
public sealed class PolicyReviewRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Completed = new(2026, 9, 30, 5, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _userId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var ctx = Context();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Recording_a_review_writes_the_last_reviewed_timestamp_on_the_reviewed_statement()
    {
        var reviewed = await SeedStatementAsync(version: 2, isCurrent: true);
        var older = await SeedStatementAsync(version: 1, isCurrent: false);

        await using (var ctx = Context())
            await new PolicyReviewRepository(ctx).RecordAsync(Review(reviewed.Id, Completed));

        await using var read = Context();
        (await read.PolicyStatements.SingleAsync(x => x.Id == reviewed.Id)).LastReviewedAt.Should().Be(Completed);
        (await read.PolicyStatements.SingleAsync(x => x.Id == older.Id)).LastReviewedAt.Should().BeNull();
    }

    [Fact]
    public async Task Recorded_review_round_trips_its_structured_proposal()
    {
        var ips = await SeedStatementAsync();
        var review = Review(ips.Id, Completed);

        await using (var ctx = Context())
            await new PolicyReviewRepository(ctx).RecordAsync(review);

        await using var read = Context();
        var latest = await new PolicyReviewRepository(read).GetLatestAsync(_userId);
        latest.Should().BeEquivalentTo(review);
    }

    [Fact]
    public async Task Latest_review_is_the_most_recently_completed()
    {
        var ips = await SeedStatementAsync();
        await using (var ctx = Context())
        {
            var repo = new PolicyReviewRepository(ctx);
            await repo.RecordAsync(Review(ips.Id, Completed.AddMonths(-3)));
            await repo.RecordAsync(Review(ips.Id, Completed));
        }

        await using var read = Context();
        (await new PolicyReviewRepository(read).GetLatestAsync(_userId))!.CompletedAt.Should().Be(Completed);
        (await new PolicyReviewRepository(read).GetLatestAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task A_review_that_fails_to_save_leaves_the_statement_unreviewed()
    {
        var ips = await SeedStatementAsync();
        var first = Review(ips.Id, Completed.AddMonths(-3));
        await using (var ctx = Context())
            await new PolicyReviewRepository(ctx).RecordAsync(first);

        var clash = Review(ips.Id, Completed);
        clash.Id = first.Id;
        await using (var ctx = Context())
        {
            var act = () => new PolicyReviewRepository(ctx).RecordAsync(clash);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using var read = Context();
        (await read.PolicyStatements.SingleAsync()).LastReviewedAt.Should().Be(Completed.AddMonths(-3));
    }

    [Fact]
    public async Task Review_against_a_statement_that_does_not_exist_is_refused()
    {
        await using var ctx = Context();

        var act = () => new PolicyReviewRepository(ctx).RecordAsync(Review(Guid.NewGuid(), Completed));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<InvestmentPolicyStatement> SeedStatementAsync(int version = 1, bool isCurrent = true)
    {
        var ips = new InvestmentPolicyStatement
        {
            UserId = _userId,
            Version = version,
            IsCurrent = isCurrent,
            ReviewCadence = "quarterly",
            AllocationTargets = [new AllocationTarget("Equity", 60, 55, 65)],
        };
        await using var ctx = Context();
        ctx.PolicyStatements.Add(ips);
        await ctx.SaveChangesAsync();
        return ips;
    }

    private PolicyReview Review(Guid statementId, DateTimeOffset completedAt) => new()
    {
        UserId = _userId,
        PolicyStatementId = statementId,
        PolicyStatementVersion = 1,
        ReviewCadence = "quarterly",
        DueAt = completedAt.AddDays(-1),
        CompletedAt = completedAt,
        DaysOverdue = 1,
        TotalValueUsd = 100_000m,
        Sleeves = [new PolicyReviewSleeve("Equity", 60, 55, 65, 70, 70_000, 10, "OverBand")],
        Adjustments = [new PolicyReviewAdjustment("Equity", PolicyReviewAdjustmentAction.Trim, 10_000, "Above its band.")],
        Rationale = "Scheduled quarterly review.",
    };

    private PolicyReviewSqliteContext Context() =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseSqlite(_connection).Options);

    /// <summary>
    /// Keeps only the statement and review tables — the rest of the schema leans on Postgres-only
    /// constructs — and clears the Postgres <c>gen_random_uuid()</c> defaults. Timestamps are stored
    /// as binary so SQLite can order by them, as Postgres does natively.
    /// </summary>
    private sealed class PolicyReviewSqliteContext(DbContextOptions<ResearchDbContext> options)
        : ResearchDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var kept = new HashSet<Type> { typeof(InvestmentPolicyStatement), typeof(PolicyReview) };
            foreach (var clrType in modelBuilder.Model.GetEntityTypes().Select(e => e.ClrType).Where(t => !kept.Contains(t)).ToList())
                modelBuilder.Ignore(clrType);

            modelBuilder.Entity<InvestmentPolicyStatement>().Property(x => x.Id).HasDefaultValueSql(null);
            var review = modelBuilder.Entity<PolicyReview>();
            review.Property(x => x.Id).HasDefaultValueSql(null);
            review.Property(x => x.CompletedAt).HasConversion(new DateTimeOffsetToBinaryConverter());
        }
    }
}
