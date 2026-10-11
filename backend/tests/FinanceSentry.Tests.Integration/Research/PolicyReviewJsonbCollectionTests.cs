namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// <c>policy_reviews.Sleeves</c> and <c>Adjustments</c> are JSON-mapped complex collections that keep the
/// camelCase jsonb the former string conversion wrote. Real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PolicyReviewJsonbCollectionTests : IAsyncLifetime
{
    private const string LegacySleeves =
        """[{"assetClass":"Equity","targetPct":60,"minPct":50,"maxPct":70,"actualPct":72.5,"actualValueUsd":7250.25,"driftPct":12.5,"status":"Over"}]""";
    private const string LegacyAdjustments =
        """[{"assetClass":"Equity","action":"Trim","approxAmountUsd":1250.5,"rationale":"Over its band"}]""";

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

    private ResearchDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private static readonly PolicyReviewSleeve Sleeve = new("Equity", 60m, 50m, 70m, 72.5m, 7250.25m, 12.5m, "Over");
    private static readonly PolicyReviewAdjustment Adjustment = new("Equity", "Trim", 1250.5m, "Over its band");

    [DockerRequiredFact]
    public async Task Row_in_the_former_conversion_shape_reads_back_unchanged()
    {
        await using var db = CreateContext();
        var review = new PolicyReview { UserId = Guid.NewGuid(), PolicyStatementId = Guid.NewGuid(), Rationale = "r" };
        db.PolicyReviews.Add(review);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE research.policy_reviews SET "Sleeves" = {0}::jsonb, "Adjustments" = {1}::jsonb WHERE "Id" = {2}""",
            LegacySleeves, LegacyAdjustments, review.Id);

        await using var read = CreateContext();
        var stored = await read.PolicyReviews.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .SingleAsync(r => r.Id == review.Id);

        stored.Sleeves.Should().Equal(Sleeve);
        stored.Adjustments.Should().Equal(Adjustment);
    }

    [DockerRequiredFact]
    public async Task Written_lists_keep_the_camelCase_json_shape()
    {
        await using var db = CreateContext();
        var review = new PolicyReview
        {
            UserId = Guid.NewGuid(), PolicyStatementId = Guid.NewGuid(), Rationale = "r",
            Sleeves = [Sleeve], Adjustments = [Adjustment],
        };
        db.PolicyReviews.Add(review);
        await db.SaveChangesAsync();

        // jsonb equality: same elements, names and values as the former conversion wrote.
        var same = await db.Database.SqlQueryRaw<bool>(
            """
            SELECT "Sleeves" = {0}::jsonb AND "Adjustments" = {1}::jsonb AS "Value"
            FROM research.policy_reviews WHERE "Id" = {2}
            """,
            LegacySleeves, LegacyAdjustments, review.Id).SingleAsync();
        same.Should().BeTrue();
    }
}
