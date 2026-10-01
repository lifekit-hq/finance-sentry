namespace FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>Slim projection used to diff stored documents against current source content without loading text.</summary>
public record ResearchDocumentIdentity(
    Guid Id,
    ResearchDocumentSourceType SourceType,
    string SourceId,
    Guid? UserId,
    string ContentHash,
    ResearchIndexStatus IndexStatus);

public interface IResearchDocumentRepository
{
    /// <summary>Every stored document, owned and shared, for the indexer, which runs with no person in scope. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<ResearchDocumentIdentity>> ListIdentitiesUnscopedAsync(CancellationToken ct = default);

    /// <summary>One stored document by id, whoever owns it, for the indexer. Opts out of the Owner query filter.</summary>
    Task<ResearchDocument?> GetUnscopedAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(ResearchDocument document, CancellationToken ct = default);

    Task UpdateAsync(ResearchDocument document, CancellationToken ct = default);

    /// <summary>Documents awaiting the indexer in the given status, whoever owns them. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<ResearchDocument>> ListByStatusUnscopedAsync(
        ResearchIndexStatus status, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<ResearchChunk>> ListChunksAsync(Guid documentId, CancellationToken ct = default);

    Task AddChunksAsync(IReadOnlyList<ResearchChunk> chunks, CancellationToken ct = default);

    Task RemoveChunksAsync(IReadOnlyList<Guid> chunkIds, CancellationToken ct = default);

    Task<IReadOnlyList<ResearchEmbedding>> ListEmbeddingsForChunksAsync(
        IReadOnlyList<Guid> chunkIds, CancellationToken ct = default);

    Task AddEmbeddingsAsync(IReadOnlyList<ResearchEmbedding> embeddings, CancellationToken ct = default);
}
