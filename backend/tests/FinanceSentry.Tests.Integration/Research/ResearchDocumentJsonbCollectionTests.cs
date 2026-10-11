namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// <c>research_documents.Tickers</c> and <c>ThesisIds</c> are primitive collections stored as a jsonb array.
/// Rows written in the shape the former string conversion produced read back unchanged, and the retrieval
/// repository filters on a list element in the database. Real Postgres: the InMemory provider cannot run
/// jsonb element queries.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ResearchDocumentJsonbCollectionTests : IAsyncLifetime
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ThesisA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ThesisB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext(null);
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private ResearchDbContext CreateContext(Guid? userId) =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(userId));

    // The JSON text the former string conversion wrote (System.Text.Json, web defaults).
    private async Task SeedLegacyRowAsync(string sourceId, string tickersJson, string thesisIdsJson, int ageDays)
    {
        await using var db = CreateContext(null);
        var capturedAt = DateTimeOffset.UtcNow.AddDays(-ageDays);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO research.research_documents
                ("Id", "SourceType", "SourceId", "Title", "ContentHash", "Text", "CapturedAt", "IndexStatus", "Tickers", "ThesisIds")
            VALUES (gen_random_uuid(), 'NewsArticle', {0}, {0}, {0}, 'body', {1}, 'Indexed', {2}::jsonb, {3}::jsonb)
            """,
            sourceId, capturedAt, tickersJson, thesisIdsJson);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO research.research_chunks ("Id", "DocumentId", "Ordinal", "Text", "ContentHash", "TokenEstimate")
            SELECT gen_random_uuid(), "Id", 0, 'chunk', 'h', 1 FROM research.research_documents WHERE "SourceId" = {0}
            """,
            sourceId);
    }

    private async Task SeedAsync()
    {
        await SeedLegacyRowAsync("nvda-a", """["NVDA","AMD"]""", $"""["{ThesisA}"]""", 1);
        await SeedLegacyRowAsync("amd-b", """["AMD"]""", $"""["{ThesisA}","{ThesisB}"]""", 2);
        await SeedLegacyRowAsync("msft-c", """["MSFT"]""", "[]", 3);
        await SeedLegacyRowAsync("untagged-d", "[]", "[]", 4);
    }

    private ResearchRetrievalRepository CreateRepository(ResearchDbContext db) =>
        new(db, Options.Create(new ResearchRetrievalOptions()));

    private static ResearchRetrievalFilter Filter(string[] tickers, Guid? thesisId) =>
        new(UserId, tickers, thesisId, [], null, null);

    [DockerRequiredFact]
    public async Task Rows_in_the_former_conversion_shape_read_back_unchanged()
    {
        await SeedAsync();

        await using var db = CreateContext(null);
        var documents = await db.ResearchDocuments.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .ToDictionaryAsync(d => d.SourceId);

        documents["nvda-a"].Tickers.Should().Equal("NVDA", "AMD");
        documents["nvda-a"].ThesisIds.Should().Equal(ThesisA);
        documents["amd-b"].ThesisIds.Should().Equal(ThesisA, ThesisB);
        documents["untagged-d"].Tickers.Should().BeEmpty();
        documents["untagged-d"].ThesisIds.Should().BeEmpty();
    }

    [DockerRequiredFact]
    public async Task Written_collections_keep_the_jsonb_array_shape()
    {
        await using (var write = CreateContext(null))
        {
            write.ResearchDocuments.Add(new ResearchDocument
            {
                SourceType = ResearchDocumentSourceType.NewsArticle,
                SourceId = "written",
                Title = "written",
                ContentHash = "written",
                Text = "body",
                Tickers = ["NVDA", "AMD"],
                ThesisIds = [ThesisA],
            });
            await write.SaveChangesAsync();
        }

        await using var read = CreateContext(null);
        var raw = await read.Database.SqlQueryRaw<string>(
            """SELECT "Tickers"::text || '|' || "ThesisIds"::text AS "Value" FROM research.research_documents WHERE "SourceId" = 'written'""")
            .SingleAsync();
        raw.Replace(" ", string.Empty).Should().Be($"""["NVDA","AMD"]|["{ThesisA}"]""");
    }

    [DockerRequiredFact]
    public async Task Ticker_filter_matches_on_a_list_element()
    {
        await SeedAsync();

        await using var db = CreateContext(UserId);
        var candidates = await CreateRepository(db).ListCandidatesAsync(Filter(["amd", "msft"], null));

        candidates.Select(c => c.Document.SourceId).Should().BeEquivalentTo("nvda-a", "amd-b", "msft-c");
        candidates.Select(c => c.Document.SourceId).Should().NotContain("untagged-d");
    }

    [DockerRequiredFact]
    public async Task Thesis_filter_matches_on_a_list_element_and_keeps_newest_first()
    {
        await SeedAsync();

        await using var db = CreateContext(UserId);
        var candidates = await CreateRepository(db).ListCandidatesAsync(Filter([], ThesisB));
        candidates.Select(c => c.Document.SourceId).Should().Equal("amd-b");

        var both = await CreateRepository(db).ListCandidatesAsync(Filter(["NVDA"], ThesisA));
        both.Select(c => c.Document.SourceId).Should().Equal("nvda-a");

        var byThesis = await CreateRepository(db).ListCandidatesAsync(Filter([], ThesisA));
        byThesis.Select(c => c.Document.SourceId).Should().Equal("nvda-a", "amd-b");
    }
}
