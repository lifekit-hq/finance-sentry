namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Integration")]
public sealed class InvestmentThesisJsonbCollectionTests : IAsyncLifetime
{
    private const string LegacyKeyDataPoints = """[{"metric":"gm","direction":"above","threshold":0.55,"source":"10-Q"}]""";
    private const string LegacyCatalysts = """[{"date":"2026-11-20","event":"earnings"}]""";
    private const string LegacyTriggers = """[{"metric":"price_drawdown","direction":"below","threshold":-20,"proxyTicker":null,"consecutivePeriods":2,"periodType":1,"benchmarkTicker":"SPY","windowDays":90}]""";

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
        var thesis = new InvestmentThesis { UserId = Guid.NewGuid(), Ticker = "NVDA", ThesisText = "t" };
        db.Theses.Add(thesis);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE research.theses SET "KeyDataPoints" = {0}::jsonb, "Catalysts" = {1}::jsonb, "InvalidationTriggers" = {2}::jsonb WHERE "Id" = {3}""",
            LegacyKeyDataPoints, LegacyCatalysts, LegacyTriggers, thesis.Id);

        await using var read = CreateContext();
        var stored = await read.Theses.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync(t => t.Id == thesis.Id);

        stored.KeyDataPoints.Should().Equal(new ThesisDataPoint("gm", "above", 0.55m, "10-Q"));
        stored.Catalysts.Should().Equal(new ThesisCatalyst(new DateOnly(2026, 11, 20), "earnings"));
        stored.InvalidationTriggers.Should().Equal(
            new ThesisInvalidationTrigger("price_drawdown", "below", -20m, null, 2, ThesisPeriodType.Annual, "SPY", 90));
    }

    [DockerRequiredFact]
    public async Task Trigger_without_consecutivePeriods_reads_back_as_one_and_stays_evaluable()
    {
        await using var db = CreateContext();
        var thesis = new InvestmentThesis { UserId = Guid.NewGuid(), Ticker = "GRAB", ThesisText = "t" };
        db.Theses.Add(thesis);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE research.theses SET "InvalidationTriggers" = {0}::jsonb WHERE "Id" = {1}""",
            """[{"metric":"gross_margin","direction":"lessThan","threshold":0.4},{"metric":"revenue_yoy","direction":"lessThan","threshold":0.1,"consecutivePeriods":0}]""",
            thesis.Id);

        await using var read = CreateContext();
        var stored = await read.Theses.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync(t => t.Id == thesis.Id);

        stored.InvalidationTriggers.Select(t => t.ConsecutivePeriods).Should().Equal(1, 0);
        ThesisTriggerEvaluability.IsStructurallyEvaluable(stored.InvalidationTriggers[0], out var reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task Thesis_with_empty_lists_survives_a_read_modify_upsert_round_trip()
    {
        var userId = Guid.NewGuid();
        await using (var seed = CreateContext())
        {
            seed.Theses.Add(new InvestmentThesis
            {
                UserId = userId, Ticker = "MU", ThesisText = "t",
                InvalidationTriggers = [new ThesisInvalidationTrigger("gross_margin", "lessThan", 0.35m, null, 1)],
            });
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext();
        var repository = new FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories.ThesisRepository(db);
        var thesis = (await repository.ListUnscopedAsync(userId)).Single();
        thesis.KeyDataPoints.Should().BeEmpty();
        thesis.Catalysts.Should().BeEmpty();

        thesis.BrokenAt = DateTimeOffset.UtcNow;
        thesis.BrokenReason = "gross margin below 35%";
        await repository.UpsertAsync(thesis);

        await using var read = CreateContext();
        var stored = (await new FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories.ThesisRepository(read).ListUnscopedAsync(userId)).Single();
        stored.BrokenReason.Should().Be("gross margin below 35%");
        stored.KeyDataPoints.Should().BeEmpty();
        stored.Catalysts.Should().BeEmpty();
    }

    [DockerRequiredFact]
    public async Task Written_lists_keep_the_camelCase_json_shape()
    {
        await using var db = CreateContext();
        var thesis = new InvestmentThesis
        {
            UserId = Guid.NewGuid(), Ticker = "NVDA", ThesisText = "t",
            KeyDataPoints = [new ThesisDataPoint("gm", "above", 0.55m, "10-Q")],
            Catalysts = [new ThesisCatalyst(new DateOnly(2026, 11, 20), "earnings")],
            InvalidationTriggers = [new ThesisInvalidationTrigger("price_drawdown", "below", -20m, null, 2, ThesisPeriodType.Annual, "SPY", 90)],
        };
        db.Theses.Add(thesis);
        await db.SaveChangesAsync();

        // jsonb equality: same elements, names and values as the former conversion wrote (key order and
        // whitespace are not part of a jsonb value).
        var same = await db.Database.SqlQueryRaw<bool>(
            """
            SELECT "KeyDataPoints" = {0}::jsonb AND "Catalysts" = {1}::jsonb AND "InvalidationTriggers" = {2}::jsonb AS "Value"
            FROM research.theses WHERE "Id" = {3}
            """,
            LegacyKeyDataPoints, LegacyCatalysts, LegacyTriggers, thesis.Id).SingleAsync();
        same.Should().BeTrue();
    }
}
