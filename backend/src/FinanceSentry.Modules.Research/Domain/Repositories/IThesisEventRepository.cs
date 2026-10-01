namespace FinanceSentry.Modules.Research.Domain.Repositories;

public interface IThesisEventRepository
{
    Task AppendAsync(ThesisEvent thesisEvent, CancellationToken ct = default);

    Task<IReadOnlyList<ThesisEvent>> ListAsync(
        Guid userId, Guid? subjectId = null, CancellationToken ct = default);

    /// <summary>The user's events for jobs and the handlers they share, which can run with no person in scope. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<ThesisEvent>> ListUnscopedAsync(
        Guid userId, Guid? subjectId = null, CancellationToken ct = default);

    /// <summary>Every user's events still awaiting prices, for the snapshot job's backfill. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<ThesisEvent>> ListPendingUnscopedAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ThesisEvent>> ListForPeriodAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// The user's latest event for the subject, for the recorder's one-Created-per-subject check, which jobs reach
    /// with no person in scope. Opts out of the Owner query filter.
    /// </summary>
    Task<ThesisEvent?> GetLatestForSubjectUnscopedAsync(
        Guid userId, ThesisSubjectType subjectType, Guid subjectId, CancellationToken ct = default);

    /// <summary>Every user with an event, for the snapshot job's sweep. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsWithEventsUnscopedAsync(CancellationToken ct = default);

    /// <summary>Stamps backfilled prices on the event. The snapshot job writes with no person in scope, so the lookup opts out of the Owner query filter.</summary>
    Task UpdatePricesAsync(ThesisEvent thesisEvent, CancellationToken ct = default);
}
