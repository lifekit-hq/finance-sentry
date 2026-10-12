namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class AnalystActionRepository(ResearchDbContext db) : IAnalystActionRepository
{
    private const int MaxLimit = 200;

    public async Task<int> UpsertAsync(IReadOnlyCollection<AnalystAction> actions, CancellationToken ct = default)
    {
        if (actions.Count == 0)
        {
            return 0;
        }

        // Collapse in-batch duplicates first (two sources in the same run), merging richer fields.
        var incoming = new Dictionary<ActionKey, AnalystAction>();
        foreach (var a in actions)
        {
            var key = ActionKey.From(a);
            if (incoming.TryGetValue(key, out var existing))
            {
                Merge(existing, a);
            }
            else
            {
                incoming[key] = a;
            }
        }

        // One INSERT ... ON CONFLICT over the unique (Ticker, Firm, ActionDate, ActionType) index: the write is
        // atomic, so two overlapping ingests merge into the same rows instead of the loser failing on the index,
        // and no read precedes it. It runs at once rather than at SaveChanges. A stored row only has its NULL
        // rating/target/url fields filled, never overwritten (keep the richer record - FR-003); RETURNING
        // (xmax = 0) is true only for a freshly inserted row, which is what the inserted count reports.
        var rows = incoming.Values.ToList();
        var ids = rows.Select(a => a.Id).ToArray();
        var tickers = rows.Select(a => a.Ticker).ToArray();
        var firms = rows.Select(a => a.Firm).ToArray();
        var actionTypes = rows.Select(a => a.ActionType.ToString()).ToArray();
        var priorRatings = rows.Select(a => a.PriorRating).ToArray();
        var newRatings = rows.Select(a => a.NewRating).ToArray();
        var priorTargets = rows.Select(a => a.PriorTarget).ToArray();
        var newTargets = rows.Select(a => a.NewTarget).ToArray();
        var actionDates = rows.Select(a => a.ActionDate).ToArray();
        var sources = rows.Select(a => a.Source).ToArray();
        var sourceUrls = rows.Select(a => a.SourceUrl).ToArray();
        var ingestedAts = rows.Select(a => a.IngestedAt).ToArray();

        var inserted = await db.Database.SqlQuery<bool>(
            $"""
            INSERT INTO research.analyst_actions AS t
                ("Id", "Ticker", "Firm", "ActionType", "PriorRating", "NewRating", "PriorTarget", "NewTarget",
                 "ActionDate", "Source", "SourceUrl", "IngestedAt")
            SELECT * FROM unnest(
                {ids}::uuid[], {tickers}::varchar[], {firms}::varchar[], {actionTypes}::varchar[],
                {priorRatings}::varchar[], {newRatings}::varchar[], {priorTargets}::numeric[], {newTargets}::numeric[],
                {actionDates}::date[], {sources}::varchar[], {sourceUrls}::varchar[], {ingestedAts}::timestamptz[])
            ON CONFLICT ("Ticker", "Firm", "ActionDate", "ActionType") DO UPDATE SET
                "PriorRating" = COALESCE(t."PriorRating", EXCLUDED."PriorRating"),
                "NewRating" = COALESCE(t."NewRating", EXCLUDED."NewRating"),
                "PriorTarget" = COALESCE(t."PriorTarget", EXCLUDED."PriorTarget"),
                "NewTarget" = COALESCE(t."NewTarget", EXCLUDED."NewTarget"),
                "SourceUrl" = COALESCE(t."SourceUrl", EXCLUDED."SourceUrl")
            RETURNING (xmax = 0) AS "Value"
            """).ToListAsync(ct);

        return inserted.Count(isNew => isNew);
    }

    public async Task<IReadOnlyList<AnalystAction>> QueryAsync(
        string? ticker,
        DateOnly since,
        AnalystActionType? actionType,
        int limit,
        CancellationToken ct = default)
    {
        var effective = Math.Clamp(limit, 1, MaxLimit);
        var q = db.AnalystActions.AsNoTracking().Where(a => a.ActionDate >= since);

        if (!string.IsNullOrWhiteSpace(ticker))
        {
            var upper = ticker.Trim().ToUpperInvariant();
            q = q.Where(a => a.Ticker == upper);
        }

        if (actionType is { } type)
        {
            q = q.Where(a => a.ActionType == type);
        }

        return await q
            .OrderByDescending(a => a.ActionDate)
            .ThenByDescending(a => a.IngestedAt)
            .Take(effective)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AnalystAction>> ListRatingChangesAsync(
        IReadOnlyCollection<string> tickers,
        DateOnly since,
        CancellationToken ct = default)
    {
        if (tickers.Count == 0)
        {
            return [];
        }

        var upper = tickers.Select(t => t.Trim().ToUpperInvariant()).Distinct().ToArray();
        return await db.AnalystActions.AsNoTracking()
            .Where(a => a.ActionDate >= since
                && (a.ActionType == AnalystActionType.Upgrade || a.ActionType == AnalystActionType.Downgrade)
                && upper.Contains(a.Ticker))
            .OrderBy(a => a.ActionDate)
            .ThenBy(a => a.Firm)
            .ToListAsync(ct);
    }

    public async Task<AnalystAction?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.AnalystActions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    // Fill NULL target/rating fields on <paramref name="target"/> from <paramref name="source"/>;
    // never overwrite an already-populated field (keep the richer record — FR-003).
    private static void Merge(AnalystAction target, AnalystAction source)
    {
        target.PriorRating ??= source.PriorRating;
        target.NewRating ??= source.NewRating;
        target.PriorTarget ??= source.PriorTarget;
        target.NewTarget ??= source.NewTarget;
        target.SourceUrl ??= source.SourceUrl;
    }

    private readonly record struct ActionKey(string Ticker, string Firm, DateOnly ActionDate, AnalystActionType ActionType)
    {
        public static ActionKey From(AnalystAction a)
            => new(a.Ticker, a.Firm.ToUpperInvariant(), a.ActionDate, a.ActionType);
    }
}
