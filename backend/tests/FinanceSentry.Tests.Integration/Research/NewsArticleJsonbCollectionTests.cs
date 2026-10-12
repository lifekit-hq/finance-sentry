namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// <c>news_articles.Tickers</c>, <c>Categories</c> and <c>ThesisIds</c> are primitive collections stored as a
/// jsonb array. Rows written in the shape the former string conversion produced read back unchanged, and the
/// news repository filters on a list element in the database (so the page limit applies to matching rows only).
/// Real Postgres: the InMemory provider cannot run jsonb element queries.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NewsArticleJsonbCollectionTests : IAsyncLifetime
{
    private static readonly Guid ThesisA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ThesisB = Guid.Parse("22222222-2222-2222-2222-222222222222");

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

    // The JSON text the former string conversion wrote (System.Text.Json, web defaults).
    private async Task SeedLegacyRowAsync(
        string title, string tickersJson, string categoriesJson, string thesisIdsJson, int ageDays)
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO research.news_articles
                ("Id", "Source", "Title", "Url", "ContentHash", "PublishedAt", "Tickers", "Categories", "ThesisIds")
            VALUES (gen_random_uuid(), 'wire', {0}, 'https://example.test/' || {0}, {0}, {1}, {2}::jsonb, {3}::jsonb, {4}::jsonb)
            """,
            title, DateTimeOffset.UtcNow.AddDays(-ageDays), tickersJson, categoriesJson, thesisIdsJson);
    }

    private async Task SeedAsync()
    {
        await SeedLegacyRowAsync("nvda-a", """["NVDA","AMD"]""", """["earnings"]""", $"""["{ThesisA}"]""", 1);
        await SeedLegacyRowAsync("amd-b", """["AMD"]""", "[]", $"""["{ThesisA}","{ThesisB}"]""", 2);
        await SeedLegacyRowAsync("msft-c", """["MSFT"]""", """["macro","fed"]""", "[]", 3);
        await SeedLegacyRowAsync("untagged-d", "[]", "[]", "[]", 4);
    }

    [DockerRequiredFact]
    public async Task Rows_in_the_former_conversion_shape_read_back_unchanged()
    {
        await SeedAsync();

        await using var db = CreateContext();
        var articles = await db.News.AsNoTracking().ToDictionaryAsync(a => a.Title);

        articles["nvda-a"].Tickers.Should().Equal("NVDA", "AMD");
        articles["nvda-a"].Categories.Should().Equal("earnings");
        articles["nvda-a"].ThesisIds.Should().Equal(ThesisA);
        articles["amd-b"].ThesisIds.Should().Equal(ThesisA, ThesisB);
        articles["msft-c"].Categories.Should().Equal("macro", "fed");
        articles["untagged-d"].Tickers.Should().BeEmpty();
        articles["untagged-d"].Categories.Should().BeEmpty();
        articles["untagged-d"].ThesisIds.Should().BeEmpty();
    }

    [DockerRequiredFact]
    public async Task Written_collections_keep_the_jsonb_array_shape()
    {
        await using (var write = CreateContext())
        {
            write.News.Add(new NewsArticle
            {
                Source = "wire",
                Title = "written",
                Url = "https://example.test/written",
                ContentHash = "written",
                PublishedAt = DateTimeOffset.UtcNow,
                Tickers = ["NVDA", "AMD"],
                Categories = ["earnings"],
                ThesisIds = [ThesisA],
            });
            await write.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var raw = await read.Database.SqlQueryRaw<string>(
            """SELECT "Tickers"::text || '|' || "Categories"::text || '|' || "ThesisIds"::text AS "Value" FROM research.news_articles WHERE "Title" = 'written'""")
            .SingleAsync();
        raw.Replace(" ", string.Empty).Should().Be($"""["NVDA","AMD"]|["earnings"]|["{ThesisA}"]""");
    }

    [DockerRequiredFact]
    public async Task Search_filters_on_ticker_and_thesis_elements_in_the_database()
    {
        await SeedAsync();

        await using var db = CreateContext();
        var repository = new NewsRepository(db);

        var byTicker = await repository.SearchAsync(null, ["amd", "msft"], null, null, 10);
        byTicker.Select(a => a.Title).Should().Equal("nvda-a", "amd-b", "msft-c");

        var byThesis = await repository.SearchAsync(null, null, ThesisB, null, 10);
        byThesis.Select(a => a.Title).Should().Equal("amd-b");

        var byBoth = await repository.SearchAsync(null, ["AMD"], ThesisA, null, 10);
        byBoth.Select(a => a.Title).Should().Equal("nvda-a", "amd-b");
    }

    [DockerRequiredFact]
    public async Task Search_limit_counts_matching_rows_only()
    {
        // Newer untagged rows used to crowd the matching one out of a fixed prefilter window.
        for (var i = 0; i < 8; i++)
            await SeedLegacyRowAsync($"noise-{i}", "[]", "[]", "[]", 0);
        await SeedLegacyRowAsync("old-match", """["NVDA"]""", "[]", "[]", 30);

        await using var db = CreateContext();
        var found = await new NewsRepository(db).SearchAsync(null, ["NVDA"], null, null, 1);

        found.Select(a => a.Title).Should().Equal("old-match");
    }

    [DockerRequiredFact]
    public async Task GetForTicker_matches_the_ticker_element_and_honours_since_and_limit()
    {
        await SeedAsync();

        await using var db = CreateContext();
        var repository = new NewsRepository(db);

        var all = await repository.GetForTickerAsync("amd", null, 10);
        all.Select(a => a.Title).Should().Equal("nvda-a", "amd-b");

        var recent = await repository.GetForTickerAsync("AMD", DateTimeOffset.UtcNow.AddHours(-36), 10);
        recent.Select(a => a.Title).Should().Equal("nvda-a");

        var limited = await repository.GetForTickerAsync("AMD", null, 1);
        limited.Select(a => a.Title).Should().Equal("nvda-a");
    }
}
