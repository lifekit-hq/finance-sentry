namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job, paired with <see cref="GeopoliticsSourceSeedJob"/>: retires a thesis-owned news
/// source once its thesis's text no longer mentions any of the <see cref="GeopoliticsTermMatcher"/>
/// terms that earned the source in the first place — the thesis was edited and the geopolitical angle
/// dropped out, so the source keeps polling under a thesis that no longer says what it said.
/// <para>
/// This covers only the "text no longer matches" half of thesis-source staleness. The other half —
/// the thesis itself was deleted — is handled synchronously by
/// <see cref="Application.Commands.DeleteThesisCommandHandler"/> at delete time, not here: the
/// <c>news_sources.ThesisId</c> foreign key is <c>ON DELETE SET NULL</c>, so by the time a nightly
/// sweep could look, a deleted thesis's id would already be gone from the row — there is nothing left
/// to key a "was this thesis deleted" check off of.
/// </para>
/// <para>
/// Retirement disables the source (<see cref="Domain.NewsSource.Enabled"/> = false) and records
/// <see cref="Domain.NewsSource.RetiredAt"/>/<see cref="Domain.NewsSource.RetiredReason"/> rather than
/// deleting the row, so a source that stops feeding stays visible for someone to find and explain.
/// </para>
/// <para>
/// Health and thesis-relevance never fight: this job only reads <c>ThesisId</c> and thesis text — it
/// never inspects <see cref="Domain.NewsSource.ConsecutiveFailures"/> or the current
/// <see cref="Domain.NewsSource.Enabled"/> value to decide anything, so a source that is merely failing
/// stays the health tracker's problem. A source already retired (<c>RetiredReason != null</c>) is
/// skipped so repeated runs never re-touch it, and <see cref="INewsSourceRepository.ListDisabledAsync"/>
/// excludes retired rows, so <see cref="NewsSourceRecoveryJob"/> never re-probes and revives one just
/// because its feed still happens to fetch.
/// </para>
/// </summary>
public sealed class ThesisSourceRetirementJob(
    ResearchDbContext research,
    INewsSourceRepository sources,
    ILogger<ThesisSourceRetirementJob> logger)
{
    private const string RetiredReasonText = "Thesis text no longer mentions a matched geopolitics term";

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var all = await sources.ListAllAsync(ct);
        var candidates = all.Where(s => s.ThesisId is not null && s.RetiredReason is null).ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var thesisIds = candidates.Select(s => s.ThesisId!.Value).Distinct().ToList();
        var thesisTexts = await research.Theses.AsNoTracking()
            .Where(t => thesisIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ThesisText })
            .ToDictionaryAsync(t => t.Id, t => t.ThesisText, ct);

        var retired = 0;
        foreach (var source in candidates)
        {
            // Owning thesis already gone: DeleteThesisCommandHandler retires a source at delete time,
            // and the FK has already nulled ThesisId on any row that predates this feature — either
            // way there is nothing for this sweep to key an "orphaned" decision off of here.
            if (!thesisTexts.TryGetValue(source.ThesisId!.Value, out var thesisText))
            {
                continue;
            }

            if (GeopoliticsTermMatcher.MatchTerms(thesisText).Count > 0)
            {
                continue;
            }

            source.Enabled = false;
            source.RetiredAt = DateTimeOffset.UtcNow;
            source.RetiredReason = RetiredReasonText;
            await sources.UpdateAsync(source, ct);
            retired++;
        }

        logger.LogInformation(
            "ThesisSourceRetirementJob retired {Count} sources whose thesis text no longer matches", retired);
    }
}
