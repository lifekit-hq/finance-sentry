namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// The request path runs under the Owner filter. The forwarder runs with no person in scope, so it calls the
// <c>…Unscoped…</c> methods, which opt out explicitly.
public class ProblemReportRepository(CompanionDbContext db) : IProblemReportRepository
{
    public async Task<ProblemReport> AddAsync(ProblemReport report, CancellationToken ct = default)
    {
        db.ProblemReports.Add(report);
        await db.SaveChangesAsync(ct);
        return report;
    }

    public async Task<IReadOnlyList<DateTimeOffset>> ListCreatedAtSinceAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => await db.ProblemReports.AsNoTracking()
            .Where(r => r.UserId == userId && r.CreatedAt >= since)
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProblemReport>> ListDueUnscopedAsync(DateTimeOffset now, int limit, CancellationToken ct = default)
        => await db.ProblemReports.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(r => r.Status == ProblemReportStatus.Pending && (r.NextAttemptAt == null || r.NextAttemptAt <= now))
            .OrderBy(r => r.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

    public async Task UpdateUnscopedAsync(ProblemReport report, CancellationToken ct = default)
    {
        db.ProblemReports.Update(report);
        await db.SaveChangesAsync(ct);
    }
}
