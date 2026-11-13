namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public sealed class CatalystRepository(ResearchDbContext db) : ICatalystRepository
{
    public async Task<IReadOnlySet<string>> ListHandledUnscopedAsync(Guid userId, DateOnly since, CancellationToken ct = default)
    {
        var rows = await db.CatalystEvents.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.UserId == userId && e.FilingDate >= since)
            .Select(e => new { e.Ticker, e.Accession })
            .ToListAsync(ct);
        return rows.Select(r => Key(r.Ticker, r.Accession)).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<int> AddEventsUnscopedAsync(
        Guid userId, IReadOnlyCollection<CatalystEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        var tickers = events.Select(e => e.Ticker).Distinct().ToList();
        var existing = (await db.CatalystEvents.AsNoTracking()
                .IgnoreQueryFilters([OwnerQueryFilter.Name])
                .Where(e => e.UserId == userId && tickers.Contains(e.Ticker))
                .Select(e => new { e.Ticker, e.Accession })
                .ToListAsync(ct))
            .Select(r => Key(r.Ticker, r.Accession))
            .ToHashSet(StringComparer.Ordinal);

        var fresh = events
            .Where(e => existing.Add(Key(e.Ticker, e.Accession)))
            .ToList();
        foreach (var row in fresh)
        {
            row.UserId = userId;
        }

        try
        {
            db.CatalystEvents.AddRange(fresh);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return fresh.Count;
    }

    public async Task<(IReadOnlyList<CatalystEvent> Events, int Total)> ListEventsUnscopedAsync(
        Guid userId, DateTimeOffset since, int take, CancellationToken ct = default)
    {
        var query = db.CatalystEvents.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.UserId == userId && e.DetectedAt >= since);
        var total = await query.CountAsync(ct);
        var events = await query
            .OrderByDescending(e => e.DetectedAt)
            .ThenBy(e => e.Ticker)
            .Take(take)
            .ToListAsync(ct);
        return (events, total);
    }

    public async Task<int> PruneEventsUnscopedAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        var old = await db.CatalystEvents
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.DetectedAt < before)
            .ToListAsync(ct);
        try
        {
            db.CatalystEvents.RemoveRange(old);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return old.Count;
    }

    /// <summary>The identity of a filing for a user: EDGAR never reuses an accession number, and one filing can name several tickers.</summary>
    public static string Key(string ticker, string accession) => ticker + "|" + accession;
}

public sealed class ThemeAttentionRepository(ResearchDbContext db) : IThemeAttentionRepository
{
    public async Task<int> UpsertDaysAsync(IReadOnlyCollection<ThemeAttentionDay> days, CancellationToken ct = default)
    {
        if (days.Count == 0)
        {
            return 0;
        }

        var themes = days.Select(d => d.Theme).Distinct().ToList();
        var from = days.Min(d => d.Day);
        var existing = await db.ThemeAttentionDays
            .Where(d => themes.Contains(d.Theme) && d.Day >= from)
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(d => (d.Theme, d.Phrase, d.Day));

        try
        {
            foreach (var day in days.GroupBy(d => (d.Theme, d.Phrase, d.Day)).Select(g => g.Last()))
            {
                if (byKey.TryGetValue((day.Theme, day.Phrase, day.Day), out var row))
                {
                    row.Articles = day.Articles;
                    row.TotalArticles = day.TotalArticles;
                    row.Source = day.Source;
                    row.MeasuredAt = day.MeasuredAt;
                }
                else
                {
                    db.ThemeAttentionDays.Add(day);
                }
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return days.GroupBy(d => (d.Theme, d.Phrase, d.Day)).Count();
    }

    public async Task<IReadOnlyList<ThemeAttentionDay>> ListDaysAsync(string? theme, DateOnly since, CancellationToken ct = default)
    {
        var query = db.ThemeAttentionDays.AsNoTracking().Where(d => d.Day >= since);
        if (!string.IsNullOrWhiteSpace(theme))
        {
            var key = theme.Trim().ToLowerInvariant();
            query = query.Where(d => d.Theme == key);
        }

        return await query.OrderBy(d => d.Theme).ThenBy(d => d.Phrase).ThenBy(d => d.Day).ToListAsync(ct);
    }

    public async Task<int> PruneAsync(DateOnly before, CancellationToken ct = default)
    {
        var old = await db.ThemeAttentionDays.Where(d => d.Day < before).ToListAsync(ct);
        try
        {
            db.ThemeAttentionDays.RemoveRange(old);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return old.Count;
    }
}
