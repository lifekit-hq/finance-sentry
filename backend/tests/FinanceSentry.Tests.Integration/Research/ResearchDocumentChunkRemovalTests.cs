namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Removing a document's chunks (<c>ExecuteDeleteAsync</c>) takes their embeddings with them through the
/// foreign key's cascade and leaves every other chunk and embedding alone; re-indexing a changed document
/// replaces its chunks end to end. Real Postgres: the InMemory provider can neither run a set-based delete nor
/// cascade.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ResearchDocumentChunkRemovalTests : IAsyncLifetime
{
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

    // No person in scope, as in the indexer job.
    private ResearchDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private static ResearchDocument NewDocument(string title, string text) => new()
    {
        SourceType = ResearchDocumentSourceType.NewsArticle,
        SourceId = title,
        Title = title,
        Text = text,
        ContentHash = ResearchChunker.ComputeContentHash($"{title}\n{text}"),
        IndexStatus = ResearchIndexStatus.Pending,
        PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        CapturedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    private static ResearchChunk NewChunk(Guid documentId, int ordinal) => new()
    {
        DocumentId = documentId,
        Ordinal = ordinal,
        Text = $"chunk {ordinal}",
        ContentHash = $"hash-{ordinal}",
    };

    private static ResearchEmbedding NewEmbedding(Guid chunkId) => new()
    {
        ChunkId = chunkId,
        Provider = "fake",
        Model = "fake-model",
        Dimensions = 3,
        EmbeddingVersion = 1,
        Vector = [0f, 0f, 0f],
    };

    [DockerRequiredFact]
    public async Task RemoveChunks_deletes_the_named_chunks_with_their_embeddings_only()
    {
        var document = NewDocument("DRAM update", "Contract pricing improved.");
        var otherDocument = NewDocument("HBM update", "Capacity is sold out.");
        var removed = new[] { NewChunk(document.Id, 0), NewChunk(document.Id, 1) };
        var keptSibling = NewChunk(document.Id, 2);
        var keptOther = NewChunk(otherDocument.Id, 0);
        await using (var seed = CreateContext())
        {
            seed.ResearchDocuments.AddRange(document, otherDocument);
            seed.ResearchChunks.AddRange([.. removed, keptSibling, keptOther]);
            seed.ResearchEmbeddings.AddRange(
                [.. removed.Select(c => NewEmbedding(c.Id)), NewEmbedding(keptSibling.Id), NewEmbedding(keptOther.Id)]);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
            await new ResearchDocumentRepository(ctx).RemoveChunksAsync(removed.Select(c => c.Id).ToList());

        await using var read = CreateContext();
        (await read.ResearchChunks.Select(c => c.Id).ToListAsync())
            .Should().BeEquivalentTo([keptSibling.Id, keptOther.Id]);
        (await read.ResearchEmbeddings.Select(e => e.ChunkId).ToListAsync())
            .Should().BeEquivalentTo([keptSibling.Id, keptOther.Id]);
        (await read.ResearchDocuments.CountAsync()).Should().Be(2);
    }

    [DockerRequiredFact]
    public async Task RemoveChunks_with_no_ids_deletes_nothing()
    {
        var document = NewDocument("DRAM update", "Contract pricing improved.");
        var chunk = NewChunk(document.Id, 0);
        await using (var seed = CreateContext())
        {
            seed.ResearchDocuments.Add(document);
            seed.ResearchChunks.Add(chunk);
            seed.ResearchEmbeddings.Add(NewEmbedding(chunk.Id));
            await seed.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
            await new ResearchDocumentRepository(ctx).RemoveChunksAsync([]);

        await using var read = CreateContext();
        (await read.ResearchChunks.CountAsync()).Should().Be(1);
        (await read.ResearchEmbeddings.CountAsync()).Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task Index_MarksChangedContentPending_AndReindexes()
    {
        var reader = new StubCorpusSourceReader();
        reader.Documents.Add(NewDocument("DRAM update", "Original text about contract pricing."));
        await using var ctx = CreateContext();
        var options = Options.Create(new ResearchRetrievalOptions
        {
            Embedding = { Enabled = true, Provider = "fake", Model = "fake-model", ApiKey = "test-key", Dimensions = 3 },
        });
        var indexer = new ResearchIndexer(
            reader,
            new ResearchDocumentRepository(ctx),
            new ResearchChunker(options),
            new StubEmbeddingService(),
            options,
            NullLogger<ResearchIndexer>.Instance);
        await indexer.IndexPendingAsync();
        var originalChunkIds = await ctx.ResearchChunks.Select(c => c.Id).ToListAsync();

        reader.Documents.Clear();
        reader.Documents.Add(NewDocument("DRAM update", "Fully revised text about a pricing correction."));
        var result = await indexer.IndexPendingAsync();

        result.Synced.Should().Be(1);
        result.Indexed.Should().Be(1);
        var document = await ctx.ResearchDocuments.SingleAsync();
        document.Text.Should().Contain("revised");
        document.IndexStatus.Should().Be(ResearchIndexStatus.Indexed);
        var chunks = await ctx.ResearchChunks.AsNoTracking().ToListAsync();
        chunks.Should().NotBeEmpty().And.OnlyContain(c => c.Text.Contains("revised"));
        chunks.Select(c => c.Id).Should().NotIntersectWith(originalChunkIds);
        (await ctx.ResearchEmbeddings.AsNoTracking().Select(e => e.ChunkId).ToListAsync())
            .Should().BeEquivalentTo(chunks.Select(c => c.Id), "the old chunks' embeddings are gone, the new ones are in");
    }

    private sealed class StubCorpusSourceReader : IResearchCorpusSourceReader
    {
        public List<ResearchDocument> Documents { get; } = [];

        public Task<IReadOnlyList<ResearchDocument>> LoadSourceDocumentsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchDocument>>([.. Documents.Select(Project)]);

        // Each load returns fresh instances, mirroring the real reader's re-projection.
        private static ResearchDocument Project(ResearchDocument source) => new()
        {
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            Title = source.Title,
            PublishedAt = source.PublishedAt,
            CapturedAt = source.CapturedAt,
            ContentHash = source.ContentHash,
            Text = source.Text,
        };
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public bool IsEnabled => true;

        public string Provider => "fake";

        public string Model => "fake-model";

        public int Dimensions => 3;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(_ => new float[] { 0f, 0f, 0f })]);
    }
}
