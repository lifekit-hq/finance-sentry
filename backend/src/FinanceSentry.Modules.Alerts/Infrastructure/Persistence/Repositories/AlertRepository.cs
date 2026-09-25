namespace FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class AlertRepository(AlertsDbContext db) : IAlertRepository
{
    private readonly AlertsDbContext _db = db;

    public async Task<(IReadOnlyList<Alert> Items, int TotalCount, int UnreadCount)> GetPagedAsync(
        Guid userId, string filter, int page, int pageSize, CancellationToken ct = default)
    {
        var baseQuery = _db.Alerts.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsDismissed);

        var filtered = filter?.ToLowerInvariant() switch
        {
            "unread" => baseQuery.Where(a => !a.IsRead),
            "error" => baseQuery.Where(a => a.Severity == AlertSeverity.Error),
            "warning" => baseQuery.Where(a => a.Severity == AlertSeverity.Warning),
            "info" => baseQuery.Where(a => a.Severity == AlertSeverity.Info),
            _ => baseQuery,
        };

        var totalCount = await filtered.CountAsync(ct);
        var unreadCount = await baseQuery.CountAsync(a => !a.IsRead && !a.IsResolved, ct);

        var items = await filtered
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount, unreadCount);
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
    {
        return _db.Alerts.AsNoTracking()
            .CountAsync(a => a.UserId == userId && !a.IsDismissed && !a.IsRead && !a.IsResolved, ct);
    }

    public Task<Alert?> FindActiveAsync(
        Guid userId, string type, Guid? referenceId, CancellationToken ct = default)
    {
        return _db.Alerts
            .Where(a => a.UserId == userId
                     && a.Type == type
                     && a.ReferenceId == referenceId
                     && !a.IsResolved
                     && !a.IsDismissed)
            .FirstOrDefaultAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid userId, string type, Guid? referenceId, CancellationToken ct = default)
    {
        return _db.Alerts.AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Type == type && a.ReferenceId == referenceId, ct);
    }

    public Task<bool> HasRecentAsync(
        Guid userId, string type, Guid? referenceId, string? referenceLabel, DateTimeOffset createdAfter,
        CancellationToken ct = default)
    {
        return _db.Alerts.AsNoTracking()
            .AnyAsync(a => a.UserId == userId
                        && a.Type == type
                        && a.ReferenceId == referenceId
                        && a.ReferenceLabel == referenceLabel
                        && a.CreatedAt >= createdAfter, ct);
    }

    public async Task<bool> MarkReadAsync(Guid userId, Guid alertId, CancellationToken ct = default)
    {
        var alert = await _db.Alerts.FirstOrDefaultAsync(a => a.Id == alertId && a.UserId == userId, ct);
        if (alert is null) return false;
        if (alert.IsRead) return true;
        alert.IsRead = true;
        alert.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.Alerts
            .Where(a => a.UserId == userId && !a.IsRead && !a.IsDismissed)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.IsRead, true)
                .SetProperty(a => a.UpdatedAt, now), ct);
    }

    public async Task<bool> DismissAsync(Guid userId, Guid alertId, CancellationToken ct = default)
    {
        var alert = await _db.Alerts.FirstOrDefaultAsync(a => a.Id == alertId && a.UserId == userId, ct);
        if (alert is null) return false;
        alert.IsDismissed = true;
        alert.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ResolveAsync(Guid alertId, CancellationToken ct = default)
    {
        var alert = await _db.Alerts.FirstOrDefaultAsync(a => a.Id == alertId, ct);
        if (alert is null || alert.IsResolved) return;
        var now = DateTimeOffset.UtcNow;
        alert.IsResolved = true;
        alert.ResolvedAt = now;
        alert.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public Task<int> PurgeOldAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        return _db.Alerts
            .Where(a => (a.IsResolved || a.IsDismissed) && a.CreatedAt < olderThan)
            .ExecuteDeleteAsync(ct);
    }

    public async Task DeleteByReferenceIdAsync(Guid referenceId, CancellationToken ct = default)
    {
        await _db.Alerts
            .Where(a => a.ReferenceId == referenceId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task AddAsync(Alert alert, CancellationToken ct = default)
    {
        _db.Alerts.Add(alert);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // A rejected insert must not stay tracked as Added, or every later save on this
            // (scoped) context retries it and fails the same way.
            _db.Entry(alert).State = EntityState.Detached;
            throw;
        }
    }

    public async Task<bool> AcknowledgeAsync(Guid userId, Guid alertId, string decision, CancellationToken ct = default)
    {
        var alert = await _db.Alerts.FirstOrDefaultAsync(a => a.Id == alertId && a.UserId == userId, ct);
        return await ApplyAcknowledgement(alert, decision, ct);
    }

    public async Task<bool> AcknowledgeByReferenceAsync(Guid userId, string alertType, Guid referenceId, string decision, CancellationToken ct = default)
    {
        var alert = await _db.Alerts
            .FirstOrDefaultAsync(a => a.UserId == userId
                                   && a.Type == alertType
                                   && a.ReferenceId == referenceId
                                   && !a.IsResolved
                                   && !a.IsDismissed, ct);
        return await ApplyAcknowledgement(alert, decision, ct);
    }

    private async Task<bool> ApplyAcknowledgement(Alert? alert, string decision, CancellationToken ct)
    {
        if (alert is null) return false;

        var now = DateTimeOffset.UtcNow;
        alert.AcknowledgementDecision = decision;
        alert.AcknowledgedAt = now;
        alert.UpdatedAt = now;

        if (decision == "Accept")
        {
            alert.IsResolved = true;
            alert.ResolvedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(IReadOnlyList<Alert> Items, int TotalCount)> GetByTypesPagedAsync(
        Guid userId, IReadOnlyCollection<string> types, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Alerts.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsDismissed && types.Contains(a.Type));

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
