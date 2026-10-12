namespace FinanceSentry.Modules.Research.Tests.Retrieval;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class ResearchIndexerTests
{
    private static ResearchIndexer CreateIndexer(
        ResearchDbContext db, FakeCorpusSourceReader sourceReader, FakeEmbeddingService embeddings)
    {
        var options = RetrievalTestContext.CreateOptions();
        return new ResearchIndexer(
            sourceReader,
            new ResearchDocumentRepository(db),
            new ResearchChunker(options),
            embeddings,
            options,
            NullLogger<ResearchIndexer>.Instance);
    }

    private static ResearchDocument SourceProjection(string title, string text)
    {
        var document = RetrievalTestContext.CreateDocument(title, text, status: ResearchIndexStatus.Pending);
        document.SourceId = title;
        return document;
    }

    [Fact]
    public async Task Index_CreatesDocumentChunksAndEmbeddings_ForNewSource()
    {
        using var db = RetrievalTestContext.CreateDb();
        var reader = new FakeCorpusSourceReader();
        reader.Documents.Add(SourceProjection("DRAM update", "Contract pricing improved for another quarter."));
        var indexer = CreateIndexer(db, reader, new FakeEmbeddingService());

        var result = await indexer.IndexPendingAsync();

        result.Indexed.Should().Be(1);
        var document = await db.ResearchDocuments.SingleAsync();
        document.IndexStatus.Should().Be(ResearchIndexStatus.Indexed);
        document.IndexedAt.Should().NotBeNull();
        (await db.ResearchChunks.CountAsync()).Should().BeGreaterThan(0);
        (await db.ResearchEmbeddings.CountAsync()).Should().Be(await db.ResearchChunks.CountAsync());
    }

    [Fact]
    public async Task Index_IsIdempotent_ForUnchangedContent()
    {
        using var db = RetrievalTestContext.CreateDb();
        var reader = new FakeCorpusSourceReader();
        reader.Documents.Add(SourceProjection("DRAM update", "Contract pricing improved for another quarter."));
        var embeddings = new FakeEmbeddingService();
        var indexer = CreateIndexer(db, reader, embeddings);

        await indexer.IndexPendingAsync();
        var chunkIds = await db.ResearchChunks.Select(c => c.Id).ToListAsync();
        var embeddingCount = await db.ResearchEmbeddings.CountAsync();
        var embedCalls = embeddings.EmbedCallCount;

        var second = await indexer.IndexPendingAsync();

        second.Processed.Should().Be(0, "unchanged documents must stay Indexed and out of the work queue");
        (await db.ResearchDocuments.CountAsync()).Should().Be(1);
        (await db.ResearchChunks.Select(c => c.Id).ToListAsync()).Should().BeEquivalentTo(chunkIds);
        (await db.ResearchEmbeddings.CountAsync()).Should().Be(embeddingCount);
        embeddings.EmbedCallCount.Should().Be(embedCalls);
    }

    [Fact]
    public async Task Index_IsolatesEmbeddingFailures_PerDocument()
    {
        using var db = RetrievalTestContext.CreateDb();
        var reader = new FakeCorpusSourceReader();
        reader.Documents.Add(SourceProjection("Poison doc", "poison text that breaks the provider."));
        reader.Documents.Add(SourceProjection("Healthy doc", "Perfectly fine research text."));
        var embeddings = new FakeEmbeddingService();
        embeddings.FailForTextContaining.Add("poison");
        var indexer = CreateIndexer(db, reader, embeddings);

        var result = await indexer.IndexPendingAsync();

        result.Failed.Should().Be(1);
        result.Indexed.Should().Be(1);
        var failed = await db.ResearchDocuments.SingleAsync(d => d.Title == "Poison doc");
        failed.IndexStatus.Should().Be(ResearchIndexStatus.Failed);
        failed.IndexFailureReason.Should().NotBeNullOrEmpty();
        var healthy = await db.ResearchDocuments.SingleAsync(d => d.Title == "Healthy doc");
        healthy.IndexStatus.Should().Be(ResearchIndexStatus.Indexed);
    }

    [Fact]
    public async Task Index_RetriesFailedDocuments_OnNextRun()
    {
        using var db = RetrievalTestContext.CreateDb();
        var reader = new FakeCorpusSourceReader();
        reader.Documents.Add(SourceProjection("Flaky doc", "poison text this run only."));
        var embeddings = new FakeEmbeddingService();
        embeddings.FailForTextContaining.Add("poison");
        var indexer = CreateIndexer(db, reader, embeddings);
        await indexer.IndexPendingAsync();

        embeddings.FailForTextContaining.Clear();
        var result = await indexer.IndexPendingAsync();

        result.Indexed.Should().Be(1);
        (await db.ResearchDocuments.SingleAsync()).IndexStatus.Should().Be(ResearchIndexStatus.Indexed);
    }

    [Fact]
    public async Task Index_SkipsDocumentsWithoutUsableText()
    {
        using var db = RetrievalTestContext.CreateDb();
        var reader = new FakeCorpusSourceReader();
        reader.Documents.Add(SourceProjection("Empty doc", "   "));
        var indexer = CreateIndexer(db, reader, new FakeEmbeddingService());

        var result = await indexer.IndexPendingAsync();

        result.Skipped.Should().Be(1);
        var document = await db.ResearchDocuments.SingleAsync();
        document.IndexStatus.Should().Be(ResearchIndexStatus.Skipped);
        document.IndexFailureReason.Should().NotBeNullOrEmpty();
        (await db.ResearchChunks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Index_StoresChunksWithoutEmbeddings_WhenProviderDisabled()
    {
        using var db = RetrievalTestContext.CreateDb();
        var reader = new FakeCorpusSourceReader();
        reader.Documents.Add(SourceProjection("DRAM update", "Contract pricing improved for another quarter."));
        var indexer = CreateIndexer(db, reader, new FakeEmbeddingService { IsEnabled = false });

        var result = await indexer.IndexPendingAsync();

        result.Indexed.Should().Be(1);
        (await db.ResearchChunks.CountAsync()).Should().BeGreaterThan(0);
        (await db.ResearchEmbeddings.CountAsync()).Should().Be(0);
    }
}
