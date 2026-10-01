namespace FinanceSentry.Modules.Research.Tests.Retrieval;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

internal static class RetrievalTestContext
{
    /// <summary>A throwaway in-memory context acting as <paramref name="actingUser"/> (null: no person, as a job).</summary>
    public static ResearchDbContext CreateDb(Guid? actingUser = null)
    {
        var options = new DbContextOptionsBuilder<ResearchDbContext>()
            .UseInMemoryDatabase($"retrieval-{Guid.NewGuid():N}")
            .Options;
        return new ResearchDbContext(options, new FixedCurrentUser(actingUser));
    }

    public static IOptions<ResearchRetrievalOptions> CreateOptions(
        Action<ResearchRetrievalOptions>? configure = null)
    {
        var options = new ResearchRetrievalOptions
        {
            Embedding =
            {
                Enabled = true,
                Provider = "fake",
                Model = "fake-model",
                ApiKey = "test-key",
                Dimensions = 3,
            },
        };
        configure?.Invoke(options);
        return Options.Create(options);
    }

    public static ResearchDocument CreateDocument(
        string title,
        string text,
        Guid? userId = null,
        ResearchDocumentSourceType sourceType = ResearchDocumentSourceType.NewsArticle,
        List<string>? tickers = null,
        List<Guid>? thesisIds = null,
        ResearchIndexStatus status = ResearchIndexStatus.Indexed,
        DateTimeOffset? publishedAt = null)
        => new()
        {
            SourceType = sourceType,
            SourceId = Guid.NewGuid().ToString(),
            UserId = userId,
            Title = title,
            Text = text,
            ContentHash = ResearchChunker.ComputeContentHash($"{title}\n{text}"),
            Tickers = tickers ?? [],
            ThesisIds = thesisIds ?? [],
            IndexStatus = status,
            PublishedAt = publishedAt ?? DateTimeOffset.UtcNow.AddDays(-1),
            CapturedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
}

internal sealed class FakeEmbeddingService : IEmbeddingService
{
    public bool IsEnabled { get; set; } = true;

    public string Provider => "fake";

    public string Model => "fake-model";

    public int Dimensions => 3;

    public Dictionary<string, float[]> Vectors { get; } = [];

    public List<string> FailForTextContaining { get; } = [];

    public int EmbedCallCount { get; private set; }

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        EmbedCallCount++;
        var result = new List<float[]>();
        foreach (var text in texts)
        {
            if (FailForTextContaining.Any(text.Contains))
            {
                throw new InvalidOperationException("Embedding provider unavailable.");
            }

            result.Add(Vectors.TryGetValue(text, out var vector) ? vector : [0f, 0f, 0f]);
        }

        return Task.FromResult<IReadOnlyList<float[]>>(result);
    }
}

internal sealed class FakeCorpusSourceReader : IResearchCorpusSourceReader
{
    public List<ResearchDocument> Documents { get; } = [];

    public Task<IReadOnlyList<ResearchDocument>> LoadSourceDocumentsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ResearchDocument>>(Documents
            .Select(CloneProjection)
            .ToList());

    /// <summary>Each load returns fresh instances, mirroring the real reader's re-projection semantics.</summary>
    private static ResearchDocument CloneProjection(ResearchDocument source)
        => new()
        {
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            UserId = source.UserId,
            Title = source.Title,
            CanonicalUrl = source.CanonicalUrl,
            SourceName = source.SourceName,
            PublishedAt = source.PublishedAt,
            CapturedAt = source.CapturedAt,
            ContentHash = source.ContentHash,
            Text = source.Text,
            Tickers = [.. source.Tickers],
            ThesisIds = [.. source.ThesisIds],
        };
}
