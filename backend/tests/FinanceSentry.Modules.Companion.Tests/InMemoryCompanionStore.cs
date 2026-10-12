namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The outbox on the in-memory provider for the job and capture tests. The repository's inserts and watermark are
/// raw <c>INSERT ... ON CONFLICT</c>, which only Postgres runs (covered in <c>CompanionUpsertTests</c>); here
/// those two are plain tracked writes and every read delegates to the real repository.
/// </summary>
internal sealed class InMemoryCompanionStore : ICompanionEventRepository, ICompanionCaptureStateRepository, IAsyncDisposable
{
    private readonly CompanionDbContext _db;
    private readonly CompanionEventRepository _events;

    public InMemoryCompanionStore(string name, ICurrentUser currentUser)
    {
        _db = new CompanionDbContext(new DbContextOptionsBuilder<CompanionDbContext>().UseInMemoryDatabase(name).Options, currentUser);
        _events = new CompanionEventRepository(_db);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    // A no-principal read matches nothing under the Owner filter, so assertions opt out like the jobs do.
    public Task<List<CompanionEvent>> AllEventsAsync()
        => _db.Events.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().ToListAsync();

    public async Task<bool> InsertIfNewAsync(CompanionEvent evt, CancellationToken ct = default)
    {
        if (await _db.Events.IgnoreQueryFilters([OwnerQueryFilter.Name]).AnyAsync(e => e.DedupKey == evt.DedupKey, ct))
            return false;

        _db.Events.Add(evt);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<IReadOnlyList<CompanionEvent>> ListByDispositionAsync(
        Guid userId, IReadOnlyCollection<EventDisposition> dispositions, int limit, CancellationToken ct = default)
        => _events.ListByDispositionAsync(userId, dispositions, limit, ct);

    public Task<IReadOnlyList<CompanionEvent>> ListRealtimePendingUnscopedAsync(int limit, CancellationToken ct = default)
        => _events.ListRealtimePendingUnscopedAsync(limit, ct);

    public Task<IReadOnlyList<CompanionEvent>> ListHeldForDigestUnscopedAsync(Guid userId, CancellationToken ct = default)
        => _events.ListHeldForDigestUnscopedAsync(userId, ct);

    public Task<IReadOnlyList<Guid>> ListHeldForDigestUserIdsUnscopedAsync(CancellationToken ct = default)
        => _events.ListHeldForDigestUserIdsUnscopedAsync(ct);

    public Task<int> CountDispatchedSinceUnscopedAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => _events.CountDispatchedSinceUnscopedAsync(userId, since, ct);

    public Task<CompanionEvent?> GetAsync(Guid id, CancellationToken ct = default) => _events.GetAsync(id, ct);

    public Task UpdateAsync(CompanionEvent evt, CancellationToken ct = default) => _events.UpdateAsync(evt, ct);

    public Task<int> MarkDeliveredAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => _events.MarkDeliveredAsync(userId, ids, ct);

    public Task<IReadOnlyList<CompanionEvent>> ListByDedupKeysAsync(
        Guid userId, IReadOnlyCollection<string> dedupKeys, CancellationToken ct = default)
        => _events.ListByDedupKeysAsync(userId, dedupKeys, ct);

    public Task<IReadOnlyList<CompanionEvent>> ListByOccurredRangeAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => _events.ListByOccurredRangeAsync(userId, from, to, ct);

    public async Task<DateTimeOffset> GetWatermarkAsync(string source, DateTimeOffset floor, CancellationToken ct = default)
        => (await _db.CaptureState.AsNoTracking().FirstOrDefaultAsync(s => s.Source == source, ct))?.Watermark ?? floor;

    public async Task SetWatermarkAsync(string source, DateTimeOffset watermark, CancellationToken ct = default)
    {
        var row = await _db.CaptureState.FirstOrDefaultAsync(s => s.Source == source, ct);
        if (row is null)
            _db.CaptureState.Add(new CompanionCaptureState { Source = source, Watermark = watermark });
        else
            row.Watermark = watermark;
        await _db.SaveChangesAsync(ct);
    }
}
