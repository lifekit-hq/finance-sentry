namespace FinanceSentry.Modules.Alerts.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>Implements <see cref="IAlertFireHistoryReader"/> over the alerts table, any alert state.</summary>
public sealed class AlertFireHistoryReader(AlertsDbContext db) : IAlertFireHistoryReader
{
    public Task<DateTimeOffset?> GetLastRaisedAtAsync(Guid userId, string alertType, CancellationToken ct = default)
        => db.Alerts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Type == alertType)
            .MaxAsync(a => (DateTimeOffset?)a.CreatedAt, ct);
}
