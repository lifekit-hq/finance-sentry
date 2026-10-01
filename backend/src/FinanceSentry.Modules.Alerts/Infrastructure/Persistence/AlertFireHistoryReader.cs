namespace FinanceSentry.Modules.Alerts.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Implements <see cref="IAlertFireHistoryReader"/> over the alerts table, any alert state. Its caller is a
/// background monitor job with no person in scope, so it opts out of the Owner query filter and scopes by the
/// user it is given.
/// </summary>
public sealed class AlertFireHistoryReader(AlertsDbContext db) : IAlertFireHistoryReader
{
    public Task<DateTimeOffset?> GetLastRaisedAtAsync(Guid userId, string alertType, CancellationToken ct = default)
        => db.Alerts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(a => a.UserId == userId && a.Type == alertType)
            .MaxAsync(a => (DateTimeOffset?)a.CreatedAt, ct);
}
