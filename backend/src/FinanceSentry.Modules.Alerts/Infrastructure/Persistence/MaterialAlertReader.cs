namespace FinanceSentry.Modules.Alerts.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Domain;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Implements the companion read contract (feature 031) over the alerts table — active alerts created
/// after a watermark. Keeps the Companion module decoupled from the Alerts internals. Its caller is the
/// companion capture job, which reads every user's new alerts, so it opts out of the Owner query filter.
/// </summary>
public class MaterialAlertReader(AlertsDbContext db) : IMaterialAlertReader, IPushAlertReader
{
    private const int MaxLimit = 500;

    public async Task<IReadOnlyList<MaterialAlertRecord>> GetNewSinceAsync(
        DateTimeOffset watermark, int limit, CancellationToken ct = default)
    {
        var effective = Math.Clamp(limit, 1, MaxLimit);
        var rows = await db.Alerts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(a => a.CreatedAt > watermark && !a.IsDismissed)
            .OrderBy(a => a.CreatedAt)
            .Take(effective)
            .ToListAsync(ct);
        return rows
            .Select(a => new MaterialAlertRecord(
                a.Id, a.UserId, a.Type, a.Severity, a.Title, a.ReferenceId, a.ReferenceLabel, a.CreatedAt,
                AlertAppPaths.For(a)))
            .ToList();
    }

    public async Task<IReadOnlySet<Guid>> GetOpenIdsAsync(
        IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
    {
        if (alertIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var open = await db.Alerts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(a => alertIds.Contains(a.Id) && !a.IsDismissed && a.ResolvedAt == null)
            .Select(a => a.Id)
            .ToListAsync(ct);
        return open.ToHashSet();
    }

    public async Task<IReadOnlyList<MaterialAlertRecord>> ListOfTypesSinceAsync(
        IReadOnlyCollection<string> types, DateTimeOffset since, CancellationToken ct = default)
    {
        var rows = await db.Alerts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(a => types.Contains(a.Type) && a.CreatedAt >= since && !a.IsDismissed)
            .OrderBy(a => a.CreatedAt)
            .Take(MaxLimit)
            .ToListAsync(ct);
        return [.. rows.Select(ToRecord)];
    }

    public async Task<IReadOnlyDictionary<Guid, MaterialAlertRecord>> GetOpenAsync(
        IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
    {
        if (alertIds.Count == 0)
        {
            return new Dictionary<Guid, MaterialAlertRecord>();
        }

        var rows = await db.Alerts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(a => alertIds.Contains(a.Id) && !a.IsDismissed)
            .ToListAsync(ct);
        return rows.Select(ToRecord).ToDictionary(r => r.AlertId);
    }

    private static MaterialAlertRecord ToRecord(Alert a) => new(
        a.Id, a.UserId, a.Type, a.Severity, a.Title, a.ReferenceId, a.ReferenceLabel, a.CreatedAt, AlertAppPaths.For(a));
}
