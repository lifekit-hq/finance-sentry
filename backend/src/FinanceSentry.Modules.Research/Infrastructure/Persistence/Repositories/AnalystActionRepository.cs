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

        // Pull any already-stored rows that collide with this batch (by the four identity fields).
        var tickers = incoming.Values.Select(a => a.Ticker).Distinct().ToArray();
        var dates = incoming.Values.Select(a => a.ActionDate).Distinct().ToArray();
        var stored = await db.AnalystActions
            .Where(a => tickers.Contains(a.Ticker) && dates.Contains(a.ActionDate))
            .ToListAsync(ct);
        var storedByKey = stored.ToDictionary(ActionKey.From);

        var inserted = 0;
        foreach (var (key, candidate) in incoming)
        {
            if (storedByKey.TryGetValue(key, out var row))
            {
                Merge(row, candidate);
            }
            else
            {
                db.AnalystActions.Add(candidate);
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        return inserted;
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
