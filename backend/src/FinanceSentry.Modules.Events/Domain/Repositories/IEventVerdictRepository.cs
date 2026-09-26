namespace FinanceSentry.Modules.Events.Domain.Repositories;

public interface IEventVerdictRepository
{
    /// <summary>Insert, or replace the existing verdict for the same (user, companion event).</summary>
    Task UpsertAsync(EventVerdict verdict, CancellationToken ct = default);

    /// <summary>Verdicts recorded on any of the given alerts, whether or not their companion rows still exist.</summary>
    Task<IReadOnlyList<EventVerdict>> ListByAlertIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default);

    /// <summary>Verdicts recorded on any of the given companion events - the join every event kind can use,
    /// including one with no alert behind it.</summary>
    Task<IReadOnlyList<EventVerdict>> ListByCompanionEventIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> companionEventIds, CancellationToken ct = default);
}
