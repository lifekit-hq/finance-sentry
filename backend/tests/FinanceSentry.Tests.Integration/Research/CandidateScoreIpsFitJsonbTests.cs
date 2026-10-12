namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// <c>candidate_scores.IpsFit</c> is a single-object jsonb column on a string conversion with the
/// <see cref="IpsFitFacts.Unknown"/> fallback. It stays there: a JSON-mapped complex property reads fine on
/// Postgres but the EF InMemory provider that the Research and MCP fixtures run on cannot compile a
/// candidate-score query for it (KeyNotFoundException in its shaper). This pins the stored camelCase shape and
/// the fallback for JSON <c>null</c> so a later move cannot drift. Real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CandidateScoreIpsFitJsonbTests : IAsyncLifetime
{
    private const string LegacyIpsFit =
        """{"currentWeight":2.5,"wouldBeWeight":4.75,"maxSinglePositionPct":10,"withinConcentration":false,"assetClassFit":"overweight","note":"above the cap"}""";
    private const string LegacyIpsFitNulls =
        """{"currentWeight":null,"wouldBeWeight":null,"maxSinglePositionPct":null,"withinConcentration":true,"assetClassFit":"unknown","note":null}""";

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

    private async Task<Guid> SeedRowAsync(string? ipsFitJson)
    {
        await using var db = CreateContext();
        var score = new CandidateScore { CandidateId = Guid.NewGuid() };
        db.CandidateScores.Add(score);
        await db.SaveChangesAsync();

        if (ipsFitJson is not null)
        {
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE research.candidate_scores SET "IpsFit" = {0}::jsonb WHERE "Id" = {1}""", ipsFitJson, score.Id);
        }

        return score.Id;
    }

    private async Task<CandidateScore> ReadAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.CandidateScores.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    [DockerRequiredFact]
    public async Task Row_in_the_former_conversion_shape_reads_back_unchanged()
    {
        var id = await SeedRowAsync(LegacyIpsFit);

        var stored = await ReadAsync(id);

        stored.IpsFit.Should().Be(new IpsFitFacts(2.5m, 4.75m, 10m, false, "overweight", "above the cap"));
    }

    [DockerRequiredFact]
    public async Task Row_with_null_members_reads_back_unchanged()
    {
        var id = await SeedRowAsync(LegacyIpsFitNulls);

        var stored = await ReadAsync(id);

        stored.IpsFit.Should().Be(IpsFitFacts.Unknown);
    }

    [DockerRequiredFact]
    public async Task Written_facts_keep_the_camelCase_json_shape()
    {
        await using var db = CreateContext();
        var score = new CandidateScore
        {
            CandidateId = Guid.NewGuid(),
            IpsFit = new IpsFitFacts(2.5m, 4.75m, 10m, false, "overweight", "above the cap"),
        };
        db.CandidateScores.Add(score);
        await db.SaveChangesAsync();

        // jsonb equality: same names and values as the former conversion wrote (key order and whitespace are
        // not part of a jsonb value).
        var same = await db.Database.SqlQueryRaw<bool>(
            """SELECT "IpsFit" = {0}::jsonb AS "Value" FROM research.candidate_scores WHERE "Id" = {1}""",
            LegacyIpsFit, score.Id).SingleAsync();
        same.Should().BeTrue();
    }

    [DockerRequiredFact]
    public async Task Null_json_in_the_column_reads_back_as_unknown_facts()
    {
        var id = await SeedRowAsync("null");

        var stored = await ReadAsync(id);

        stored.IpsFit.Should().Be(IpsFitFacts.Unknown);
    }

    [DockerRequiredFact]
    public async Task Default_score_round_trips_with_unknown_facts()
    {
        await using var write = CreateContext();
        var score = new CandidateScore { CandidateId = Guid.NewGuid() };
        write.CandidateScores.Add(score);
        await write.SaveChangesAsync();

        var stored = await ReadAsync(score.Id);

        stored.IpsFit.Should().Be(IpsFitFacts.Unknown);
    }
}
