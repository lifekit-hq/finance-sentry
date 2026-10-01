namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under the Owner filter. The snapshot job, the event recorder's dedup check and the handlers jobs share
// run with no person in scope, so they call the …Unscoped… methods, which opt out explicitly; the price backfill
// does the same internally. Each opted-out query keeps its own UserId predicate, or is an explicit all-users sweep
// (the backfill matches by event id from that sweep).
public class ThesisEventRepository(ResearchDbContext db) : IThesisEventRepository
{
    public async Task AppendAsync(ThesisEvent thesisEvent, CancellationToken ct = default)
    {
        db.ThesisEvents.Add(thesisEvent);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ThesisEvent>> ListAsync(
        Guid userId, Guid? subjectId = null, CancellationToken ct = default)
    {
        var query = db.ThesisEvents.AsNoTracking().Where(e => e.UserId == userId);

        if (subjectId is { } id)
        {
            query = query.Where(e => e.SubjectId == id);
        }

        return await query.OrderBy(e => e.Timestamp).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ThesisEvent>> ListUnscopedAsync(
        Guid userId, Guid? subjectId = null, CancellationToken ct = default)
    {
        var query = db.ThesisEvents.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.UserId == userId);

        if (subjectId is { } id)
        {
            query = query.Where(e => e.SubjectId == id);
        }

        return await query.OrderBy(e => e.Timestamp).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ThesisEvent>> ListPendingUnscopedAsync(CancellationToken ct = default)
        => await db.ThesisEvents
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.PricesPending)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ThesisEvent>> ListForPeriodAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromTimestamp = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toTimestamp = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        return await db.ThesisEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.Timestamp >= fromTimestamp && e.Timestamp <= toTimestamp)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(ct);
    }

    public Task<ThesisEvent?> GetLatestForSubjectUnscopedAsync(
        Guid userId, ThesisSubjectType subjectType, Guid subjectId, CancellationToken ct = default)
        => db.ThesisEvents.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.UserId == userId && e.SubjectType == subjectType && e.SubjectId == subjectId)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetUserIdsWithEventsUnscopedAsync(CancellationToken ct = default)
        => await db.ThesisEvents.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Select(e => e.UserId)
            .Distinct()
            .ToListAsync(ct);

    public async Task UpdatePricesAsync(ThesisEvent thesisEvent, CancellationToken ct = default)
    {
        var existing = await db.ThesisEvents
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(e => e.Id == thesisEvent.Id && e.UserId == thesisEvent.UserId, ct);
        if (existing is null)
        {
            return;
        }

        existing.SubjectPrice = thesisEvent.SubjectPrice;
        existing.BenchmarkPrice = thesisEvent.BenchmarkPrice;
        existing.PricesPending = thesisEvent.PricesPending;

        await db.SaveChangesAsync(ct);
    }
}
