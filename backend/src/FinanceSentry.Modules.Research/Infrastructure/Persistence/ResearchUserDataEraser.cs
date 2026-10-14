namespace FinanceSentry.Modules.Research.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Erases a person's Research data. Three tables need care beyond the owned-entity sweep: a thesis's news sources
/// point at it with <c>ON DELETE SET NULL</c>, so deleting the thesis first would turn them into market-wide
/// sources; candidate scores carry no foreign key to their candidate, so they would be left behind; and a research
/// document's <c>UserId</c> is nullable (the shared corpus), so it is not <c>IUserOwned</c> - its chunks and
/// embeddings go with it through the cascade.
/// </summary>
internal sealed class ResearchUserDataEraser(ResearchDbContext db) : IUserDataEraser
{
    public string Module => "Research";

    public async Task<int> EraseAsync(Guid userId, CancellationToken ct = default)
    {
        var theses = db.Theses.IgnoreQueryFilters().Where(t => t.UserId == userId).Select(t => t.Id);
        var candidates = db.OpportunityCandidates.IgnoreQueryFilters().Where(c => c.UserId == userId).Select(c => c.Id);

        var removed = await db.NewsSources
            .Where(s => s.ThesisId != null && theses.Contains(s.ThesisId.Value))
            .ExecuteDeleteAsync(ct);
        removed += await db.CandidateScores.Where(s => candidates.Contains(s.CandidateId)).ExecuteDeleteAsync(ct);
        removed += await db.ResearchDocuments.IgnoreQueryFilters().Where(d => d.UserId == userId).ExecuteDeleteAsync(ct);

        return removed + await OwnedRowEraser.EraseAsync(db, userId, ct);
    }
}
