namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// A list-backed journal for write-path tests: the production repository orders by a <see cref="DateTimeOffset"/>,
/// which the SQLite store the thesis fixture uses cannot translate. Rows are the real <see cref="ThesisEvent"/>s the
/// real <c>ThesisEventRecorder</c> appends, so a test asserts on exactly what would be persisted.
/// </summary>
public sealed class InMemoryThesisEventRepository : IThesisEventRepository
{
    public List<ThesisEvent> Rows { get; } = [];

    public Task AppendAsync(ThesisEvent thesisEvent, CancellationToken ct = default)
    {
        // Distinct, ordered timestamps even when appended within one clock tick.
        if (Rows.Count > 0 && thesisEvent.Timestamp <= Rows[^1].Timestamp)
        {
            thesisEvent.Timestamp = Rows[^1].Timestamp.AddMilliseconds(1);
        }

        Rows.Add(thesisEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ThesisEvent>> ListAsync(Guid userId, Guid? subjectId = null, CancellationToken ct = default)
        => ListUnscopedAsync(userId, subjectId, ct);

    public Task<IReadOnlyList<ThesisEvent>> ListUnscopedAsync(Guid userId, Guid? subjectId = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ThesisEvent>>(Rows
            .Where(e => e.UserId == userId && (subjectId is null || e.SubjectId == subjectId))
            .OrderBy(e => e.Timestamp)
            .ToList());

    public Task<IReadOnlyList<ThesisEvent>> ListPendingUnscopedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ThesisEvent>>(Rows.Where(e => e.PricesPending).ToList());

    public Task<IReadOnlyList<ThesisEvent>> ListForPeriodAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => ListUnscopedAsync(userId, null, ct);

    public Task<ThesisEvent?> GetLatestForSubjectUnscopedAsync(
        Guid userId, ThesisSubjectType subjectType, Guid subjectId, CancellationToken ct = default)
        => Task.FromResult(Rows
            .Where(e => e.UserId == userId && e.SubjectType == subjectType && e.SubjectId == subjectId)
            .MaxBy(e => e.Timestamp));

    public Task<IReadOnlyList<Guid>> GetUserIdsWithEventsUnscopedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Guid>>(Rows.Select(e => e.UserId).Distinct().ToList());

    public Task UpdatePricesAsync(ThesisEvent thesisEvent, CancellationToken ct = default) => Task.CompletedTask;

    public Task<ThesisEvent?> FindAsync(Guid userId, Guid eventId, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(e => e.UserId == userId && e.Id == eventId));

    public Task<IReadOnlyDictionary<Guid, ThesisEventOrigin>> GetLatestOriginsAsync(
        Guid userId,
        ThesisSubjectType subjectType,
        IReadOnlyCollection<Guid> subjectIds,
        IReadOnlyCollection<ThesisEventType> eventTypes,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, ThesisEventOrigin>>(Rows
            .Where(e => e.UserId == userId && e.SubjectType == subjectType
                && subjectIds.Contains(e.SubjectId) && eventTypes.Contains(e.EventType))
            .GroupBy(e => e.SubjectId)
            .ToDictionary(g => g.Key, g => g.MaxBy(e => e.Timestamp)!.Origin));
}
