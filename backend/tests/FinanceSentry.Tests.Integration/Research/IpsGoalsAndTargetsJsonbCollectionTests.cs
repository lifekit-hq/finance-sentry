namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// <c>investment_policy_statements.Goals</c> and <c>AllocationTargets</c> are JSON-mapped complex collections
/// that keep the camelCase jsonb the former string conversion wrote. Real Postgres: the InMemory provider
/// cannot run jsonb element queries.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IpsGoalsAndTargetsJsonbCollectionTests : IAsyncLifetime
{
    private const string LegacyGoals =
        """[{"label":"House","targetAmountUsd":120000.5,"targetDate":"2030-06-01","priority":"High"},{"label":"Rainy day","targetAmountUsd":null,"targetDate":null,"priority":"Low"}]""";
    private const string LegacyTargets =
        """[{"assetClass":"Equity","targetPct":60.5,"minPct":50,"maxPct":70},{"assetClass":"Cash","targetPct":39.5,"minPct":30,"maxPct":50}]""";

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

    [DockerRequiredFact]
    public async Task Row_in_the_former_conversion_shape_reads_back_unchanged()
    {
        await using var db = CreateContext();
        var ips = new InvestmentPolicyStatement { UserId = Guid.NewGuid() };
        db.PolicyStatements.Add(ips);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE research.investment_policy_statements SET "Goals" = {0}::jsonb, "AllocationTargets" = {1}::jsonb WHERE "Id" = {2}""",
            LegacyGoals, LegacyTargets, ips.Id);

        await using var read = CreateContext();
        var stored = await read.PolicyStatements.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .SingleAsync(i => i.Id == ips.Id);

        stored.Goals.Should().Equal(
            new InvestmentGoal("House", 120000.5m, new DateOnly(2030, 6, 1), "High"),
            new InvestmentGoal("Rainy day", null, null, "Low"));
        stored.AllocationTargets.Should().Equal(
            new AllocationTarget("Equity", 60.5m, 50m, 70m),
            new AllocationTarget("Cash", 39.5m, 30m, 50m));
    }

    [DockerRequiredFact]
    public async Task Written_lists_keep_the_camelCase_json_shape()
    {
        await using var db = CreateContext();
        var ips = new InvestmentPolicyStatement
        {
            UserId = Guid.NewGuid(),
            Goals =
            [
                new InvestmentGoal("House", 120000.5m, new DateOnly(2030, 6, 1), "High"),
                new InvestmentGoal("Rainy day", null, null, "Low"),
            ],
            AllocationTargets =
            [
                new AllocationTarget("Equity", 60.5m, 50m, 70m),
                new AllocationTarget("Cash", 39.5m, 30m, 50m),
            ],
        };
        db.PolicyStatements.Add(ips);
        await db.SaveChangesAsync();

        // jsonb equality: same elements, names and values as the former conversion wrote (key order and
        // whitespace are not part of a jsonb value).
        var same = await db.Database.SqlQueryRaw<bool>(
            """
            SELECT "Goals" = {0}::jsonb AND "AllocationTargets" = {1}::jsonb AS "Value"
            FROM research.investment_policy_statements WHERE "Id" = {2}
            """,
            LegacyGoals, LegacyTargets, ips.Id).SingleAsync();
        same.Should().BeTrue();
    }
}
