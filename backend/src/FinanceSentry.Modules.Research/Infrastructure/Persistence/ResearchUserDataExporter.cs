namespace FinanceSentry.Modules.Research.Infrastructure.Persistence;

using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Exports a person's Research data over the same rows <see cref="ResearchUserDataEraser"/> deletes: the owned-entity
/// sweep, plus the three tables it handles by hand - a thesis's news sources and a candidate's scores (no
/// <c>UserId</c> of their own, reached through their parent) and the person's own research documents. A document's
/// chunks and embeddings are derived index data and are not exported; the shared corpus (a document with no
/// <c>UserId</c>) is not the person's.
/// </summary>
internal sealed class ResearchUserDataExporter(ResearchDbContext db) : IUserDataExporter
{
    public string Module => "Research";

    public async Task ExportAsync(Guid userId, Utf8JsonWriter writer, CancellationToken ct = default)
    {
        await OwnedRowExporter.ExportAsync(db, userId, writer, ct);

        var theses = db.Theses.IgnoreQueryFilters().Where(t => t.UserId == userId).Select(t => t.Id);
        var candidates = db.OpportunityCandidates.IgnoreQueryFilters().Where(c => c.UserId == userId).Select(c => c.Id);

        await OwnedRowExporter.WriteRowsAsync<NewsSource>(
            db, s => s.ThesisId != null && theses.Contains(s.ThesisId.Value), writer, ct);
        await OwnedRowExporter.WriteRowsAsync<CandidateScore>(db, s => candidates.Contains(s.CandidateId), writer, ct);
        await OwnedRowExporter.WriteRowsAsync<ResearchDocument>(db, d => d.UserId == userId, writer, ct);
    }
}
