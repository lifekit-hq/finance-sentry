namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class ResearchIndexer(
    IResearchCorpusSourceReader sourceReader,
    IResearchDocumentRepository documents,
    IResearchChunker chunker,
    IEmbeddingService embeddings,
    IOptions<ResearchRetrievalOptions> options,
    ILogger<ResearchIndexer> logger) : IResearchIndexer
{
    public async Task<ResearchIndexingResult> IndexPendingAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var synced = await SyncSourceDocumentsAsync(ct);

        var processed = 0;
        var indexed = 0;
        var skipped = 0;
        var failed = 0;
        var attempted = new HashSet<Guid>();

        foreach (var status in new[] { ResearchIndexStatus.Pending, ResearchIndexStatus.Failed })
        {
            while (processed < opts.MaxDocumentsPerRun)
            {
                ct.ThrowIfCancellationRequested();
                // A document is attempted at most once per run: without this guard a document that
                // fails in the Pending pass would be retried (and re-counted) by the Failed pass.
                var batch = (await documents.ListByStatusUnscopedAsync(status, opts.IndexingBatchSize, ct))
                    .Where(d => attempted.Add(d.Id))
                    .ToList();
                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var document in batch)
                {
                    processed++;
                    var outcome = await IndexDocumentAsync(document, ct);
                    switch (outcome)
                    {
                        case ResearchIndexStatus.Indexed: indexed++; break;
                        case ResearchIndexStatus.Skipped: skipped++; break;
                        default: failed++; break;
                    }
                }

                if (batch.Count < opts.IndexingBatchSize)
                {
                    break;
                }
            }
        }

        logger.LogInformation(
            "Research indexing run: {Synced} synced, {Processed} processed, {Indexed} indexed, {Skipped} skipped, {Failed} failed",
            synced, processed, indexed, skipped, failed);
        return new ResearchIndexingResult(synced, processed, indexed, skipped, failed);
    }

    /// <summary>
    /// Upserts source projections into research documents. New sources are added as Pending;
    /// documents whose source content hash changed are updated and reset to Pending.
    /// </summary>
    private async Task<int> SyncSourceDocumentsAsync(CancellationToken ct)
    {
        var identities = await documents.ListIdentitiesUnscopedAsync(ct);
        var byIdentity = identities.ToDictionary(i => (i.SourceType, i.SourceId, i.UserId));
        var sourceDocuments = await sourceReader.LoadSourceDocumentsAsync(ct);

        var synced = 0;
        foreach (var source in sourceDocuments)
        {
            ct.ThrowIfCancellationRequested();
            if (!byIdentity.TryGetValue((source.SourceType, source.SourceId, source.UserId), out var existing))
            {
                await documents.AddAsync(source, ct);
                synced++;
                continue;
            }

            if (existing.ContentHash == source.ContentHash)
            {
                continue;
            }

            var stored = await documents.GetUnscopedAsync(existing.Id, ct);
            if (stored is null)
            {
                continue;
            }

            stored.Title = source.Title;
            stored.CanonicalUrl = source.CanonicalUrl;
            stored.SourceName = source.SourceName;
            stored.PublishedAt = source.PublishedAt;
            stored.CapturedAt = source.CapturedAt;
            stored.ContentHash = source.ContentHash;
            stored.Text = source.Text;
            stored.Tickers = source.Tickers;
            stored.ThesisIds = source.ThesisIds;
            stored.IndexStatus = ResearchIndexStatus.Pending;
            stored.IndexFailureReason = null;
            await documents.UpdateAsync(stored, ct);
            synced++;
        }

        return synced;
    }

    private async Task<ResearchIndexStatus> IndexDocumentAsync(ResearchDocument document, CancellationToken ct)
    {
        try
        {
            var chunks = chunker.Chunk(document);
            if (chunks.Count == 0)
            {
                return await FinishAsync(document, ResearchIndexStatus.Skipped, "Document has no usable text.", ct);
            }

            var stored = await ReconcileChunksAsync(document, chunks, ct);
            if (embeddings.IsEnabled)
            {
                await EmbedMissingAsync(stored, ct);
            }

            return await FinishAsync(document, ResearchIndexStatus.Indexed, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Indexing failed for research document {DocumentId} ({Title})", document.Id, document.Title);
            return await FinishAsync(document, ResearchIndexStatus.Failed, ex.Message, ct);
        }
    }

    /// <summary>Keeps chunk rows identical for unchanged content; otherwise replaces the document's chunks.</summary>
    private async Task<IReadOnlyList<ResearchChunk>> ReconcileChunksAsync(
        ResearchDocument document, IReadOnlyList<ResearchChunk> chunks, CancellationToken ct)
    {
        var existing = await documents.ListChunksAsync(document.Id, ct);
        var existingKeys = existing.Select(c => (c.Ordinal, c.ContentHash)).ToHashSet();
        var freshKeys = chunks.Select(c => (c.Ordinal, c.ContentHash)).ToHashSet();
        if (existingKeys.SetEquals(freshKeys) && existing.Count == chunks.Count)
        {
            return existing;
        }

        if (existing.Count > 0)
        {
            await documents.RemoveChunksAsync(existing.Select(c => c.Id).ToList(), ct);
        }

        await documents.AddChunksAsync(chunks, ct);
        return chunks;
    }

    private async Task EmbedMissingAsync(IReadOnlyList<ResearchChunk> chunks, CancellationToken ct)
    {
        var chunkIds = chunks.Select(c => c.Id).ToList();
        var existing = await documents.ListEmbeddingsForChunksAsync(chunkIds, ct);
        var covered = existing
            .Where(e => e.Provider == embeddings.Provider
                && e.Model == embeddings.Model
                && e.EmbeddingVersion == options.Value.EmbeddingVersion)
            .Select(e => e.ChunkId)
            .ToHashSet();
        var missing = chunks.Where(c => !covered.Contains(c.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var batchSize = Math.Max(1, options.Value.Embedding.BatchSize);
        foreach (var batch in missing.Chunk(batchSize))
        {
            ct.ThrowIfCancellationRequested();
            var vectors = await embeddings.EmbedAsync(batch.Select(c => c.Text).ToList(), ct);
            var rows = batch.Select((chunk, i) => new ResearchEmbedding
            {
                ChunkId = chunk.Id,
                Provider = embeddings.Provider,
                Model = embeddings.Model,
                Dimensions = vectors[i].Length,
                EmbeddingVersion = options.Value.EmbeddingVersion,
                Vector = vectors[i],
            }).ToList();
            await documents.AddEmbeddingsAsync(rows, ct);
        }
    }

    private async Task<ResearchIndexStatus> FinishAsync(
        ResearchDocument document, ResearchIndexStatus status, string? reason, CancellationToken ct)
    {
        document.IndexStatus = status;
        document.IndexFailureReason = reason;
        document.IndexedAt = status == ResearchIndexStatus.Indexed ? DateTimeOffset.UtcNow : document.IndexedAt;
        await documents.UpdateAsync(document, ct);
        return status;
    }
}
