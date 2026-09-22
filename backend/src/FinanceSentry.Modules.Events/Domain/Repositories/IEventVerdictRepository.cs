namespace FinanceSentry.Modules.Events.Domain.Repositories;

public interface IEventVerdictRepository
{
    /// <summary>Insert, or replace the existing verdict for the same (user, companion event).</summary>
    Task UpsertAsync(EventVerdict verdict, CancellationToken ct = default);

    Task<IReadOnlyList<EventVerdict>> ListByEventIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> companionEventIds, CancellationToken ct = default);
}
