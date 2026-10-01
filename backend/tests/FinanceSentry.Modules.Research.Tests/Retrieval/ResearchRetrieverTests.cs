namespace FinanceSentry.Modules.Research.Tests.Retrieval;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class ResearchRetrieverTests
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private static async Task<Guid> SeedDocumentWithChunkAsync(
        ResearchDbContext db, ResearchDocument document, float[]? vector = null)
    {
        db.ResearchDocuments.Add(document);
        var chunk = new ResearchChunk
        {
            DocumentId = document.Id,
            Ordinal = 0,
            Text = document.Text,
            ContentHash = ResearchChunker.ComputeContentHash(document.Text),
            TokenEstimate = document.Text.Length / 4,
        };
        db.ResearchChunks.Add(chunk);
        if (vector is not null)
        {
            db.ResearchEmbeddings.Add(new ResearchEmbedding
            {
                ChunkId = chunk.Id,
                Provider = "fake",
                Model = "fake-model",
                Dimensions = vector.Length,
                EmbeddingVersion = 1,
                Vector = vector,
            });
        }

        await db.SaveChangesAsync();
        return chunk.Id;
    }

    private static ResearchRetriever CreateRetriever(ResearchDbContext db, FakeEmbeddingService embeddings)
    {
        var options = RetrievalTestContext.CreateOptions();
        return new ResearchRetriever(
            new ResearchRetrievalRepository(db, options),
            embeddings,
            options,
            NullLogger<ResearchRetriever>.Instance);
    }

    private static ResearchRetrievalRequest Request(
        Guid userId, string query, IReadOnlyList<string>? tickers = null, Guid? thesisId = null, int limit = 10)
        => new(userId, query, tickers ?? [], thesisId, [], null, null, limit);

    [Fact]
    public async Task Search_ReturnsSemanticallyRelevantChunk_WithoutKeywordOverlap()
    {
        using var db = RetrievalTestContext.CreateDb();
        var dram = RetrievalTestContext.CreateDocument(
            "Contract prices continue to rise",
            "Suppliers report another quarter of improving contract conditions across the industry.",
            tickers: ["MU"]);
        var airline = RetrievalTestContext.CreateDocument(
            "Airline fuel costs surge",
            "Carriers face higher jet fuel expenses squeezing operating margins this summer.");
        var dramChunkId = await SeedDocumentWithChunkAsync(db, dram, [0.9f, 0.1f, 0f]);
        await SeedDocumentWithChunkAsync(db, airline, [0f, 1f, 0f]);

        var embeddings = new FakeEmbeddingService();
        embeddings.Vectors["memory cycle recovery evidence"] = [1f, 0f, 0f];
        var retriever = CreateRetriever(db, embeddings);

        var result = await retriever.SearchAsync(Request(UserA, "memory cycle recovery evidence"));

        result.Hits.Should().NotBeEmpty();
        result.Hits[0].Chunk.Id.Should().Be(dramChunkId);
        result.Hits[0].SemanticScore.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public async Task Search_ExcludesOtherUsersPrivateDocuments()
    {
        using var db = RetrievalTestContext.CreateDb();
        var privateNote = RetrievalTestContext.CreateDocument(
            "MU private thesis note", "My private conviction notes about memory pricing.",
            userId: UserA, sourceType: ResearchDocumentSourceType.InvestmentThesis);
        var globalArticle = RetrievalTestContext.CreateDocument(
            "Memory pricing update", "Public market coverage of memory pricing trends.");
        await SeedDocumentWithChunkAsync(db, privateNote);
        await SeedDocumentWithChunkAsync(db, globalArticle);

        var retriever = CreateRetriever(db, new FakeEmbeddingService { IsEnabled = false });

        var result = await retriever.SearchAsync(Request(UserB, "memory pricing"));

        result.Hits.Should().NotBeEmpty();
        result.Hits.Should().OnlyContain(h => h.Document.UserId == null);
    }

    [Fact]
    public async Task Search_ReturnsOwnPrivateDocuments_ToOwner()
    {
        using var db = RetrievalTestContext.CreateDb(UserA);
        var privateNote = RetrievalTestContext.CreateDocument(
            "MU private thesis note", "My private conviction notes about memory pricing.",
            userId: UserA, sourceType: ResearchDocumentSourceType.InvestmentThesis);
        await SeedDocumentWithChunkAsync(db, privateNote);

        var retriever = CreateRetriever(db, new FakeEmbeddingService { IsEnabled = false });

        var result = await retriever.SearchAsync(Request(UserA, "memory pricing"));

        result.Hits.Should().ContainSingle(h => h.Document.UserId == UserA);
    }

    [Fact]
    public async Task Search_FiltersByTicker()
    {
        using var db = RetrievalTestContext.CreateDb();
        var mu = RetrievalTestContext.CreateDocument(
            "MU coverage", "Memory pricing recovery continues.", tickers: ["MU"]);
        var aapl = RetrievalTestContext.CreateDocument(
            "AAPL coverage", "Memory pricing affects handset margins.", tickers: ["AAPL"]);
        await SeedDocumentWithChunkAsync(db, mu);
        await SeedDocumentWithChunkAsync(db, aapl);

        var retriever = CreateRetriever(db, new FakeEmbeddingService { IsEnabled = false });

        var result = await retriever.SearchAsync(Request(UserA, "memory pricing", tickers: ["mu"]));

        result.Hits.Should().NotBeEmpty();
        result.Hits.Should().OnlyContain(h => h.Document.Tickers.Contains("MU"));
    }

    [Fact]
    public async Task Search_IgnoresUnindexedDocuments()
    {
        using var db = RetrievalTestContext.CreateDb();
        var pending = RetrievalTestContext.CreateDocument(
            "Pending doc", "Memory pricing text.", status: ResearchIndexStatus.Pending);
        await SeedDocumentWithChunkAsync(db, pending);

        var retriever = CreateRetriever(db, new FakeEmbeddingService { IsEnabled = false });

        var result = await retriever.SearchAsync(Request(UserA, "memory pricing"));

        result.Hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_FallsBackToLexical_WhenQueryEmbeddingFails()
    {
        using var db = RetrievalTestContext.CreateDb();
        var doc = RetrievalTestContext.CreateDocument("Memory pricing", "Memory pricing recovery continues.");
        await SeedDocumentWithChunkAsync(db, doc, [0.5f, 0.5f, 0f]);

        var embeddings = new FakeEmbeddingService();
        embeddings.FailForTextContaining.Add("memory");
        var retriever = CreateRetriever(db, embeddings);

        var result = await retriever.SearchAsync(Request(UserA, "memory pricing"));

        result.Hits.Should().NotBeEmpty();
        result.Hits[0].SemanticScore.Should().Be(0);
        result.Hits[0].LexicalScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Search_RespectsLimit_AndReportsTotalMatched()
    {
        using var db = RetrievalTestContext.CreateDb();
        for (var i = 0; i < 5; i++)
        {
            await SeedDocumentWithChunkAsync(db, RetrievalTestContext.CreateDocument(
                $"Memory article {i}", $"Memory pricing recovery item number {i}."));
        }

        var retriever = CreateRetriever(db, new FakeEmbeddingService { IsEnabled = false });

        var result = await retriever.SearchAsync(Request(UserA, "memory pricing", limit: 2));

        result.Hits.Should().HaveCount(2);
        result.TotalMatched.Should().Be(5);
    }

    [Fact]
    public async Task Search_ReturnsEmpty_ForBlankQuery()
    {
        using var db = RetrievalTestContext.CreateDb();
        var retriever = CreateRetriever(db, new FakeEmbeddingService { IsEnabled = false });

        var result = await retriever.SearchAsync(Request(UserA, "   "));

        result.Hits.Should().BeEmpty();
        result.TotalMatched.Should().Be(0);
    }
}
