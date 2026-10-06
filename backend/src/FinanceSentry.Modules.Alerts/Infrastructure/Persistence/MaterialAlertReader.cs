namespace FinanceSentry.Modules.Alerts.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Implements the companion read contract (feature 031) over the alerts table — active alerts created
/// after a watermark. Keeps the Companion module decoupled from the Alerts internals. Its caller is the
/// companion capture job, which reads every user's new alerts, so it opts out of the Owner query filter.
/// </summary>
public class MaterialAlertReader(AlertsDbContext db) : IMaterialAlertReader
{
    private const int MaxLimit = 500;

    public async Task<IReadOnlyList<MaterialAlertRecord>> GetNewSinceAsync(
        DateTimeOffset watermark, int limit, CancellationToken ct = default)
    {
        var effective = Math.Clamp(limit, 1, MaxLimit);
        return await db.Alerts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(a => a.CreatedAt > watermark && !a.IsDismissed)
            .OrderBy(a => a.CreatedAt)
            .Take(effective)
            .Select(a => new MaterialAlertRecord(
                a.Id, a.UserId, a.Type, a.Severity, a.Title, a.ReferenceId, a.ReferenceLabel, a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlySet<Guid>> GetResolvedIdsAsync(
        IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
    {
        if (alertIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var resolved = await db.Alerts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(a => alertIds.Contains(a.Id) && a.ResolvedAt != null)
            .Select(a => a.Id)
            .ToListAsync(ct);
        return resolved.ToHashSet();
    }
}
