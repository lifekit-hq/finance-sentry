namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Puts a sized proposal in the queue (#1069) and keeps the journal whole: an older pending proposal on the same ticker
/// is marked <see cref="RecommendationStatus.Superseded"/> with its own journal event before the new one goes in, and
/// the new one gets its <see cref="ThesisEventType.Created"/> event. Shared by Ledger's proposals and the rule sweep,
/// so every read here is job-safe.
/// </summary>
public sealed class RecommendationWriter(IRecommendationRepository repo, IThesisEventRecorder recorder)
{
    private const int CreatedNoteLength = 300;

    public async Task<Recommendation> WriteAsync(Recommendation recommendation, CancellationToken ct)
    {
        var existing = await repo.FindPendingUnscopedAsync(recommendation.UserId, recommendation.Ticker, ct);
        if (existing is not null)
        {
            // Supersede first: the partial unique index allows one Pending row per ticker.
            existing.Status = RecommendationStatus.Superseded;
            existing.DecidedAt = DateTimeOffset.UtcNow;
            existing.SupersededById = recommendation.Id;
            existing.Execution = RecommendationExecution.NotApplicable;
            await repo.UpdateAsync(existing, ct);
            await recorder.RecordAsync(
                existing.UserId, ThesisSubjectType.Recommendation, existing.Id, existing.Ticker,
                ThesisEventType.Superseded, $"Superseded by a newer {recommendation.Action} proposal", ct);
        }

        await repo.AddPendingAsync(recommendation, ct);
        await recorder.RecordAsync(
            recommendation.UserId, ThesisSubjectType.Recommendation, recommendation.Id, recommendation.Ticker,
            ThesisEventType.Created, CreatedNote(recommendation), ct);
        return recommendation;
    }

    private static string CreatedNote(Recommendation r)
    {
        var head = $"{r.Action.ToString().ToUpperInvariant()} {r.Ticker}: ";
        var room = CreatedNoteLength - head.Length;
        var rationale = r.Rationale.Length <= room ? r.Rationale : string.Concat(r.Rationale.AsSpan(0, room - 1), "…");
        return head + rationale;
    }
}
