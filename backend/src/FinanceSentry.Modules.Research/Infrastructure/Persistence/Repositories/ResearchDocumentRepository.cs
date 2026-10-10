namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// The indexer is the only caller and works over the whole corpus with no person in scope, so its document
// reads opt out of the Owner query filter. Chunks and embeddings carry no owner and are reached by document id.
public class ResearchDocumentRepository(ResearchDbContext db) : IResearchDocumentRepository
{
    public async Task<IReadOnlyList<ResearchDocumentIdentity>> ListIdentitiesUnscopedAsync(CancellationToken ct = default)
        => await db.ResearchDocuments.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Select(d => new ResearchDocumentIdentity(
                d.Id, d.SourceType, d.SourceId, d.UserId, d.ContentHash, d.IndexStatus))
            .ToListAsync(ct);

    public Task<ResearchDocument?> GetUnscopedAsync(Guid id, CancellationToken ct = default)
        => db.ResearchDocuments.IgnoreQueryFilters([OwnerQueryFilter.Name]).FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task AddAsync(ResearchDocument document, CancellationToken ct = default)
    {
        db.ResearchDocuments.Add(document);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ResearchDocument document, CancellationToken ct = default)
    {
        db.ResearchDocuments.Update(document);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ResearchDocument>> ListByStatusUnscopedAsync(
        ResearchIndexStatus status, int limit, CancellationToken ct = default)
        => await db.ResearchDocuments
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(d => d.IndexStatus == status)
            .OrderBy(d => d.CapturedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ResearchChunk>> ListChunksAsync(Guid documentId, CancellationToken ct = default)
        => await db.ResearchChunks.AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.Ordinal)
            .ToListAsync(ct);

    public async Task AddChunksAsync(IReadOnlyList<ResearchChunk> chunks, CancellationToken ct = default)
    {
        db.ResearchChunks.AddRange(chunks);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveChunksAsync(IReadOnlyList<Guid> chunkIds, CancellationToken ct = default)
    {
        // One DELETE; the embeddings go with their chunks through the research_embeddings → research_chunks
        // ON DELETE CASCADE foreign key.
        await db.ResearchChunks.Where(c => chunkIds.Contains(c.Id)).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<ResearchEmbedding>> ListEmbeddingsForChunksAsync(
        IReadOnlyList<Guid> chunkIds, CancellationToken ct = default)
        => await db.ResearchEmbeddings.AsNoTracking()
            .Where(e => chunkIds.Contains(e.ChunkId))
            .ToListAsync(ct);

    public async Task AddEmbeddingsAsync(IReadOnlyList<ResearchEmbedding> embeddings, CancellationToken ct = default)
    {
        db.ResearchEmbeddings.AddRange(embeddings);
        await db.SaveChangesAsync(ct);
    }
}
