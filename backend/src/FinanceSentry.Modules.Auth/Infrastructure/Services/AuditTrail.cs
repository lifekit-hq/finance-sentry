using System.Diagnostics;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

/// <summary>
/// <see cref="IAuditTrail"/> over <c>auth.audit_events</c>. Saves through a context of its own and commits before the
/// caller carries on - the request's <see cref="AuthDbContext"/> may hold unrelated pending changes that an audit
/// write must neither flush nor roll back with - and lets any failure through: see <see cref="IAuditTrail"/> for why a
/// trail that fails must not be swallowed.
/// </summary>
internal sealed class AuditTrail(
    DbContextOptions<AuthDbContext> options, ICurrentUser currentUser, TimeProvider clock) : IAuditTrail
{
    public async Task RecordAsync(IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return;

        var traceId = Activity.Current?.TraceId.ToHexString() ?? string.Empty;
        var now = clock.GetUtcNow().UtcDateTime;

        await using var db = new AuthDbContext(options, currentUser);
        db.AuditEvents.AddRange(entries.Select(entry => new AuditEvent(entry, now, traceId)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
