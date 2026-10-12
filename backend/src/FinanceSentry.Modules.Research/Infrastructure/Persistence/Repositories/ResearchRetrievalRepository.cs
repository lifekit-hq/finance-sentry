namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

public class ResearchRetrievalRepository(
    ResearchDbContext db,
    IOptions<ResearchRetrievalOptions> options) : IResearchRetrievalRepository
{
    public async Task<IReadOnlyList<ResearchChunkCandidate>> ListCandidatesAsync(
        ResearchRetrievalFilter filter, CancellationToken ct = default)
    {
        var opts = options.Value;

        var query = db.ResearchDocuments.AsNoTracking()
            .Where(d => d.IndexStatus == ResearchIndexStatus.Indexed)
            .Where(d => d.UserId == null || d.UserId == filter.UserId);
        if (filter.SourceTypes.Count > 0)
        {
            query = query.Where(d => filter.SourceTypes.Contains(d.SourceType));
        }

        if (filter.From is not null)
        {
            query = query.Where(d => (d.PublishedAt ?? d.CapturedAt) >= filter.From);
        }

        if (filter.To is not null)
        {
            query = query.Where(d => (d.PublishedAt ?? d.CapturedAt) <= filter.To);
        }

        if (filter.Tickers.Count > 0)
        {
            var tickers = filter.Tickers.Select(t => t.ToUpperInvariant()).ToList();
            query = query.Where(d => d.Tickers.Any(t => tickers.Contains(t)));
        }

        if (filter.ThesisId is { } thesisId)
        {
            query = query.Where(d => d.ThesisIds.Contains(thesisId));
        }

        var documents = await query
            .OrderByDescending(d => d.PublishedAt ?? d.CapturedAt)
            .ToListAsync(ct);

        var byDocument = documents.ToDictionary(d => d.Id);
        var documentIds = documents.Select(d => d.Id).ToList();
        var chunks = await db.ResearchChunks.AsNoTracking()
            .Where(c => documentIds.Contains(c.DocumentId))
            .ToListAsync(ct);

        // Cap the candidate set, preferring chunks of the freshest documents.
        var ordered = chunks
            .OrderBy(c => documentIds.IndexOf(c.DocumentId))
            .ThenBy(c => c.Ordinal)
            .Take(opts.MaxSearchCandidates)
            .ToList();

        var chunkIds = ordered.Select(c => c.Id).ToList();
        var embeddings = await db.ResearchEmbeddings.AsNoTracking()
            .Where(e => chunkIds.Contains(e.ChunkId)
                && e.Provider == opts.Embedding.Provider
                && e.Model == opts.Embedding.Model
                && e.EmbeddingVersion == opts.EmbeddingVersion)
            .ToListAsync(ct);
        var embeddingByChunk = embeddings
            .GroupBy(e => e.ChunkId)
            .ToDictionary(g => g.Key, g => g.First());

        return ordered
            .Select(c => new ResearchChunkCandidate(
                byDocument[c.DocumentId],
                c,
                embeddingByChunk.GetValueOrDefault(c.Id)))
            .ToList();
    }
}
