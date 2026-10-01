namespace FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under RadarDbContext's Owner query filter: global signals plus the acting person's own. The writer's
// dedup check, the retention prune and the weekly brief run with no person in scope (or must see every holder's
// rows), so they use the …Unscoped… methods, which opt out of the filter and keep their own predicates.
public sealed class RadarSignalRepository(RadarDbContext db) : IRadarSignalRepository
{
    private IQueryable<RadarSignal> AllUsers => db.RadarSignals.IgnoreQueryFilters([OwnerQueryFilter.Name]);

    public async Task AppendAsync(RadarSignal signal, CancellationToken ct = default)
    {
        db.RadarSignals.Add(signal);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> HasRecentUnscopedAsync(string dedupKey, DateTimeOffset since, CancellationToken ct = default)
        => AllUsers.AsNoTracking()
            .AnyAsync(s => s.DedupKey == dedupKey && s.Timestamp >= since, ct);

    public Task<IReadOnlyList<RadarSignal>> ListAsync(SignalFilter filter, CancellationToken ct = default)
        => ListAsync(db.RadarSignals, filter, ct);

    public Task<IReadOnlyList<RadarSignal>> ListForUserUnscopedAsync(
        Guid userId, SignalFilter filter, CancellationToken ct = default)
        => ListAsync(AllUsers, filter with { UserId = userId }, ct);

    public async Task<int> PruneInfoBeforeUnscopedAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => await AllUsers
            .Where(s => s.Severity == SignalSeverity.Info && s.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct);

    private static async Task<IReadOnlyList<RadarSignal>> ListAsync(
        IQueryable<RadarSignal> source, SignalFilter filter, CancellationToken ct)
    {
        var query = source.AsNoTracking();

        if (filter.Since is not null)
        {
            query = query.Where(s => s.Timestamp >= filter.Since.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Scanner))
        {
            query = query.Where(s => s.Scanner == filter.Scanner);
        }

        if (!string.IsNullOrWhiteSpace(filter.SignalType))
        {
            query = query.Where(s => s.SignalType == filter.SignalType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Subject))
        {
            query = query.Where(s => s.Subject == filter.Subject);
        }

        if (filter.UserId is not null)
        {
            // Global signals (breadth, rotation, unusual moves on non-held tickers) carry no
            // UserId; a user-scoped read must still see them or the market disappears.
            query = query.Where(s => s.UserId == null || s.UserId == filter.UserId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Severity) &&
            Enum.TryParse<SignalSeverity>(filter.Severity, ignoreCase: true, out var severity))
        {
            query = query.Where(s => s.Severity == severity);
        }

        return await query.OrderByDescending(s => s.Timestamp).ToListAsync(ct);
    }
}
